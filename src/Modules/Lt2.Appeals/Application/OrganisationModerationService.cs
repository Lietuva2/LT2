using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// Moderation of organisations and their statements after the fact (design 8.14.9, 8.14.14): new and changed organisations
/// and statements that wait, are flagged, reported or not yet seen come to the queue to be looked at, not approved. Every
/// action is recorded in <c>appeals.moderation_action</c> and the audit log in one transaction, and emailed to the
/// organisation's representatives with the statement and the moderators' contact address. Nothing here reads a position
/// or names a representative; only restricting a statement's writer reaches that person, as with an argument.
/// </summary>
internal sealed class OrganisationModerationService(
    IDbContextFactory<AppealsDb> contexts,
    OrganisationReads reads,
    AppealReads appealReads,
    ModerationService moderation,
    ModerationRecorder recorder,
    IOutbox outbox,
    IOrganisationCodeSwitch codes,
    TimeProvider clock) : IOrganisationModeration
{
    private const int QueueLimit = 100;
    private static readonly string[] Kinds = ["organisation", "statement"];

    public async Task<OrganisationModerationItems> QueueAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var reports = await OpenReportsAsync(db, cancellationToken);
        var reported = reports.Select(r => r.TargetId).ToHashSet();
        var organisations = (await db.Organisations.AsNoTracking().Where(o => o.Status != OrganisationStatuses.Closed).ToListAsync(cancellationToken))
            .Where(o => (o.Status == OrganisationStatuses.Active && o.SeenAt == null) || reported.Contains(o.Id))
            .OrderBy(o => o.ChangedAt)
            .Take(QueueLimit)
            .ToList();
        var ids = organisations.Select(o => o.Id).ToList();
        var history = (await db.Revisions.AsNoTracking()
                .Where(r => r.EntityKind == "organisation" && ids.Contains(r.EntityId) && (r.Field == "name" || r.Field == "website"))
                .OrderBy(r => r.ChangedAt)
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.EntityId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProfileChange>)g.Select(r => new ProfileChange(r.ChangedAt, r.Field, r.OldValue, r.NewValue)).ToList());

        var statements = await (from s in db.OrganisationStatements.AsNoTracking()
                                join o in db.Organisations.AsNoTracking() on s.OrganisationId equals o.Id
                                where o.Status == OrganisationStatuses.Active
                                    && ((s.Status == StatementStatuses.Pending && (s.SeenAt == null || reported.Contains(s.Id)))
                                        || (s.Status == StatementStatuses.Published && (s.SeenAt == null || reported.Contains(s.Id))))
                                orderby s.PublishedAt
                                select s)
            .Take(QueueLimit)
            .ToListAsync(cancellationToken);
        return new OrganisationModerationItems(
            organisations.Select(o => QueueItem(o, reports, history.GetValueOrDefault(o.Id) ?? [])).ToList(),
            await QueueStatementsAsync(db, statements, reports, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<QueueStatement>>> OnBillAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await reads.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        var statements = await db.OrganisationStatements.AsNoTracking()
            .Where(s => s.BillNumber == bill.Bill.Number && (s.Status == StatementStatuses.Pending || s.Status == StatementStatuses.Published))
            .OrderBy(s => s.PublishedAt)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<QueueStatement>>.Success(await QueueStatementsAsync(db, statements, await OpenReportsAsync(db, cancellationToken), cancellationToken));
    }

    public async Task<HiddenOrganisationItems> HiddenAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var blocked = await db.Organisations.AsNoTracking().Where(o => o.Status == OrganisationStatuses.Blocked).ToListAsync(cancellationToken);
        var hidden = await db.OrganisationStatements.AsNoTracking().Where(s => s.Status == StatementStatuses.Hidden).ToListAsync(cancellationToken);
        var targets = hidden.Select(s => s.Id).ToList();
        var hides = (await db.ModerationActions.AsNoTracking().Where(m => targets.Contains(m.TargetId) && m.Action == "hide").ToListAsync(cancellationToken))
            .GroupBy(m => m.TargetId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First());
        var organisationIds = hidden.Select(s => s.OrganisationId).Distinct().ToList();
        var organisationOf = await db.Organisations.AsNoTracking().Where(o => organisationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var names = await appealReads.NamesAsync(blocked.Select(o => o.BlockedByUserId).OfType<Guid>().Concat(hides.Values.Select(m => m.ModeratorUserId)), cancellationToken);
        var billsOf = await reads.BillsAsync(db, hidden.Select(s => s.BillNumber).ToList(), cancellationToken);

        return new HiddenOrganisationItems(
            blocked.OrderByDescending(o => o.BlockedAt)
                .Select(o => new HiddenOrganisation(o.Id, o.Slug, o.Name, o.BlockedAt, o.BlockedByUserId is { } by ? names.GetValueOrDefault(by) : null, o.BlockStatement))
                .ToList(),
            hidden.Select(s =>
                {
                    var hide = hides.GetValueOrDefault(s.Id);
                    var bill = billsOf.GetValueOrDefault(AppealReads.NormaliseBillNumber(s.BillNumber));
                    return new HiddenStatement(
                        s.Id, bill?.Bill ?? new BillFacts(s.BillNumber, s.BillNumber), organisationOf[s.OrganisationId].Name, s.Stance, s.Text,
                        hide?.CreatedAt, hide is null ? null : names.GetValueOrDefault(hide.ModeratorUserId), hide?.Statement, bill?.Open == true);
                })
                .OrderByDescending(s => s.HiddenAt ?? DateTimeOffset.MinValue)
                .ToList());
    }

    public Task<Result<Unit>> KeepAsync(Guid organisationId, string? note, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(note, required: false) ?? OnOrganisationAsync(organisationId, moderator, "keep", StatementRules.Normalise(note), OrganisationNotices.Kept, contact, null, (_, o, now) =>
        {
            if (o.Status != OrganisationStatuses.Active)
            {
                return Task.FromResult<string?>("moderation.wrong-state");
            }

            o.SeenAt = now;
            return Task.FromResult<string?>(null);
        }, cancellationToken);

    public Task<Result<Unit>> BlockAsync(Guid organisationId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnOrganisationAsync(organisationId, moderator, "block", statement.Trim(), OrganisationNotices.Blocked, contact, OrganisationCodeStates.Blocked, (_, o, now) =>
        {
            if (o.Status != OrganisationStatuses.Active)
            {
                return Task.FromResult<string?>("moderation.wrong-state");
            }

            // Statements keep their own statuses: a blocked organisation's are shown nowhere, and lifting the block shows
            // exactly the published ones again (design 8.14.14).
            o.Status = OrganisationStatuses.Blocked;
            o.BlockedAt = now;
            o.BlockedByUserId = moderator.UserId;
            o.BlockStatement = statement.Trim();
            o.SeenAt = now;
            return Task.FromResult<string?>(null);
        }, cancellationToken);

    public Task<Result<Unit>> UnblockAsync(Guid organisationId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnOrganisationAsync(organisationId, moderator, "unblock", statement.Trim(), OrganisationNotices.Unblocked, contact, OrganisationCodeStates.Active, async (db, o, now) =>
        {
            if (o.Status != OrganisationStatuses.Blocked)
            {
                return "moderation.wrong-state";
            }

            // While it was blocked its name was free for others; it comes back only if nobody took it meanwhile.
            if (await db.Organisations.AnyAsync(x => x.Id != o.Id && x.NameKey == o.NameKey && x.Status == OrganisationStatuses.Active, cancellationToken))
            {
                return "organisation.name-taken";
            }

            o.Status = OrganisationStatuses.Active;
            o.BlockedAt = null;
            o.BlockedByUserId = null;
            o.BlockStatement = null;
            o.SeenAt = now;
            return null;
        }, cancellationToken);

    public Task<Result<Unit>> ConfirmAsync(Guid organisationId, string note, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(note, required: true) ?? OnOrganisationAsync(organisationId, moderator, "confirm", note.Trim(), OrganisationNotices.Confirmed, contact, null, (_, o, now) =>
        {
            if (o.Status != OrganisationStatuses.Active || o.ConfirmedBy is not null)
            {
                return Task.FromResult<string?>("moderation.wrong-state");
            }

            o.ConfirmedBy = ConfirmationKinds.Moderator;
            o.ConfirmedDomain = null;
            o.ConfirmedAt = now;
            o.ConfirmedByUserId = moderator.UserId;
            o.ConfirmationNote = note.Trim();
            o.SeenAt = now;
            return Task.FromResult<string?>(null);
        }, cancellationToken);

    public Task<Result<Unit>> WithdrawConfirmationAsync(Guid organisationId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnOrganisationAsync(organisationId, moderator, "unconfirm", statement.Trim(), OrganisationNotices.ConfirmationWithdrawn, contact, null, async (db, o, now) =>
        {
            if (o.ConfirmedBy is null || o.Status == OrganisationStatuses.Closed)
            {
                return "moderation.wrong-state";
            }

            // From now on only a moderator can confirm it: codes already sent confirm nothing (design 8.14.14).
            o.ConfirmedBy = null;
            o.ConfirmedDomain = null;
            o.ConfirmedAt = null;
            o.ConfirmedByUserId = null;
            o.ConfirmationNote = null;
            o.DomainBarred = true;
            await db.OrganisationCodes.Where(c => c.OrganisationId == o.Id && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), cancellationToken);
            return null;
        }, cancellationToken);

    public Task<Result<Unit>> PublishStatementAsync(Guid statementId, string? statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: false) ?? OnStatementAsync(statementId, moderator, "publish", StatementRules.Normalise(statement), contact, async (db, s, bill, now) =>
        {
            // A statement that still waited when the bill closed is never published (design 6.9).
            if (OrganisationRules.CheckPublish(s.Status, bill?.Finished == true) is { } refused)
            {
                return ((string?)refused, (string?)null);
            }

            var waited = s.Status == StatementStatuses.Pending;
            s.Status = StatementStatuses.Published;
            s.SeenAt = now;

            // A statement that waited becomes public now: its followers are told from this moment (design 8.14.14, stage B),
            // and it is the bill's activity (design 8.12.5).
            s.FirstPublicAt ??= now;
            if (waited)
            {
                await RecordActivity.TouchBillAsync(db, s.BillNumber, now, cancellationToken);
            }

            return ((string?)null, (string?)(waited ? OrganisationNotices.StatementPublished : OrganisationNotices.StatementKept));
        }, cancellationToken);

    /// <summary>
    /// "Palikti" on a statement that still waited when the bill closed (design 6.9): it stays unpublished, is marked seen
    /// and leaves the queue unless reported again; its representatives are told it was looked at and left as it is.
    /// </summary>
    public Task<Result<Unit>> KeepStatementAsync(Guid statementId, string? statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: false) ?? OnStatementAsync(statementId, moderator, "keep", StatementRules.Normalise(statement), contact, (_, s, bill, now) =>
        {
            if (OrganisationRules.CheckKeepWaiting(s.Status, bill?.Finished == true) is { } refused)
            {
                return Task.FromResult<(string?, string?)>((refused, null));
            }

            s.SeenAt = now;
            return Task.FromResult<(string?, string?)>((null, OrganisationNotices.StatementKept));
        }, cancellationToken);

    public Task<Result<Unit>> HideStatementAsync(Guid statementId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnStatementAsync(statementId, moderator, "hide", statement.Trim(), contact, (_, s, _, now) =>
        {
            if (s.Status is not (StatementStatuses.Pending or StatementStatuses.Published))
            {
                return Task.FromResult<(string?, string?)>(("moderation.wrong-state", null));
            }

            s.Status = StatementStatuses.Hidden;
            s.SeenAt = now;
            return Task.FromResult<(string?, string?)>((null, OrganisationNotices.StatementHidden));
        }, cancellationToken);

    public Task<Result<Unit>> RestoreStatementAsync(Guid statementId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? OnStatementAsync(statementId, moderator, "restore", statement.Trim(), contact, (_, s, bill, now) =>
        {
            if (s.Status != StatementStatuses.Hidden || bill?.Open != true)
            {
                return Task.FromResult<(string?, string?)>(("moderation.wrong-state", null));
            }

            s.Status = StatementStatuses.Published;
            s.SeenAt = now;
            s.FirstPublicAt ??= now;
            return Task.FromResult<(string?, string?)>((null, OrganisationNotices.StatementRestored));
        }, cancellationToken);

    public Task<Result<AuthorView>> WriterOfStatementAsync(Guid statementId, CancellationToken cancellationToken) =>
        moderation.ViewAsync(db => db.OrganisationStatements.Where(s => s.Id == statementId).Select(s => s.WrittenByUserId), "statement.not-found", cancellationToken);

    public Task<Result<Guid>> RestrictWriterOfStatementAsync(Guid statementId, DateOnly? until, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        moderation.RestrictAsync(db => db.OrganisationStatements.Where(s => s.Id == statementId).Select(s => s.WrittenByUserId), "statement.not-found", until, statement, moderator, contact, cancellationToken);

    /// <summary>A moderator's statement checked where it is stored (design 6.8): the refusal, or null when it passes.</summary>
    private static Task<Result<Unit>>? Refused(string? statement, bool required) =>
        StatementRules.Check(statement, required) is { } broken ? Task.FromResult<Result<Unit>>(RuleErrors.Of(broken)) : null;

    private static Task<List<Report>> OpenReportsAsync(AppealsDb db, CancellationToken ct) =>
        db.Reports.AsNoTracking().Where(r => r.Status == "open" && Kinds.Contains(r.TargetKind)).OrderBy(r => r.CreatedAt).ToListAsync(ct);

    private static List<QueueReport> ReportsOn(IReadOnlyList<Report> reports, Guid id) =>
        reports.Where(r => r.TargetId == id).Select(r => new QueueReport(r.Id, r.Ground, r.Text, r.CreatedAt)).ToList();

    private static QueueOrganisation QueueItem(Organisation o, IReadOnlyList<Report> reports, IReadOnlyList<ProfileChange> history) => new(
        o.Id, o.Slug, o.Name, o.Description, o.Website, o.LegalCode, o.DeclaredLobbyist, OrganisationReads.Label(o), o.Status, o.SeenAt is not null,
        history.Count > 0, o.ScreeningFlags, ReportsOn(reports, o.Id), history, o.ConfirmationNote, o.DomainBarred, o.RegisteredAt);

    private async Task<IReadOnlyList<QueueStatement>> QueueStatementsAsync(AppealsDb db, IReadOnlyList<OrganisationStatement> statements, IReadOnlyList<Report> reports, CancellationToken ct)
    {
        var ids = statements.Select(s => s.OrganisationId).Distinct().ToList();
        var organisations = await db.Organisations.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var billsOf = await reads.BillsAsync(db, statements.Select(s => s.BillNumber).ToList(), ct);
        return statements.Select(s =>
        {
            var o = organisations[s.OrganisationId];
            var bill = billsOf.GetValueOrDefault(AppealReads.NormaliseBillNumber(s.BillNumber));
            return new QueueStatement(
                s.Id, bill?.Bill ?? new BillFacts(s.BillNumber, s.BillNumber), o.Id, o.Slug, o.Name, OrganisationReads.Label(o),
                s.Stance, s.Text, s.Link, s.Goal, s.Status, s.SeenAt is not null, s.ScreeningFlags, ReportsOn(reports, s.Id), s.PublishedAt,
                bill?.Finished == true);
        }).ToList();
    }

    private delegate Task<string?> OrganisationAction(AppealsDb db, Organisation organisation, DateTimeOffset now);

    /// <summary>Returns a refusal code, or null and the notice to send.</summary>
    private delegate Task<(string? Refusal, string? Notice)> StatementAction(AppealsDb db, OrganisationStatement statement, BillForStatements? bill, DateTimeOffset now);

    private async Task<Result<Unit>> OnOrganisationAsync(
        Guid organisationId, StaffActor moderator, string action, string? statement, string notice, string? contact, string? codeState, OrganisationAction apply, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLocks.OrganisationAsync(db, organisationId, ct);
        var o = await db.Organisations.FirstOrDefaultAsync(x => x.Id == organisationId, ct);
        if (o is null)
        {
            return Error.NotFound("organisation.not-found");
        }

        var now = clock.GetUtcNow();
        if (await apply(db, o, now) is { } refused)
        {
            return refused == "moderation.wrong-state" ? Error.Conflict(refused) : OrganisationErrors.Of(refused);
        }

        // The organisation's invitation code follows a block and its lifting (design 8.14.14, stage B), in this transaction.
        if (codeState is not null)
        {
            await codes.SetAsync(o.Id, codeState, transaction.GetDbTransaction(), ct);
        }

        o.ChangedAt = now;
        await recorder.RecordAsync(db, transaction, "organisation", o.Id, action, statement, moderator, now, null, new { organisation = o.Slug }, answersReports: true, ct);
        await NotifyAsync(db, transaction, o, notice, $"/o/{Uri.EscapeDataString(o.Slug)}", statement, contact, null, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    private async Task<Result<Unit>> OnStatementAsync(
        Guid statementId, StaffActor moderator, string action, string? statement, string? contact, StatementAction apply, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var s = await db.OrganisationStatements.FirstOrDefaultAsync(x => x.Id == statementId, ct);
        if (s is null)
        {
            return Error.NotFound("statement.not-found");
        }

        await AdvisoryLocks.OrganisationAsync(db, s.OrganisationId, ct);
        var o = await db.Organisations.AsNoTracking().FirstAsync(x => x.Id == s.OrganisationId, ct);
        if (o.Status == OrganisationStatuses.Closed)
        {
            return Error.Conflict("moderation.wrong-state");
        }

        var bill = await reads.BillAsync(db, s.BillNumber, ct);
        var now = clock.GetUtcNow();
        var (refused, notice) = await apply(db, s, bill, now);
        if (refused is not null)
        {
            return Error.Conflict(refused);
        }

        await recorder.RecordAsync(db, transaction, "statement", s.Id, action, statement, moderator, now, s.BillNumber, new { organisation = o.Slug }, answersReports: true, ct);
        if (notice is not null)
        {
            await NotifyAsync(db, transaction, o, notice, $"/seimas/{Uri.EscapeDataString(s.BillNumber)}", statement, contact, bill?.Bill ?? new BillFacts(s.BillNumber, s.BillNumber), ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    /// <summary>Each current representative is told what a moderator did and why (design 8.14.14); never who the others are.</summary>
    private async Task NotifyAsync(
        AppealsDb db, IDbContextTransaction transaction, Organisation o, string notice, string path, string? statement, string? contact, BillFacts? bill, CancellationToken ct)
    {
        foreach (var address in await reads.RepresentativeAddressesAsync(db, o.Id, ct))
        {
            await outbox.EnqueueAsync(
                OutboxMessage.Create(MessageKinds.OrganisationNoticeEmail, new OrganisationNoticeEmail(address, notice, o.Name, path, statement, contact, bill?.Number, bill?.Title)),
                transaction.GetDbTransaction(),
                ct);
        }
    }
}
