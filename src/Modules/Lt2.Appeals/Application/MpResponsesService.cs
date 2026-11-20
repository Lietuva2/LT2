using System.Text.Json;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.Appeals.Screening;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// MPs' responses on bills (design 8.5.5, 8.5.6) and their moderation after the fact. A response is public at once; every
/// change keeps the text it replaces as a public version (<c>appeals.revision</c>, kind <c>response</c>, field <c>version</c>,
/// without a person), and a withdrawn one stays shown with its versions; a response written again after a withdrawal is the
/// same row, so its history continues. Frozen at the adoption vote, after which the MP's ballot at that vote is given beside
/// it. Nothing here logs or returns who wrote a response, and nothing touches a position, a count or a summary.
/// </summary>
internal sealed class MpResponsesService(
    IDbContextFactory<AppealsDb> contexts,
    OrganisationReads bills,
    AppealReads reads,
    IBillCatalogue catalogue,
    IPreScreening screening,
    ModerationRecorder recorder,
    IMpLinks links,
    IMpDirectory mps,
    IOutbox outbox,
    ISettings settings,
    TimeProvider clock) : IMpResponses, IMpResponseModeration
{
    private const string Kind = ReportTargets.Response;
    private const int QueueLimit = 100;

    public async Task<Result<BillResponses>> OnBillAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await bills.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        if (bill.Hidden)
        {
            // Taking part is closed by the moderators: the page shows the bill only (design 8.12.3).
            return new BillResponses(bill.Bill.Number, false, true, []);
        }

        var rows = await db.MpResponses.AsNoTracking().Where(r => r.BillNumber == bill.Bill.Number).ToListAsync(cancellationToken);
        var versions = await VersionsAsync(db, rows, cancellationToken);
        var votes = await VotesAsync(bill, rows.Select(r => r.SeimasPersonId).ToList(), cancellationToken);
        var comments = await CommentCascade.CountsAsync(db, CommentKinds.Response, rows.Select(r => r.Id).ToList(), cancellationToken);
        return new BillResponses(
            bill.Bill.Number,
            bill.Open,
            false,
            rows.Select(r => View(r, versions.GetValueOrDefault(r.Id) ?? [], votes.GetValueOrDefault(r.SeimasPersonId)) with { Comments = comments.GetValueOrDefault(r.Id) }).ToList());
    }

    public async Task<IReadOnlyList<PersonResponse>> OfPersonAsync(long seimasPersonId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = (await db.MpResponses.AsNoTracking().Where(r => r.SeimasPersonId == seimasPersonId).ToListAsync(cancellationToken))
            .OrderByDescending(LastChange)
            .ThenBy(r => r.BillNumber, StringComparer.Ordinal)
            .ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var billsOf = await bills.BillsAsync(db, rows.Select(r => r.BillNumber).ToList(), cancellationToken);
        var versions = await VersionsAsync(db, rows, cancellationToken);
        var result = new List<PersonResponse>(rows.Count);
        foreach (var r in rows)
        {
            var bill = billsOf.GetValueOrDefault(r.BillNumber);
            var vote = bill is null ? null : (await VotesAsync(bill, [seimasPersonId], cancellationToken)).GetValueOrDefault(seimasPersonId);
            result.Add(new PersonResponse(bill?.Bill ?? new BillFacts(r.BillNumber, r.BillNumber), bill?.Open == true, View(r, versions.GetValueOrDefault(r.Id) ?? [], vote)));
        }

        return result;
    }

    public async Task<Result<OwnResponse>> OwnAsync(string billNumber, long seimasPersonId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await bills.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        var row = await db.MpResponses.AsNoTracking().FirstOrDefaultAsync(r => r.SeimasPersonId == seimasPersonId && r.BillNumber == bill.Bill.Number, cancellationToken);
        var refusal = ResponseRules.CheckBillOpen(bill.Record?.Status, bill.Finished) ?? ResponseRules.CheckWritable(row?.Status);
        if (row is null)
        {
            return new OwnResponse(bill.Bill.Number, refusal is null, refusal, null);
        }

        var versions = await VersionsAsync(db, [row], cancellationToken);
        var vote = (await VotesAsync(bill, [seimasPersonId], cancellationToken)).GetValueOrDefault(seimasPersonId);
        return new OwnResponse(bill.Bill.Number, refusal is null, refusal, View(row, versions.GetValueOrDefault(row.Id) ?? [], vote));
    }

    public async Task<IReadOnlyDictionary<string, ResponseView>> OnBillsAsync(long seimasPersonId, IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken)
    {
        var numbers = billNumbers.Select(AppealReads.NormaliseBillNumber).Distinct().ToList();
        if (numbers.Count == 0)
        {
            return new Dictionary<string, ResponseView>(StringComparer.Ordinal);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.MpResponses.AsNoTracking()
            .Where(r => r.SeimasPersonId == seimasPersonId && numbers.Contains(r.BillNumber))
            .ToListAsync(cancellationToken);
        var versions = await VersionsAsync(db, rows, cancellationToken);

        // These bills are open (they are in summaries now): no ballot yet.
        return rows.ToDictionary(r => r.BillNumber, r => View(r, versions.GetValueOrDefault(r.Id) ?? [], null), StringComparer.Ordinal);
    }

    public async Task<Result<ResponseSaved>> SaveAsync(string billNumber, long seimasPersonId, Guid writerUserId, NewResponse request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Design 5.6, 6.8 (owner, 2026-11-16): the length is the members' texts setting, checked here for every caller.
        var maxLength = await settings.GetAsync(SettingKeys.TextsMaxLength, cancellationToken);
        if (ResponseRules.CheckResponse(request.Stance, request.Text, maxLength) is { } broken)
        {
            return broken == "response.too-long" ? Errors(broken) with { Numbers = [MemberTextLength.Allowed(maxLength)] } : Errors(broken);
        }

        var text = AppealRules.NormaliseText(request.Text);

        // Design 8.5.5: the same screening as for statements; a flag puts the response in the queue and holds nothing back.
        var flags = await screening.ScreenAsync(text, cancellationToken);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await bills.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        await AdvisoryLocks.MpResponseAsync(db, seimasPersonId, bill.Bill.Number, cancellationToken);
        var now = clock.GetUtcNow();
        if (await WritingRestrictionReads.IsRestrictedAsync(db, writerUserId, now, cancellationToken))
        {
            return Error.Forbidden("writing.restricted");
        }

        if (ResponseRules.CheckBillOpen(bill.Record?.Status, bill.Finished) is { } closed)
        {
            return Errors(closed);
        }

        var existing = await db.MpResponses.FirstOrDefaultAsync(r => r.SeimasPersonId == seimasPersonId && r.BillNumber == bill.Bill.Number, cancellationToken);
        if (ResponseRules.CheckWritable(existing?.Status) is { } hidden)
        {
            return Errors(hidden);
        }

        if (existing is null)
        {
            existing = new MpResponse
            {
                Id = Ids.New(clock),
                BillNumber = bill.Bill.Number,
                SeimasPersonId = seimasPersonId,
                PublishedAt = now,
            };
            db.MpResponses.Add(existing);
        }
        else
        {
            if (ResponseRules.Unchanged(existing.Status, existing.Stance, existing.Text, request.Stance, text))
            {
                return new ResponseSaved(existing.Id, existing.Status);
            }

            // Nothing is silently replaced (design 8.5.5): the text now shown becomes an earlier version.
            if (ResponseRules.KeepsVersion(existing.Status))
            {
                db.Revisions.Add(AppealsService.Revise(Kind, existing.Id, "version", VersionJson(existing, withdrawn: false), null, null, now));
            }

            existing.EditedAt = now;
            existing.WithdrawnAt = null;
        }

        // Design 8.20.1: a first publication or a changed stance is what followers are told about the next morning.
        if (ResponseRules.Announces(existing.Stance.Length == 0 ? null : existing.Stance, request.Stance))
        {
            existing.AnnouncedAt = now;
        }

        existing.Stance = request.Stance;
        existing.Text = text;
        existing.Status = ResponseStatuses.Published;
        existing.ScreeningFlags = [.. flags];
        existing.SeenAt = null;
        existing.WrittenByUserId = writerUserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);

            // Design 8.12.5: a response made public or changed is the bill's activity.
            await RecordActivity.TouchBillAsync(db, bill.Bill.Number, now, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Two first responses at the same moment: one stands.
            return Error.Conflict("response.exists");
        }

        await transaction.CommitAsync(cancellationToken);
        return new ResponseSaved(existing.Id, existing.Status);
    }

    public async Task<Result<Unit>> WithdrawAsync(string billNumber, long seimasPersonId, Guid writerUserId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await bills.BillAsync(db, billNumber, cancellationToken) is not { } bill)
        {
            return Error.NotFound("bill.not-found");
        }

        await AdvisoryLocks.MpResponseAsync(db, seimasPersonId, bill.Bill.Number, cancellationToken);
        var response = await db.MpResponses.FirstOrDefaultAsync(r => r.SeimasPersonId == seimasPersonId && r.BillNumber == bill.Bill.Number, cancellationToken);
        if (ResponseRules.CheckWithdraw(response?.Status) is { } missing)
        {
            return response?.Status == ResponseStatuses.Hidden ? Errors("response.hidden") : Error.NotFound(missing);
        }

        if (ResponseRules.CheckBillOpen(bill.Record?.Status, bill.Finished) is { } closed)
        {
            return Errors(closed);
        }

        var now = clock.GetUtcNow();

        // The withdrawn text stays public, among the versions, marked withdrawn (design 8.5.5).
        db.Revisions.Add(AppealsService.Revise(Kind, response!.Id, "version", VersionJson(response, withdrawn: true), null, null, now));
        response.Status = ResponseStatuses.Withdrawn;
        response.WithdrawnAt = now;
        response.WrittenByUserId = writerUserId;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<ResponseModerationItems> ItemsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var reports = await db.Reports.AsNoTracking().Where(r => r.Status == "open" && r.TargetKind == Kind).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var reported = reports.Select(r => r.TargetId).Distinct().ToList();
        var due = db.MpResponses.AsNoTracking()
            .Where(r => (r.Status == ResponseStatuses.Published || r.Status == ResponseStatuses.Withdrawn)
                && ((r.SeenAt == null && r.ScreeningFlags.Length > 0) || reported.Contains(r.Id)));
        var queued = await due.OrderBy(r => r.EditedAt ?? r.PublishedAt).Take(QueueLimit).ToListAsync(cancellationToken);
        var allHidden = db.MpResponses.AsNoTracking().Where(r => r.Status == ResponseStatuses.Hidden);
        var hidden = await allHidden.OrderByDescending(r => r.HiddenAt).Take(QueueLimit).ToListAsync(cancellationToken);

        // Owner, 2026-11-18: the lists still show the first hundred, and say so when there are more.
        var queuedTotal = queued.Count < QueueLimit ? queued.Count : await due.CountAsync(cancellationToken);
        var hiddenTotal = hidden.Count < QueueLimit ? hidden.Count : await allHidden.CountAsync(cancellationToken);
        var hiddenIds = hidden.Select(r => r.Id).ToList();
        var hides = (await db.ModerationActions.AsNoTracking().Where(m => m.TargetKind == Kind && hiddenIds.Contains(m.TargetId) && m.Action == "hide").ToListAsync(cancellationToken))
            .GroupBy(m => m.TargetId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAt).First());
        var names = await reads.NamesAsync(hides.Values.Select(m => m.ModeratorUserId), cancellationToken);
        var billsOf = await bills.BillsAsync(db, queued.Concat(hidden).Select(r => r.BillNumber).ToList(), cancellationToken);
        BillFacts Facts(string number) => billsOf.GetValueOrDefault(number)?.Bill ?? new BillFacts(number, number);
        return new ResponseModerationItems(
            queued.Select(r => new QueueResponse(
                r.Id, Facts(r.BillNumber), r.SeimasPersonId, r.Status, r.Stance, r.Text, r.ScreeningFlags,
                reports.Where(p => p.TargetId == r.Id).Select(p => new QueueReport(p.Id, p.Ground, p.Text, p.CreatedAt)).ToList(),
                r.PublishedAt, r.EditedAt, billsOf.GetValueOrDefault(r.BillNumber)?.Open == true)).ToList(),
            hidden.Select(r =>
            {
                var hide = hides.GetValueOrDefault(r.Id);
                return new HiddenResponse(
                    r.Id, Facts(r.BillNumber), r.SeimasPersonId, r.Stance, r.Text, r.HiddenAt, hide is null ? null : names.GetValueOrDefault(hide.ModeratorUserId), r.HideStatement,
                    ResponseRules.MayRestore(r.Status, billsOf.GetValueOrDefault(r.BillNumber)?.Open == true));
            }).ToList())
        {
            QueueTotal = queuedTotal,
            HiddenTotal = hiddenTotal,
        };
    }

    public Task<Result<Unit>> KeepAsync(Guid responseId, string? note, StaffActor moderator, CancellationToken cancellationToken) =>
        Refused(note, required: false) ?? ActAsync(responseId, moderator, "keep", StatementRules.Normalise(note), null, (r, _, now) =>
        {
            if (!ResponseRules.MayKeep(r.Status))
            {
                return (false, null);
            }

            r.SeenAt = now;
            return (true, null);
        }, cancellationToken);

    public Task<Result<Unit>> HideAsync(Guid responseId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? ActAsync(responseId, moderator, "hide", statement.Trim(), contact, (r, _, now) =>
        {
            if (!ResponseRules.MayHide(r.Status))
            {
                return (false, null);
            }

            r.Status = ResponseStatuses.Hidden;
            r.HiddenAt = now;
            r.HideStatement = statement.Trim();
            r.SeenAt = now;
            return (true, MpResponseNotices.Hidden);
        }, cancellationToken);

    public Task<Result<Unit>> RestoreAsync(Guid responseId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        Refused(statement, required: true) ?? ActAsync(responseId, moderator, "restore", statement.Trim(), contact, (r, bill, now) =>
        {
            if (!ResponseRules.MayRestore(r.Status, bill?.Open == true))
            {
                return (false, null);
            }

            r.Status = ResponseRules.RestoredStatus(r.WithdrawnAt);
            r.HiddenAt = null;
            r.HideStatement = null;
            r.SeenAt = now;
            return (true, MpResponseNotices.Restored);
        }, cancellationToken);

    /// <summary>A moderator's statement checked where it is stored (design 6.8): the refusal, or null when it passes.</summary>
    private static Task<Result<Unit>>? Refused(string? statement, bool required) =>
        StatementRules.Check(statement, required) is { } broken ? Task.FromResult<Result<Unit>>(RuleErrors.Of(broken)) : null;

    /// <summary>Applies the action; false when the response's state does not allow it. Returns the notice to send, if any.</summary>
    private delegate (bool Applied, string? Notice) ResponseAction(MpResponse response, BillForStatements? bill, DateTimeOffset now);

    private async Task<Result<Unit>> ActAsync(
        Guid responseId, StaffActor moderator, string action, string? statement, string? contact, ResponseAction apply, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var found = await db.MpResponses.AsNoTracking().FirstOrDefaultAsync(r => r.Id == responseId, ct);
        if (found is null)
        {
            return Error.NotFound("response.not-found");
        }

        await AdvisoryLocks.MpResponseAsync(db, found.SeimasPersonId, found.BillNumber, ct);
        var response = await db.MpResponses.FirstAsync(r => r.Id == responseId, ct);
        var bill = await bills.BillAsync(db, response.BillNumber, ct);
        var now = clock.GetUtcNow();
        var (applied, notice) = apply(response, bill, now);
        if (!applied)
        {
            return Error.Conflict("moderation.wrong-state");
        }

        await recorder.RecordAsync(db, transaction, Kind, response.Id, action, statement, moderator, now, response.BillNumber, new { mp = response.SeimasPersonId }, answersReports: true, ct);
        if (notice is not null)
        {
            await NotifyAsync(transaction, response, bill?.Bill ?? new BillFacts(response.BillNumber, response.BillNumber), notice, statement, contact, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// The MP is told what a moderator did and why (design 8.5.5), at the MP's account's own address; with no account linked as
    /// the MP now, nobody is (the statement stays public in the response's place). Never who wrote the response.
    /// </summary>
    private async Task NotifyAsync(IDbContextTransaction transaction, MpResponse response, BillFacts bill, string notice, string? statement, string? contact, CancellationToken ct)
    {
        if (await links.MpAccountAddressAsync(response.SeimasPersonId, ct) is not { } address)
        {
            return;
        }

        var name = (await mps.MpsAsync([response.SeimasPersonId], ct)).TryGetValue(response.SeimasPersonId, out var mp) ? mp.FirstName + " " + mp.LastName : "";
        await outbox.EnqueueAsync(
            OutboxMessage.Create(
                MessageKinds.MpResponseNoticeEmail,
                new MpResponseNoticeEmail(address, notice, name, bill.Number, bill.Title, $"/seimas/{Uri.EscapeDataString(bill.Number)}#atsakymai", statement, contact)),
            transaction.GetDbTransaction(),
            ct);
    }

    private static Error Errors(string code) => code switch
    {
        "response.stance-invalid" => Error.Validation(code, "stance"),
        "response.empty" or "response.too-long" => Error.Validation(code, "text"),
        _ => Error.Conflict(code),
    };

    private static DateTimeOffset LastChange(MpResponse r) => new[] { r.PublishedAt, r.EditedAt ?? r.PublishedAt, r.WithdrawnAt ?? r.PublishedAt }.Max();

    /// <summary>The public view: a hidden response gives only the moderator's statement; a withdrawn one its versions; the ballot only beside a published one.</summary>
    private static ResponseView View(MpResponse r, IReadOnlyList<ResponseVersion> earlier, ResponseVote? vote) => r.Status switch
    {
        ResponseStatuses.Hidden => new ResponseView(r.Id, r.SeimasPersonId, r.Status, null, null, r.PublishedAt, null, null, r.HideStatement, [], null),
        ResponseStatuses.Withdrawn => new ResponseView(r.Id, r.SeimasPersonId, r.Status, null, null, r.PublishedAt, r.EditedAt, r.WithdrawnAt, null, earlier, null),
        _ => new ResponseView(r.Id, r.SeimasPersonId, r.Status, r.Stance, r.Text, r.PublishedAt, r.EditedAt, null, null, earlier, vote),
    };

    /// <summary>The kept versions of the given responses, newest first.</summary>
    private static async Task<Dictionary<Guid, IReadOnlyList<ResponseVersion>>> VersionsAsync(AppealsDb db, IReadOnlyCollection<MpResponse> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return (await db.Revisions.AsNoTracking()
                .Where(r => r.EntityKind == Kind && r.Field == "version" && ids.Contains(r.EntityId))
                .Select(r => new { r.EntityId, r.ChangedAt, r.Id, r.OldValue })
                .ToListAsync(ct))
            .GroupBy(r => r.EntityId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ResponseVersion>)g.OrderByDescending(r => r.ChangedAt).ThenByDescending(r => r.Id)
                    .Select(r => Version(r.OldValue, r.ChangedAt))
                    .OfType<ResponseVersion>()
                    .ToList());
    }

    private static string VersionJson(MpResponse r, bool withdrawn) =>
        JsonSerializer.Serialize(new VersionRow(r.Stance, r.Text, r.EditedAt ?? r.PublishedAt, withdrawn), JsonSerializerOptions.Web);

    private static ResponseVersion? Version(string? json, DateTimeOffset replacedAt) =>
        string.IsNullOrEmpty(json) || JsonSerializer.Deserialize<VersionRow>(json, JsonSerializerOptions.Web) is not { } row
            ? null
            : new ResponseVersion(row.Stance, row.Text, row.WrittenAt, replacedAt, row.Withdrawn);

    /// <summary>
    /// After the adoption vote, each given MP's ballot at it (design 8.5.5): <c>consensus</c> for a vote without per-MP ballots,
    /// <c>did-not-vote</c> for an MP without a ballot. Nothing while the bill is open, or while the vote's ballots are not read.
    /// </summary>
    private async Task<Dictionary<long, ResponseVote>> VotesAsync(BillForStatements bill, List<long> persons, CancellationToken ct)
    {
        var none = new Dictionary<long, ResponseVote>();
        if (!bill.Finished || persons.Count == 0 || await catalogue.FinalVoteAsync(bill.Bill.Number, ct) is not { } final)
        {
            return none;
        }

        var facts = (await catalogue.StageResultsAsync([bill.Bill.Number], final.HeldAt, ct)).FirstOrDefault(v => v.VoteId == final.VoteId);
        if (facts is { IsConsensus: true })
        {
            return persons.Distinct().ToDictionary(p => p, _ => new ResponseVote(final.VoteId, final.HeldAt, "consensus"));
        }

        if (facts is { HasResult: false })
        {
            return none;
        }

        var choices = await catalogue.ChoicesAsync(final.VoteId, persons.Distinct().ToList(), ct);
        return persons.Distinct().ToDictionary(p => p, p => new ResponseVote(final.VoteId, final.HeldAt, choices.GetValueOrDefault(p) ?? BallotChoices.DidNotVote));
    }

    /// <summary>A version as kept: its stance and text, when it was written, and whether it was the text withdrawn.</summary>
    private sealed record VersionRow(string Stance, string Text, DateTimeOffset WrittenAt, bool Withdrawn);
}
