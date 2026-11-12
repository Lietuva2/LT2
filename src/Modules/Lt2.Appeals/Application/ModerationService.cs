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

        var surgeIds = surges.Keys.ToList();
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
                surges.GetValueOrDefault(a.Id));
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

            // An open appeal that was reported is kept as it is: the reports are closed.
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
