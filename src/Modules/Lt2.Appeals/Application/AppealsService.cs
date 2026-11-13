using System.Globalization;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.Appeals.Screening;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// What members do with appeals (design 8.1, 8.2): open one, take or change a side, write and endorse arguments,
/// report. Every action of one person runs under that person's lock (<see cref="ParticipationLock"/>), so the limits
/// per person and the consent check cannot be raced.
/// </summary>
internal sealed class AppealsService(
    IDbContextFactory<AppealsDb> contexts,
    AppealReads reads,
    IParticipants participants,
    IBillCatalogue bills,
    IPreScreening screening,
    IPlannedAgenda agenda,
    ISettings settings,
    IAuditLog audit,
    TimeProvider clock) : IAppealsModule
{
    private const int PageSize = 20;

    public Task<AppealRulesInfo> RulesAsync(CancellationToken cancellationToken) => reads.RulesAsync(cancellationToken);

    public async Task<AppealPage> ListAsync(string? sort, string? cursor, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeals = await db.Appeals.AsNoTracking()
            .Where(a => a.Status == AppealStatuses.Open || a.Status == AppealStatuses.Closed)
            .ToListAsync(cancellationToken);
        var counts = await reads.CountsAsync(db, appeals.Select(a => a.Id).ToList(), cancellationToken);
        var ordered = sort == "newest"
            ? appeals.OrderByDescending(a => a.PublishedAt).ThenBy(a => a.BillNumber, StringComparer.Ordinal).ToList()
            : appeals.OrderByDescending(a => counts[a.Id].Total).ThenByDescending(a => a.PublishedAt).ToList();
        var offset = int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var o) ? Math.Min(o, ordered.Count) : 0;
        var page = ordered.Skip(offset).Take(PageSize).ToList();
        var billFacts = await reads.BillsAsync(page.Select(a => a.BillNumber).ToList(), cancellationToken);
        var minimum = await reads.MinimumAsync(cancellationToken);
        var planned = await agenda.ForBillsAsync(page.Select(a => a.BillNumber).ToList(), cancellationToken);
        var items = page
            .Select(a => new AppealListItem(
                a.Id,
                billFacts.GetValueOrDefault(a.BillNumber) ?? new BillFacts(a.BillNumber, a.BillNumber),
                counts[a.Id],
                AppealReads.Threshold(a, counts[a.Id], minimum),
                a.PublishedAt,
                a.Status == AppealStatuses.Open ? planned.GetValueOrDefault(a.BillNumber) : null))
            .ToList();
        var next = offset + page.Count < ordered.Count ? (offset + page.Count).ToString(CultureInfo.InvariantCulture) : null;
        return new AppealPage(items, ordered.Count, next);
    }

    public async Task<Result<AppealInfo>> GetAsync(string billNumber, Guid? viewer, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeal = await AppealReads.FindByBillAsync(db, billNumber, tracking: false, cancellationToken);
        if (appeal is null || !AppealReads.IsVisibleTo(appeal, viewer))
        {
            return Error.NotFound("appeal.not-found");
        }

        return await InfoAsync(db, appeal, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<(DateTimeOffset ChangedAt, string? OldText, string? NewText)>>> HistoryAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeal = await AppealReads.FindByBillAsync(db, billNumber, tracking: false, cancellationToken);
        if (appeal is null || appeal.Status is not (AppealStatuses.Open or AppealStatuses.Closed))
        {
            return Error.NotFound("appeal.not-found");
        }

        var rows = await db.Revisions.AsNoTracking()
            .Where(r => r.EntityKind == "appeal" && r.EntityId == appeal.Id && r.Field == "description")
            .OrderBy(r => r.ChangedAt)
            .Select(r => new { r.ChangedAt, r.OldValue, r.NewValue })
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<(DateTimeOffset, string?, string?)>>.Success(rows.Select(r => (r.ChangedAt, r.OldValue, r.NewValue)).ToList());
    }

    public async Task<Result<MyAppeal>> MineAsync(string billNumber, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeal = await AppealReads.FindByBillAsync(db, billNumber, tracking: false, cancellationToken);
        if (appeal is null || !AppealReads.IsVisibleTo(appeal, userId))
        {
            return Error.NotFound("appeal.not-found");
        }

        var side = await db.Positions.AsNoTracking()
            .Where(p => p.AppealId == appeal.Id && p.UserId == userId)
            .Select(p => p.Side)
            .FirstOrDefaultAsync(cancellationToken);
        var endorsed = await db.Endorsements.AsNoTracking()
            .Where(e => e.AppealId == appeal.Id && e.UserId == userId)
            .Select(e => e.ReasonId)
            .ToListAsync(cancellationToken);
        var mine = await db.Reasons.AsNoTracking()
            .Where(r => r.AppealId == appeal.Id && r.AuthorUserId == userId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        var names = await reads.NamesAsync([userId], cancellationToken);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, mine.Select(r => r.Id).ToList(), cancellationToken);
        var facts = await participants.FindAsync(userId, cancellationToken);
        var isInitiator = appeal.InitiatorUserId == userId;
        return new MyAppeal(
            side,
            endorsed,
            AppealRules.Left(endorsed.Count, await settings.GetAsync(SettingKeys.AppealsEndorsementsPerPerson, cancellationToken)),
            mine.Select(r => AppealReads.ToInfo(r, names, endorsements)).ToList(),
            AppealRules.Left(
                mine.Count(r => AppealRules.CountedReasonStatuses.Contains(r.Status)),
                await settings.GetAsync(SettingKeys.AppealsReasonsPerPerson, cancellationToken)),
            isInitiator,
            isInitiator && ThresholdRules.InitiatorMayChange(appeal.Status, appeal.ThresholdSetBy, appeal.FirstSummarisedAt),
            facts?.HasPositionConsent == true);
    }

    public async Task<Result<AppealInfo>> OpenAsync(string billNumber, Guid userId, OpenAppeal request, CancellationToken cancellationToken)
    {
        if (FirstBroken(
                AppealRules.CheckSide(request.Side),
                AppealRules.CheckReasonText(request.FirstReason),
                AppealRules.CheckDescription(request.Description),
                ThresholdRules.CheckRequested(request.Threshold, await reads.MinimumAsync(cancellationToken))) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        var bill = await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken);
        if (bill is null)
        {
            return Error.NotFound("bill.not-found");
        }

        var description = AppealRules.NormaliseText(request.Description);
        var firstReason = AppealRules.NormaliseText(request.FirstReason);
        var descriptionFlags = await screening.ScreenAsync(description, cancellationToken);
        var reasonFlags = await screening.ScreenAsync(firstReason, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ParticipationLock.TakeAsync(db, userId, cancellationToken);
        await ParticipationLock.TakeBillAsync(db, bill.Number, cancellationToken);

        var participant = await participants.FindAsync(userId, cancellationToken);
        if (participant is null)
        {
            return Error.Forbidden("member.required");
        }

        if (!participant.HasPositionConsent)
        {
            return Error.Conflict("consent.required");
        }

        var existing = await db.Appeals.AsNoTracking()
            .Where(a => a.BillNumber == bill.Number)
            .Select(a => a.Status)
            .ToListAsync(cancellationToken);
        if (AppealRules.BillAlreadyHasAppeal(existing))
        {
            return Error.Conflict("appeal.exists");
        }

        var now = clock.GetUtcNow();
        var appeal = new Appeal
        {
            Id = Ids.New(clock),
            BillNumber = bill.Number,
            InitiatorUserId = userId,
            Description = description,
            Status = AppealStatuses.Pending,
            OpenedAt = now,
            ChangedAt = now,
            Threshold = request.Threshold,
            ThresholdSetBy = ThresholdSetters.Initiator,
            ScreeningFlags = [.. descriptionFlags],
        };
        db.Appeals.Add(appeal);
        db.Reasons.Add(new Reason
        {
            Id = Ids.New(clock),
            AppealId = appeal.Id,
            Side = request.Side,
            AuthorUserId = userId,
            Text = firstReason,
            Status = ReasonStatuses.Pending,
            ScreeningFlags = [.. reasonFlags],
            CreatedAt = now,
        });
        db.Positions.Add(new Position
        {
            AppealId = appeal.Id,
            UserId = userId,
            Side = request.Side,
            CreatedAt = now,
            ChangedAt = now,
            MemberSince = participant.MemberSince,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await InfoAsync(db, appeal, cancellationToken);
    }

    public async Task<Result<Unit>> EditAsync(string billNumber, Guid userId, EditAppeal request, CancellationToken cancellationToken)
    {
        if (FirstBroken(AppealRules.CheckDescription(request.Description), AppealRules.CheckReasonText(request.FirstReason)) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var appeal = await AppealReads.FindByBillAsync(db, billNumber, tracking: true, cancellationToken);
        if (appeal is null || !AppealReads.IsVisibleTo(appeal, userId))
        {
            return Error.NotFound("appeal.not-found");
        }

        if (appeal.InitiatorUserId != userId)
        {
            return Error.Forbidden("appeal.initiator-only");
        }

        if (appeal.Status is not (AppealStatuses.Pending or AppealStatuses.Returned))
        {
            return Error.Conflict("appeal.not-editable");
        }

        var now = clock.GetUtcNow();
        var description = AppealRules.NormaliseText(request.Description);
        if (description != appeal.Description)
        {
            db.Revisions.Add(Revise("appeal", appeal.Id, "description", appeal.Description, description, userId, now));
            appeal.Description = description;
            appeal.ScreeningFlags = [.. await screening.ScreenAsync(description, cancellationToken)];
        }

        var first = await db.Reasons
            .Where(r => r.AppealId == appeal.Id && r.AuthorUserId == userId)
            .OrderBy(r => r.CreatedAt)
            .FirstAsync(cancellationToken);
        var text = AppealRules.NormaliseText(request.FirstReason);
        if (text != first.Text)
        {
            db.Revisions.Add(Revise("reason", first.Id, "text", first.Text, text, userId, now));
            first.Text = text;
            first.ScreeningFlags = [.. await screening.ScreenAsync(text, cancellationToken)];
        }

        appeal.Status = AppealStatuses.Pending;
        appeal.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> ChangeThresholdAsync(string billNumber, Guid userId, int threshold, CancellationToken cancellationToken)
    {
        if (ThresholdRules.CheckRequested(threshold, await reads.MinimumAsync(cancellationToken)) is { } below)
        {
            return RuleErrors.Of(below);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var appeal = await AppealReads.FindByBillAsync(db, billNumber, tracking: true, cancellationToken);
        if (appeal is null || !AppealReads.IsVisibleTo(appeal, userId))
        {
            return Error.NotFound("appeal.not-found");
        }

        if (appeal.InitiatorUserId != userId)
        {
            return Error.Forbidden("appeal.initiator-only");
        }

        if (ThresholdRules.CheckInitiatorChange(appeal.Status, appeal.ThresholdSetBy, appeal.FirstSummarisedAt) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        if (appeal.Threshold != threshold)
        {
            var now = clock.GetUtcNow();
            db.Revisions.Add(Revise("appeal", appeal.Id, "threshold", Number(appeal.Threshold), Number(threshold), userId, now));
            appeal.Threshold = threshold;
            appeal.ChangedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> TakePositionAsync(string billNumber, Guid userId, string side, CancellationToken cancellationToken)
    {
        if (AppealRules.CheckSide(side) is { } invalid)
        {
            return RuleErrors.Of(invalid);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ParticipationLock.TakeAsync(db, userId, cancellationToken);
        var appeal = await OpenAppealAsync(db, billNumber, userId, cancellationToken);
        if (!appeal.IsSuccess)
        {
            return appeal.Error!;
        }

        // Read after the lock: a consent withdrawn a moment ago is seen here (IPositionErasure takes the same lock).
        var participant = await participants.FindAsync(userId, cancellationToken);
        if (participant is null)
        {
            return Error.Forbidden("member.required");
        }

        if (!participant.HasPositionConsent)
        {
            return Error.Conflict("consent.required");
        }

        var now = clock.GetUtcNow();
        var position = await db.Positions.FirstOrDefaultAsync(p => p.AppealId == appeal.Value.Id && p.UserId == userId, cancellationToken);
        if (position is null)
        {
            db.Positions.Add(new Position
            {
                AppealId = appeal.Value.Id,
                UserId = userId,
                Side = side,
                CreatedAt = now,
                ChangedAt = now,
                MemberSince = participant.MemberSince,
            });
        }
        else if (position.Side != side)
        {
            if (AppealRules.ChangingSideDropsEndorsementsOfTheOldSide(position.Side, side))
            {
                var oldSide = position.Side;
                await db.Endorsements
                    .Where(e => e.AppealId == appeal.Value.Id && e.UserId == userId
                        && db.Reasons.Any(r => r.Id == e.ReasonId && r.Side == oldSide))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // Overwritten in place: no earlier side is kept anywhere (design 5.3).
            position.Side = side;
            position.ChangedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> ClearPositionAsync(string billNumber, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ParticipationLock.TakeAsync(db, userId, cancellationToken);
        var appeal = await OpenAppealAsync(db, billNumber, userId, cancellationToken);
        if (!appeal.IsSuccess)
        {
            return appeal.Error!;
        }

        await db.Endorsements.Where(e => e.AppealId == appeal.Value.Id && e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Positions.Where(p => p.AppealId == appeal.Value.Id && p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<IReadOnlyList<ReasonInfo>>> FindSimilarAsync(string billNumber, NewReason request, CancellationToken cancellationToken)
    {
        if (FirstBroken(AppealRules.CheckSide(request.Side), AppealRules.CheckReasonText(request.Text)) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeal = await AppealReads.FindByBillAsync(db, billNumber, tracking: false, cancellationToken);
        if (appeal is null || appeal.Status != AppealStatuses.Open)
        {
            return Error.NotFound("appeal.not-found");
        }

        var existing = await db.Reasons.AsNoTracking()
            .Where(r => r.AppealId == appeal.Id && r.Side == request.Side && r.Status == ReasonStatuses.Published)
            .ToListAsync(cancellationToken);
        var similar = ReasonSimilarity.MostSimilar(AppealRules.NormaliseText(request.Text), existing, r => r.Text);
        var names = await reads.NamesAsync(similar.Select(r => r.AuthorUserId), cancellationToken);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, similar.Select(r => r.Id).ToList(), cancellationToken);
        return Result<IReadOnlyList<ReasonInfo>>.Success(similar.Select(r => AppealReads.ToInfo(r, names, endorsements)).ToList());
    }

    public async Task<Result<ReasonCreated>> WriteReasonAsync(string billNumber, Guid userId, NewReason request, CancellationToken cancellationToken)
    {
        if (FirstBroken(AppealRules.CheckSide(request.Side), AppealRules.CheckReasonText(request.Text)) is { } broken)
        {
            return RuleErrors.Of(broken);
        }

        var text = AppealRules.NormaliseText(request.Text);
        var flags = await screening.ScreenAsync(text, cancellationToken);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ParticipationLock.TakeAsync(db, userId, cancellationToken);
        var appeal = await OpenAppealAsync(db, billNumber, userId, cancellationToken);
        if (!appeal.IsSuccess)
        {
            return appeal.Error!;
        }

        if (await participants.FindAsync(userId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        // Either side, whatever the author's own (design 8.2): the position is not read, and writing never stores one.
        var counted = AppealRules.CountedReasonStatuses.ToList();
        var written = await db.Reasons.CountAsync(r => r.AppealId == appeal.Value.Id && r.AuthorUserId == userId && counted.Contains(r.Status), cancellationToken);
        var limit = await settings.GetAsync(SettingKeys.AppealsReasonsPerPerson, cancellationToken);
        if (AppealRules.CheckNewReason(request.Side, written, limit) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        var reason = new Reason
        {
            Id = Ids.New(clock),
            AppealId = appeal.Value.Id,
            Side = request.Side,
            AuthorUserId = userId,
            Text = text,
            Status = flags.Count > 0 ? ReasonStatuses.Pending : ReasonStatuses.Published,
            ScreeningFlags = [.. flags],
            CreatedAt = clock.GetUtcNow(),
        };
        db.Reasons.Add(reason);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ReasonCreated(reason.Id, reason.Status);
    }

    public async Task<Result<Unit>> EndorseAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ParticipationLock.TakeAsync(db, userId, cancellationToken);
        var reason = await db.Reasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reasonId && r.Status == ReasonStatuses.Published, cancellationToken);
        var appeal = reason is null ? null : await db.Appeals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == reason.AppealId, cancellationToken);
        if (reason is null || appeal is null || appeal.Status != AppealStatuses.Open)
        {
            return Error.NotFound("reason.not-found");
        }

        if (await db.Endorsements.AnyAsync(e => e.ReasonId == reasonId && e.UserId == userId, cancellationToken))
        {
            return Result.Ok();
        }

        var side = await db.Positions.Where(p => p.AppealId == appeal.Id && p.UserId == userId).Select(p => p.Side).FirstOrDefaultAsync(cancellationToken);
        var already = await db.Endorsements.CountAsync(e => e.AppealId == appeal.Id && e.UserId == userId, cancellationToken);
        var limit = await settings.GetAsync(SettingKeys.AppealsEndorsementsPerPerson, cancellationToken);
        if (AppealRules.CheckEndorsement(side, reason.Side, already, limit) is { } refused)
        {
            return RuleErrors.Of(refused);
        }

        db.Endorsements.Add(new Endorsement { ReasonId = reasonId, UserId = userId, AppealId = appeal.Id, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> RemoveEndorsementAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ParticipationLock.TakeAsync(db, userId, cancellationToken);
        var appealId = await db.Reasons.Where(r => r.Id == reasonId).Select(r => (Guid?)r.AppealId).FirstOrDefaultAsync(cancellationToken);
        if (appealId is null)
        {
            return Error.NotFound("reason.not-found");
        }

        var status = await db.Appeals.Where(a => a.Id == appealId).Select(a => a.Status).FirstAsync(cancellationToken);
        if (status != AppealStatuses.Open)
        {
            return Error.Conflict("appeal.not-open");
        }

        await db.Endorsements.Where(e => e.ReasonId == reasonId && e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// The author withdraws their own argument (design 8.2, decided 2026-11-13), while the appeal is open: the row is
    /// deleted with its "Man svarbu" marks, arguments once merged into it stay merged but point nowhere, and open reports
    /// on it are closed. A summary already made keeps its frozen text. The audit entry names the bill, not the text or
    /// the side. Someone else's argument: <c>reason.author-only</c>; a hidden, merged or unknown one: not found; on an
    /// appeal that is not open (closed, or still with the moderators): <c>appeal.not-open</c>.
    /// </summary>
    public async Task<Result<Unit>> WithdrawReasonAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ParticipationLock.TakeAsync(db, userId, cancellationToken);
        var counted = AppealRules.CountedReasonStatuses.ToList();
        var reason = await db.Reasons.FirstOrDefaultAsync(r => r.Id == reasonId && counted.Contains(r.Status), cancellationToken);
        if (reason is null)
        {
            return Error.NotFound("reason.not-found");
        }

        if (reason.AuthorUserId != userId)
        {
            return Error.Forbidden("reason.author-only");
        }

        var appeal = await db.Appeals.AsNoTracking().FirstAsync(a => a.Id == reason.AppealId, cancellationToken);
        if (appeal.Status != AppealStatuses.Open)
        {
            return Error.Conflict("appeal.not-open");
        }

        var now = clock.GetUtcNow();
        await db.Endorsements.Where(e => e.ReasonId == reasonId).ExecuteDeleteAsync(cancellationToken);
        await db.Reasons.Where(r => r.MergedIntoId == reasonId).ExecuteUpdateAsync(s => s.SetProperty(r => r.MergedIntoId, (Guid?)null), cancellationToken);
        await db.Reports.Where(r => r.TargetId == reasonId && r.Status == "open")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "closed").SetProperty(r => r.ClosedAt, now), cancellationToken);
        db.Reasons.Remove(reason);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(
                "reason.withdrawn", userId, "member", "reason:" + reasonId,
                System.Text.Json.JsonSerializer.SerializeToElement(new { bill = appeal.BillNumber }, System.Text.Json.JsonSerializerOptions.Web),
                null),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> ReportAsync(Guid userId, NewReport request, CancellationToken cancellationToken)
    {
        if (!ReportGrounds.All.Contains(request.Ground))
        {
            return Error.Validation("report.ground", "ground");
        }

        var text = string.IsNullOrWhiteSpace(request.Text) ? null : AppealRules.NormaliseText(request.Text);
        if (text is { Length: > ReportGrounds.TextMaxLength })
        {
            return Error.Validation("report.too-long", "text");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        Guid appealId;
        switch (request.TargetKind)
        {
            case "appeal":
                var appeal = await db.Appeals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.TargetId, cancellationToken);
                if (appeal is null || appeal.Status is not (AppealStatuses.Open or AppealStatuses.Closed))
                {
                    return Error.NotFound("report.target-not-found");
                }

                appealId = appeal.Id;
                break;
            case "reason":
                var reason = await db.Reasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.TargetId && r.Status == ReasonStatuses.Published, cancellationToken);
                if (reason is null)
                {
                    return Error.NotFound("report.target-not-found");
                }

                appealId = reason.AppealId;
                break;
            default:
                return Error.Validation("report.target", "targetKind");
        }

        if (await participants.FindAsync(userId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        // One open report per person and target; reporting again changes nothing.
        if (await db.Reports.AnyAsync(r => r.TargetId == request.TargetId && r.ReporterUserId == userId && r.Status == "open", cancellationToken))
        {
            return Result.Ok();
        }

        db.Reports.Add(new Report
        {
            Id = Ids.New(clock),
            TargetKind = request.TargetKind,
            TargetId = request.TargetId,
            AppealId = appealId,
            ReporterUserId = userId,
            Ground = request.Ground,
            Text = text,
            CreatedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // The same person reported the same thing twice at once: one report stands.
        }

        return Result.Ok();
    }

    public async Task<Result<Unit>> FlagDateAsync(string billNumber, Guid userId, NewFlag request, CancellationToken cancellationToken)
    {
        if (!FlagKinds.All.Contains(request.Kind))
        {
            return Error.Validation("flag.kind", "kind");
        }

        if (!FlagKinds.IsLink(request.Link))
        {
            return Error.Validation("flag.link", "link");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : AppealRules.NormaliseText(request.Note);
        if (note is { Length: > 1000 })
        {
            return Error.Validation("flag.note", "note");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeal = await OpenAppealAsync(db, billNumber, userId, cancellationToken);
        if (!appeal.IsSuccess)
        {
            return appeal.Error!;
        }

        // The initiator and any participant (design 8.4): someone with a side on this appeal.
        var participates = appeal.Value.InitiatorUserId == userId
            || await db.Positions.AnyAsync(p => p.AppealId == appeal.Value.Id && p.UserId == userId, cancellationToken);
        if (!participates)
        {
            return Error.Conflict("flag.participants-only");
        }

        if (await db.Flags.AnyAsync(f => f.AppealId == appeal.Value.Id && f.ReporterUserId == userId && f.Status == "open", cancellationToken))
        {
            return Result.Ok();
        }

        db.Flags.Add(new Flag
        {
            Id = Ids.New(clock),
            AppealId = appeal.Value.Id,
            Kind = request.Kind,
            Link = request.Link.Trim(),
            Note = note,
            ReporterUserId = userId,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>The open appeal on a bill, as seen by the member acting on it.</summary>
    private static async Task<Result<Appeal>> OpenAppealAsync(AppealsDb db, string billNumber, Guid userId, CancellationToken ct)
    {
        var appeal = await AppealReads.FindByBillAsync(db, billNumber, tracking: false, ct);
        if (appeal is null || !AppealReads.IsVisibleTo(appeal, userId))
        {
            return Error.NotFound("appeal.not-found");
        }

        return appeal.Status == AppealStatuses.Open ? appeal : Error.Conflict("appeal.not-open");
    }

    private async Task<AppealInfo> InfoAsync(AppealsDb db, Appeal appeal, CancellationToken ct)
    {
        var counts = ClosingRules.Shown(
            (await reads.CountsAsync(db, [appeal.Id], ct))[appeal.Id],
            appeal.ClosedFor, appeal.ClosedForOlderThan30Days, appeal.ClosedAgainst, appeal.ClosedAgainstOlderThan30Days);
        var reasons = await db.Reasons.AsNoTracking()
            .Where(r => r.AppealId == appeal.Id && r.Status == ReasonStatuses.Published)
            .ToListAsync(ct);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, reasons.Select(r => r.Id).ToList(), ct);
        var names = await reads.NamesAsync(reasons.Select(r => r.AuthorUserId).Append(appeal.InitiatorUserId), ct);
        var bill = await reads.BillAsync(appeal.BillNumber, ct);
        var minimum = await reads.MinimumAsync(ct);
        var ordered = reasons
            .OrderByDescending(r => endorsements.GetValueOrDefault(r.Id))
            .ThenBy(r => r.CreatedAt)
            .Select(r => AppealReads.ToInfo(r, names, endorsements))
            .ToList();
        return new AppealInfo(
            appeal.Id,
            bill,
            appeal.Status,
            names.GetValueOrDefault(appeal.InitiatorUserId),
            appeal.Description,
            appeal.OpenedAt,
            appeal.PublishedAt,
            counts,
            AppealReads.Threshold(appeal, counts, minimum),
            ordered,
            appeal.Status == AppealStatuses.Returned ? appeal.ReturnNote : null,
            appeal.Status == AppealStatuses.Open ? await reads.SurgeAsync(db, appeal.Id, ct) : null,
            appeal.ClosingVoteId,
            appeal.ClosedAt,
            appeal.Status == AppealStatuses.Closed ? null : (await agenda.ForBillsAsync([appeal.BillNumber], ct)).GetValueOrDefault(appeal.BillNumber));
    }

    internal static Revision Revise(string kind, Guid id, string field, string? oldValue, string? newValue, Guid? by, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        EntityKind = kind,
        EntityId = id,
        Field = field,
        OldValue = oldValue,
        NewValue = newValue,
        ChangedByUserId = by,
        ChangedAt = at,
    };

    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? FirstBroken(params string?[] codes) => codes.FirstOrDefault(c => c is not null);
}

/// <summary>Grounds for a report (design 8.3, Digital Services Act notice and action).</summary>
internal static class ReportGrounds
{
    public const int TextMaxLength = 1000;

    public static readonly IReadOnlyList<string> All = ["illegal", "personal-data", "insult", "spam", "other"];
}

/// <summary>
/// Serialises one person's participation (positions, arguments, endorsements, consent withdrawal) with a transaction
/// advisory lock, so per-person limits and the consent check hold under concurrent requests.
/// </summary>
internal static class ParticipationLock
{
    public static Task TakeAsync(AppealsDb db, Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.participation:" + userId.ToString("N")], ct);

    /// <summary>Serialises opening appeals on one bill.</summary>
    public static Task TakeBillAsync(AppealsDb db, string billNumber, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.appeal-bill:" + billNumber], ct);

    public static string KeyOf(Guid userId) => "lt2.participation:" + userId.ToString("N");
}

internal static class TransactionExtensions
{
    public static System.Data.Common.DbTransaction Db(this IDbContextTransaction transaction) => transaction.GetDbTransaction();
}
