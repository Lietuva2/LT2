using System.Data.Common;
using System.Text.Json;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// Moderation (design 8.3): every action is recorded in <c>appeals.moderation_action</c> and in the audit log, in the
/// same transaction, and the author is told by email through the outbox, with the statement of reasons. Nothing here
/// reads or writes a position.
/// </summary>
internal sealed class ModerationService(
    IDbContextFactory<AppealsDb> contexts,
    AppealReads reads,
    IParticipants participants,
    IAuditLog audit,
    IOutbox outbox,
    IPlannedAgenda agenda,
    TimeProvider clock) : IAppealsModeration
{
    private const int QueueLimit = 100;

    public async Task<ModerationItems> QueueAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var openReports = await db.Reports.AsNoTracking().Where(r => r.Status == "open").OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var reportedIds = openReports.Select(r => r.TargetId).ToHashSet();

        // Open appeals come into the queue when reported or when a surge needs a person to look (design 6.6).
        var surges = new Dictionary<Guid, SurgeInfo>();
        foreach (var openId in await db.Appeals.AsNoTracking().Where(a => a.Status == AppealStatuses.Open).Select(a => a.Id).ToListAsync(cancellationToken))
        {
            if (await reads.SurgeAsync(db, openId, cancellationToken) is { } surge)
            {
                surges[openId] = surge;
            }
        }

        // Date flags, and appeals under their threshold whose bill has reached the agenda (design 8.3).
        var openFlags = await db.Flags.AsNoTracking().Where(f => f.Status == "open").OrderBy(f => f.CreatedAt).ToListAsync(cancellationToken);
        var openAppeals = await db.Appeals.AsNoTracking().Where(a => a.Status == AppealStatuses.Open).ToListAsync(cancellationToken);
        var planned = await agenda.ForBillsAsync(openAppeals.Select(a => a.BillNumber).ToList(), cancellationToken);
        var counts = await reads.CountsAsync(db, openAppeals.Select(a => a.Id).ToList(), cancellationToken);
        var minimum = await reads.MinimumAsync(cancellationToken);
        var underThresholdOnAgenda = openAppeals
            .Where(a => planned.ContainsKey(a.BillNumber) && !AppealReads.Threshold(a, counts[a.Id], minimum).Reached)
            .Select(a => a.Id)
            .ToHashSet();
        var held = openAppeals.Where(a => a.Sending == "held").Select(a => a.Id);
        var surgeIds = surges.Keys.Concat(openFlags.Select(f => f.AppealId)).Concat(underThresholdOnAgenda).Concat(held).Distinct().ToList();
        var appeals = await db.Appeals.AsNoTracking()
            .Where(a => a.Status == AppealStatuses.Pending
                || (a.Status == AppealStatuses.Open && (reportedIds.Contains(a.Id) || surgeIds.Contains(a.Id))))
            .OrderBy(a => a.OpenedAt)
            .Take(QueueLimit)
            .ToListAsync(cancellationToken);
        var pendingIds = appeals.Where(a => a.Status == AppealStatuses.Pending).Select(a => a.Id).ToList();
        var firstReasons = await db.Reasons.AsNoTracking()
            .Where(r => pendingIds.Contains(r.AppealId))
            .ToListAsync(cancellationToken);

        var reasons = await db.Reasons.AsNoTracking()
            .Where(r => db.Appeals.Any(a => a.Id == r.AppealId && a.Status == AppealStatuses.Open)
                && (r.Status == ReasonStatuses.Pending
                    || (r.Status == ReasonStatuses.Published && (r.SeenAt == null || reportedIds.Contains(r.Id)))))
            .OrderBy(r => r.CreatedAt)
            .Take(QueueLimit)
            .ToListAsync(cancellationToken);
        var appealOf = await db.Appeals.AsNoTracking()
            .Where(a => reasons.Select(r => r.AppealId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);
        var candidateAppeals = reasons.Select(r => r.AppealId).Distinct().ToList();
        var candidates = await db.Reasons.AsNoTracking()
            .Where(r => candidateAppeals.Contains(r.AppealId) && r.Status == ReasonStatuses.Published)
            .ToListAsync(cancellationToken);

        var allReasons = firstReasons.Concat(reasons).Concat(candidates).ToList();
        var names = await reads.NamesAsync(appeals.Select(a => a.InitiatorUserId).Concat(allReasons.Select(r => r.AuthorUserId)), cancellationToken);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, allReasons.Select(r => r.Id).Distinct().ToList(), cancellationToken);
        var billFacts = await reads.BillsAsync(appeals.Select(a => a.BillNumber).Concat(appealOf.Values.Select(a => a.BillNumber)).Distinct().ToList(), cancellationToken);
        BillFacts Bill(string number) => billFacts.GetValueOrDefault(number) ?? new BillFacts(number, number);
        IReadOnlyList<QueueReport> ReportsOn(Guid id) => openReports.Where(r => r.TargetId == id).Select(r => new QueueReport(r.Id, r.Ground, r.Text, r.CreatedAt)).ToList();

        var queueAppeals = appeals.Select(a =>
        {
            var first = firstReasons.Where(r => r.AppealId == a.Id && r.AuthorUserId == a.InitiatorUserId).MinBy(r => r.CreatedAt);
            return new QueueAppeal(
                a.Id,
                Bill(a.BillNumber),
                a.Status,
                names.GetValueOrDefault(a.InitiatorUserId),
                a.Description,
                first is null ? null : AppealReads.ToInfo(first, names, endorsements),
                a.Threshold,
                a.ScreeningFlags.Concat(first?.ScreeningFlags ?? []).Distinct().ToList(),
                ReportsOn(a.Id),
                a.OpenedAt,
                surges.GetValueOrDefault(a.Id),
                planned.GetValueOrDefault(a.BillNumber),
                openFlags.Where(f => f.AppealId == a.Id).Select(f => new QueueFlag(f.Id, f.Kind, f.Link, f.Note, f.CreatedAt)).ToList(),
                underThresholdOnAgenda.Contains(a.Id),
                a.Sending == "held");
        }).ToList();

        var queueReasons = reasons.Select(r => new QueueReason(
            r.Id,
            r.AppealId,
            Bill(appealOf[r.AppealId].BillNumber),
            r.Side,
            r.Text,
            names.GetValueOrDefault(r.AuthorUserId),
            r.Status,
            r.SeenAt is not null,
            r.ScreeningFlags,
            ReportsOn(r.Id),
            ReasonSimilarity.MostSimilar(
                    r.Text,
                    candidates.Where(c => c.AppealId == r.AppealId && c.Side == r.Side && c.Id != r.Id),
                    c => c.Text)
                .Select(c => AppealReads.ToInfo(c, names, endorsements))
                .ToList(),
            r.CreatedAt)).ToList();

        return new ModerationItems(queueAppeals, queueReasons);
    }

    public Task<Result<Unit>> PublishAppealAsync(Guid appealId, string? statement, ModeratorActor moderator, CancellationToken cancellationToken) =>
        OnAppealAsync(appealId, moderator, "publish", statement, async (db, appeal, now) =>
        {
            if (appeal.Status is not (AppealStatuses.Pending or AppealStatuses.Open))
            {
                return Error.Conflict("moderation.wrong-state");
            }

            var notice = (string?)null;
            if (appeal.Status == AppealStatuses.Pending)
            {
                appeal.Status = AppealStatuses.Open;
                appeal.PublishedAt = now;
                appeal.ReturnNote = null;
                notice = ModerationNotices.AppealPublished;

                // The initiator's first argument was checked with the appeal.
                await db.Reasons
                    .Where(r => r.AppealId == appeal.Id && r.AuthorUserId == appeal.InitiatorUserId && r.Status == ReasonStatuses.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReasonStatuses.Published).SetProperty(r => r.SeenAt, now), cancellationToken);
            }

            // An open appeal that was reported or flagged is kept as it is: its reports and date flags are closed.
            await CloseFlagsAsync(db, appeal.Id, now, cancellationToken);
            return notice;
        }, cancellationToken);

    public Task<Result<Unit>> ReturnAppealAsync(Guid appealId, string statement, ModeratorActor moderator, CancellationToken cancellationToken) =>
        OnAppealAsync(appealId, moderator, "return", statement, (_, appeal, _) =>
        {
            if (appeal.Status != AppealStatuses.Pending)
            {
                return Task.FromResult<Result<string?>>(Error.Conflict("moderation.wrong-state"));
            }

            appeal.Status = AppealStatuses.Returned;
            appeal.ReturnNote = statement;
            return Task.FromResult<Result<string?>>(ModerationNotices.AppealReturned);
        }, cancellationToken);

    public Task<Result<Unit>> HideAppealAsync(Guid appealId, string statement, ModeratorActor moderator, CancellationToken cancellationToken) =>
        OnAppealAsync(appealId, moderator, "hide", statement, (_, appeal, _) =>
        {
            if (appeal.Status == AppealStatuses.Rejected)
            {
                return Task.FromResult<Result<string?>>(Error.Conflict("moderation.wrong-state"));
            }

            appeal.Status = AppealStatuses.Rejected;
            return Task.FromResult<Result<string?>>(ModerationNotices.AppealHidden);
        }, cancellationToken);

    public async Task<Result<Unit>> SetThresholdAsync(Guid appealId, int threshold, string statement, ModeratorActor moderator, CancellationToken cancellationToken)
    {
        if (ThresholdRules.CheckRequested(threshold, await reads.MinimumAsync(cancellationToken)) is { } below)
        {
            return RuleErrors.Of(below);
        }

        return await OnAppealAsync(appealId, moderator, "threshold", statement, (db, appeal, now) =>
        {
            if (appeal.Status is AppealStatuses.Rejected or AppealStatuses.Closed)
            {
                return Task.FromResult<Result<string?>>(Error.Conflict("moderation.wrong-state"));
            }

            db.Revisions.Add(AppealsService.Revise("appeal", appeal.Id, "threshold", AppealsService.Number(appeal.Threshold), AppealsService.Number(threshold), moderator.UserId, now));
            appeal.Threshold = threshold;
            appeal.ThresholdSetBy = ThresholdSetters.Moderator;
            appeal.ThresholdNote = statement;
            return Task.FromResult<Result<string?>>(ModerationNotices.ThresholdChanged);
        }, cancellationToken, details: new { threshold });
    }

    public Task<Result<Unit>> PublishReasonAsync(Guid reasonId, string? statement, ModeratorActor moderator, CancellationToken cancellationToken) =>
        OnReasonAsync(reasonId, moderator, "publish", statement, (_, reason, now) =>
        {
            if (reason.Status is not (ReasonStatuses.Pending or ReasonStatuses.Published))
            {
                return Task.FromResult<Result<string?>>(Error.Conflict("moderation.wrong-state"));
            }

            var wasPending = reason.Status == ReasonStatuses.Pending;
            reason.Status = ReasonStatuses.Published;
            reason.SeenAt = now;
            return Task.FromResult<Result<string?>>(wasPending ? ModerationNotices.ReasonPublished : null);
        }, cancellationToken);

    public Task<Result<Unit>> HideReasonAsync(Guid reasonId, string statement, ModeratorActor moderator, CancellationToken cancellationToken) =>
        OnReasonAsync(reasonId, moderator, "hide", statement, async (db, reason, now) =>
        {
            if (reason.Status is not (ReasonStatuses.Pending or ReasonStatuses.Published))
            {
                return Error.Conflict("moderation.wrong-state");
            }

            reason.Status = ReasonStatuses.Hidden;
            reason.SeenAt = now;

            // A hidden argument cannot be endorsed; the marks on it go, so they no longer count towards anyone's three.
            await db.Endorsements.Where(e => e.ReasonId == reason.Id).ExecuteDeleteAsync(cancellationToken);
            return ModerationNotices.ReasonHidden;
        }, cancellationToken);

    public Task<Result<Unit>> MergeReasonAsync(Guid reasonId, Guid intoReasonId, string statement, ModeratorActor moderator, CancellationToken cancellationToken) =>
        OnReasonAsync(reasonId, moderator, "merge", statement, async (db, reason, now) =>
        {
            if (reasonId == intoReasonId || reason.Status is not (ReasonStatuses.Pending or ReasonStatuses.Published))
            {
                return Error.Conflict("moderation.wrong-state");
            }

            var into = await db.Reasons.FirstOrDefaultAsync(r => r.Id == intoReasonId, cancellationToken);
            if (into is null || into.AppealId != reason.AppealId || into.Side != reason.Side || into.Status != ReasonStatuses.Published)
            {
                return Error.Conflict("merge.target-invalid");
            }

            // Endorsements move with it (design 8.3); someone who marked both keeps one mark.
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO appeals.endorsement (reason_id, user_id, appeal_id, created_at)
                SELECT {1}, e.user_id, e.appeal_id, e.created_at FROM appeals.endorsement e WHERE e.reason_id = {0}
                ON CONFLICT (reason_id, user_id) DO NOTHING
                """,
                [reasonId, intoReasonId],
                cancellationToken);
            await db.Endorsements.Where(e => e.ReasonId == reasonId).ExecuteDeleteAsync(cancellationToken);
            reason.Status = ReasonStatuses.Merged;
            reason.MergedIntoId = intoReasonId;
            reason.SeenAt = now;
            return ModerationNotices.ReasonMerged;
        }, cancellationToken, details: new { into = intoReasonId });

    public Task<Result<Unit>> SetPlannedAsync(Guid appealId, DateOnly date, string? stage, string statement, ModeratorActor moderator, CancellationToken cancellationToken)
    {
        if (stage is not (null or "presentation" or "consideration" or "adoption"))
        {
            return Task.FromResult<Result<Unit>>(Error.Validation("planned.stage", "stage"));
        }

        return OnAppealAsync(appealId, moderator, "planned", statement, async (db, appeal, now) =>
        {
            if (appeal.Status != AppealStatuses.Open)
            {
                return Error.Conflict("moderation.wrong-state");
            }

            await agenda.CorrectAsync(appeal.BillNumber, date, stage, moderator.UserId, statement, db.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
            await CloseFlagsAsync(db, appeal.Id, now, cancellationToken);

            // Not the initiator's content: no notice to the author.
            return (string?)null;
        }, cancellationToken, details: new { date, stage });
    }

    public Task<Result<Unit>> HoldAsync(Guid appealId, string statement, ModeratorActor moderator, CancellationToken cancellationToken) =>
        SendingAsync(appealId, "held", "hold", statement, moderator, cancellationToken);

    public Task<Result<Unit>> ReleaseAsync(Guid appealId, string statement, ModeratorActor moderator, CancellationToken cancellationToken) =>
        SendingAsync(appealId, "automatic", "release", statement, moderator, cancellationToken);

    private Task<Result<Unit>> SendingAsync(Guid appealId, string sending, string action, string statement, ModeratorActor moderator, CancellationToken ct) =>
        OnAppealAsync(appealId, moderator, action, statement, (_, appeal, _) =>
        {
            if (appeal.Status != AppealStatuses.Open || appeal.Sending == sending)
            {
                return Task.FromResult<Result<string?>>(Error.Conflict("moderation.wrong-state"));
            }

            appeal.Sending = sending;

            // Timing is not the initiator's content: no notice to the author.
            return Task.FromResult<Result<string?>>((string?)null);
        }, ct);

    private static Task<int> CloseFlagsAsync(AppealsDb db, Guid appealId, DateTimeOffset now, CancellationToken ct) =>
        db.Flags.Where(f => f.AppealId == appealId && f.Status == "open")
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, "closed").SetProperty(f => f.ClosedAt, now), ct);

    private delegate Task<Result<string?>> AppealAction(AppealsDb db, Appeal appeal, DateTimeOffset now);

    private delegate Task<Result<string?>> ReasonAction(AppealsDb db, Reason reason, DateTimeOffset now);

    private async Task<Result<Unit>> OnAppealAsync(
        Guid appealId, ModeratorActor moderator, string action, string? statement, AppealAction apply, CancellationToken ct, object? details = null)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var appeal = await db.Appeals.FirstOrDefaultAsync(a => a.Id == appealId, ct);
        if (appeal is null)
        {
            return Error.NotFound("appeal.not-found");
        }

        var now = clock.GetUtcNow();
        var outcome = await apply(db, appeal, now);
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        appeal.ChangedAt = now;
        await RecordAsync(db, transaction, "appeal", appealId, action, statement, moderator, now, appeal.BillNumber, details, ct);
        if (outcome.Value is { } notice)
        {
            await NotifyAsync(transaction, appeal.InitiatorUserId, notice, appeal.BillNumber, statement, null, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    private async Task<Result<Unit>> OnReasonAsync(
        Guid reasonId, ModeratorActor moderator, string action, string? statement, ReasonAction apply, CancellationToken ct, object? details = null)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var reason = await db.Reasons.FirstOrDefaultAsync(r => r.Id == reasonId, ct);
        var appeal = reason is null ? null : await db.Appeals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == reason.AppealId, ct);
        if (reason is null || appeal is null)
        {
            return Error.NotFound("reason.not-found");
        }

        if (appeal.Status != AppealStatuses.Open)
        {
            // Arguments of an unpublished appeal are moderated with the appeal.
            return Error.Conflict("moderation.wrong-state");
        }

        var now = clock.GetUtcNow();
        var outcome = await apply(db, reason, now);
        if (!outcome.IsSuccess)
        {
            return outcome.Error!;
        }

        await RecordAsync(db, transaction, "reason", reasonId, action, statement, moderator, now, appeal.BillNumber, details, ct);
        if (outcome.Value is { } notice)
        {
            var excerpt = reason.Text.Length > 120 ? reason.Text[..120] + "…" : reason.Text;
            await NotifyAsync(transaction, reason.AuthorUserId, notice, appeal.BillNumber, statement, excerpt, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    /// <summary>The moderation record, the audit entry and the closing of open reports on the target, in one transaction.</summary>
    private async Task RecordAsync(
        AppealsDb db, IDbContextTransaction transaction, string kind, Guid targetId, string action, string? statement,
        ModeratorActor moderator, DateTimeOffset now, string billNumber, object? details, CancellationToken ct)
    {
        db.ModerationActions.Add(new ModerationAction
        {
            Id = Ids.New(clock),
            ModeratorUserId = moderator.UserId,
            TargetKind = kind,
            TargetId = targetId,
            Action = action,
            Statement = statement,
            CreatedAt = now,
        });
        await db.Reports.Where(r => r.TargetId == targetId && r.Status == "open")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "closed").SetProperty(r => r.ClosedAt, now), ct);
        var auditDetails = JsonSerializer.SerializeToElement(new { bill = billNumber, statement, extra = details }, JsonSerializerOptions.Web);
        await audit.WriteAsync(
            new AuditEntry($"moderation.{kind}.{action}", moderator.UserId, moderator.Role, $"{kind}:{targetId}", auditDetails, moderator.IpHash),
            transaction.GetDbTransaction(),
            ct);
    }

    public Task<Result<AuthorView>> AuthorOfReasonAsync(Guid reasonId, CancellationToken cancellationToken) =>
        ViewAsync(db => db.Reasons.Where(r => r.Id == reasonId).Select(r => (Guid?)r.AuthorUserId), "reason.not-found", cancellationToken);

    public Task<Result<AuthorView>> InitiatorOfAppealAsync(Guid appealId, CancellationToken cancellationToken) =>
        ViewAsync(db => db.Appeals.Where(a => a.Id == appealId).Select(a => (Guid?)a.InitiatorUserId), "appeal.not-found", cancellationToken);

    public Task<Result<Guid>> RestrictAuthorOfReasonAsync(Guid reasonId, DateOnly? until, string statement, ModeratorActor moderator, string? contact, CancellationToken cancellationToken) =>
        RestrictAsync(db => db.Reasons.Where(r => r.Id == reasonId).Select(r => (Guid?)r.AuthorUserId), "reason.not-found", until, statement, moderator, contact, cancellationToken);

    public Task<Result<Guid>> RestrictInitiatorOfAppealAsync(Guid appealId, DateOnly? until, string statement, ModeratorActor moderator, string? contact, CancellationToken cancellationToken) =>
        RestrictAsync(db => db.Appeals.Where(a => a.Id == appealId).Select(a => (Guid?)a.InitiatorUserId), "appeal.not-found", until, statement, moderator, contact, cancellationToken);

    public async Task<IReadOnlyList<RestrictionInfo>> RestrictionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var holding = (await db.WritingRestrictions.AsNoTracking().Where(w => w.LiftedAt == null).OrderBy(w => w.SetAt).ToListAsync(cancellationToken))
            .Where(w => WritingRules.Holds(w.EndsAt, w.LiftedAt, now))
            .ToList();
        var names = await reads.NamesAsync(holding.Select(w => w.UserId), cancellationToken);
        return holding.Select(w => Info(w, names.GetValueOrDefault(w.UserId), now)).ToList();
    }

    public async Task<Result<Unit>> LiftRestrictionAsync(Guid restrictionId, string statement, ModeratorActor moderator, string? contact, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return Error.Validation("statement.required", "statement");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var restriction = await db.WritingRestrictions.FirstOrDefaultAsync(w => w.Id == restrictionId, cancellationToken);
        if (restriction is null)
        {
            return Error.NotFound("restriction.not-found");
        }

        var now = clock.GetUtcNow();
        if (!WritingRules.Holds(restriction.EndsAt, restriction.LiftedAt, now))
        {
            return Error.Conflict("restriction.not-holding");
        }

        restriction.LiftedAt = now;
        restriction.LiftedByUserId = moderator.UserId;
        restriction.LiftStatement = statement.Trim();
        await RecordRestrictionAsync(db, transaction, restriction.Id, "lift", restriction.LiftStatement, null, moderator, now, cancellationToken);
        await NotifyRestrictionAsync(transaction, restriction.UserId, WritingRestrictionNotices.Lifted, null, restriction.LiftStatement, contact, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    private async Task<Result<AuthorView>> ViewAsync(Func<AppealsDb, IQueryable<Guid?>> who, string notFound, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        if (await who(db).FirstOrDefaultAsync(ct) is not { } userId)
        {
            return Error.NotFound(notFound);
        }

        var now = clock.GetUtcNow();
        var reasons = await db.Reasons.AsNoTracking().Where(r => r.AuthorUserId == userId).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var appeals = await db.Appeals.AsNoTracking().Where(a => a.InitiatorUserId == userId).OrderByDescending(a => a.OpenedAt).ToListAsync(ct);
        var reasonAppeals = reasons.Select(r => r.AppealId).Distinct().ToList();
        var bills = await db.Appeals.AsNoTracking().Where(a => reasonAppeals.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.BillNumber, ct);
        var restrictions = await db.WritingRestrictions.AsNoTracking().Where(w => w.UserId == userId).OrderByDescending(w => w.SetAt).ToListAsync(ct);
        var targets = reasons.Select(r => r.Id).Concat(appeals.Select(a => a.Id)).Concat(restrictions.Select(w => w.Id)).ToList();
        var actions = await db.ModerationActions.AsNoTracking().Where(m => targets.Contains(m.TargetId)).OrderByDescending(m => m.CreatedAt).ToListAsync(ct);
        var name = (await reads.NamesAsync([userId], ct)).GetValueOrDefault(userId);

        // The person's arguments and appeals, and what moderators did about them. Never the address, a side or a mark.
        return new AuthorView(
            name,
            reasons.Select(r => new AuthorReason(r.Id, bills.GetValueOrDefault(r.AppealId) ?? "", r.Side, r.Text, r.Status, r.CreatedAt)).ToList(),
            appeals.Select(a => new AuthorAppeal(a.Id, a.BillNumber, a.Status, a.OpenedAt)).ToList(),
            actions.Select(m => new ModerationEntry(m.TargetKind, m.Action, m.Statement, m.CreatedAt)).ToList(),
            restrictions.Select(w => Info(w, name, now)).ToList());
    }

    private async Task<Result<Guid>> RestrictAsync(
        Func<AppealsDb, IQueryable<Guid?>> who, string notFound, DateOnly? until, string statement, ModeratorActor moderator, string? contact, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(statement))
        {
            return Error.Validation("statement.required", "statement");
        }

        if (WritingRules.CheckUntil(until, now) is { } wrong)
        {
            return Error.Validation(wrong, "until");
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await who(db).FirstOrDefaultAsync(ct) is not { } userId)
        {
            return Error.NotFound(notFound);
        }

        var open = await db.WritingRestrictions.Where(w => w.UserId == userId && w.LiftedAt == null).ToListAsync(ct);
        if (open.Any(w => WritingRules.Holds(w.EndsAt, w.LiftedAt, now)))
        {
            // One at a time: to change its length, a moderator lifts it and sets a new one.
            return Error.Conflict("restriction.exists");
        }

        var restriction = new WritingRestriction
        {
            Id = Ids.New(clock),
            UserId = userId,
            EndsAt = until is { } day ? WritingRules.EndsAt(day) : null,
            Statement = statement.Trim(),
            SetByUserId = moderator.UserId,
            SetAt = now,
        };
        db.WritingRestrictions.Add(restriction);
        await RecordRestrictionAsync(db, transaction, restriction.Id, "restrict", restriction.Statement, until, moderator, now, ct);
        await NotifyRestrictionAsync(transaction, userId, WritingRestrictionNotices.Restricted, until, restriction.Statement, contact, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return restriction.Id;
    }

    private static RestrictionInfo Info(WritingRestriction w, string? name, DateTimeOffset now) =>
        new(w.Id, name, WritingRules.Until(w.EndsAt), w.Statement, w.SetAt, WritingRules.Holds(w.EndsAt, w.LiftedAt, now), w.LiftedAt, w.LiftStatement);

    /// <summary>Like every moderation action: the record and the audit entry (moderator, target, statement, end), nothing about sides.</summary>
    private async Task RecordRestrictionAsync(
        AppealsDb db, IDbContextTransaction transaction, Guid restrictionId, string action, string statement, DateOnly? until, ModeratorActor moderator, DateTimeOffset now, CancellationToken ct)
    {
        db.ModerationActions.Add(new ModerationAction
        {
            Id = Ids.New(clock),
            ModeratorUserId = moderator.UserId,
            TargetKind = "restriction",
            TargetId = restrictionId,
            Action = action,
            Statement = statement,
            CreatedAt = now,
        });
        var details = JsonSerializer.SerializeToElement(new { statement, until }, JsonSerializerOptions.Web);
        await audit.WriteAsync(
            new AuditEntry($"moderation.restriction.{action}", moderator.UserId, moderator.Role, $"restriction:{restrictionId}", details, moderator.IpHash),
            transaction.GetDbTransaction(),
            ct);
    }

    private async Task NotifyRestrictionAsync(IDbContextTransaction transaction, Guid userId, string notice, DateOnly? until, string statement, string? contact, CancellationToken ct)
    {
        if (await participants.FindAsync(userId, ct) is not { } person)
        {
            return;
        }

        await outbox.EnqueueAsync(
            OutboxMessage.Create(MessageKinds.WritingRestrictionEmail, new WritingRestrictionEmail(person.Email, notice, until, statement, contact)),
            transaction.GetDbTransaction(),
            ct);
    }

    private async Task NotifyAsync(IDbContextTransaction transaction, Guid authorId, string notice, string billNumber, string? statement, string? excerpt, CancellationToken ct)
    {
        var author = await participants.FindAsync(authorId, ct);
        if (author is null)
        {
            return;
        }

        var bill = await reads.BillAsync(billNumber, ct);
        DbTransaction dbTransaction = transaction.GetDbTransaction();
        await outbox.EnqueueAsync(
            OutboxMessage.Create(MessageKinds.ModerationNoticeEmail, new ModerationNoticeEmail(author.Email, notice, bill.Number, bill.Title, statement, excerpt)),
            dbTransaction,
            ct);
    }
}
