using System.Globalization;
using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.Appeals.Screening;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Appeals.Application;

/// <summary>
/// What members do on a bill (design 8.12.1, 8.2): take or change a side, write and endorse arguments, report. Every active
/// bill is open: the first side or argument creates the bill's record (<see cref="BillRecords"/>). Every action of one
/// person runs under that person's lock (<see cref="ParticipationLock"/>), so the limits per person and the consent check
/// cannot be raced.
/// </summary>
internal sealed class AppealsService(
    IDbContextFactory<AppealsDb> contexts,
    AppealReads reads,
    BillRecords records,
    IParticipants participants,
    IBillCatalogue bills,
    IPreScreening screening,
    IPlannedAgenda agenda,
    ISettings settings,
    IAuditLog audit,
    TimeProvider clock) : IAppealsModule
{
    private const int PageSize = 20;

    /// <summary>"Planuojami artimiausiu metu" (design 8.12.5): a planned date within this many days from today.</summary>
    private const int SoonDays = 14;

    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    public Task<AppealRulesInfo> RulesAsync(CancellationToken cancellationToken) => reads.RulesAsync(cancellationToken);

    public async Task<AppealPage> ListAsync(BillListQuery query, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var threshold = await reads.ThresholdSettingsAsync(cancellationToken);
        var offset = int.TryParse(query.Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var o) ? o : 0;

        if (query.Closed)
        {
            // The closed tab (design 8.12.5): records the Seimas finished, newest first, with their frozen counts.
            var closed = (await db.Appeals.AsNoTracking().Where(a => a.Status == AppealStatuses.Closed).ToListAsync(cancellationToken))
                .OrderByDescending(a => a.ClosedAt).ThenBy(a => a.BillNumber, StringComparer.Ordinal).ToList();
            var closedFacts = await reads.BillsAsync(closed.Select(a => a.BillNumber).ToList(), cancellationToken);
            var matching = closed.Where(a => BillListRules.Matches(query.Search, a.BillNumber, closedFacts.GetValueOrDefault(a.BillNumber)?.Title ?? "")).ToList();
            var closedPage = matching.Skip(Math.Min(offset, matching.Count)).Take(PageSize).ToList();
            var closedIds = closedPage.Select(a => a.Id).ToList();
            var live = await reads.CountsAsync(db, closedIds, cancellationToken);
            var closedArguments = await ArgumentCountsAsync(db, closedIds, cancellationToken);
            var closedItems = closedPage.Select(a =>
            {
                var shown = ClosingRules.Shown(live[a.Id], a.ClosedFor, a.ClosedForOlderThan30Days, a.ClosedAgainst, a.ClosedAgainstOlderThan30Days);
                return new AppealListItem(a.Id, closedFacts.GetValueOrDefault(a.BillNumber) ?? new BillFacts(a.BillNumber, a.BillNumber), a.Status, shown,
                    AppealReads.Threshold(a, shown, threshold), null, closedArguments.GetValueOrDefault(a.Id));
            }).ToList();
            return new AppealPage(closedItems, matching.Count, Next(offset, closedPage.Count, matching.Count));
        }

        // Active bills (design 8.12.1): in the catalogue and not finished; a bill whose record is hidden or closed is not
        // offered here.
        var active = (await bills.ActiveAsync(cancellationToken)).Where(b => BillListRules.Matches(query.Search, b.Number, b.Title)).ToList();
        var numbers = active.Select(b => b.Number).ToList();
        var recordsOnBills = await db.Appeals.AsNoTracking()
            .Where(a => numbers.Contains(a.BillNumber) && (a.Status == AppealStatuses.Open || a.Status == AppealStatuses.Closed || a.Status == AppealStatuses.Rejected))
            .ToDictionaryAsync(a => a.BillNumber, StringComparer.Ordinal, cancellationToken);
        var openIds = recordsOnBills.Values.Where(a => a.Status == AppealStatuses.Open).Select(a => a.Id).ToList();
        var counts = await reads.CountsAsync(db, openIds, cancellationToken);
        var arguments = await ArgumentCountsAsync(db, openIds, cancellationToken);
        var planned = await agenda.ForBillsAsync(numbers, cancellationToken);
        var soon = VilniusToday().AddDays(SoonDays);
        var candidates = active
            .Where(b => !recordsOnBills.TryGetValue(b.Number, out var r) || r.Status == AppealStatuses.Open)
            .Where(b => !query.PlannedOnly || (planned.TryGetValue(b.Number, out var p) && p.Date <= soon))
            .Select(b =>
            {
                var record = recordsOnBills.GetValueOrDefault(b.Number);
                var c = record is null ? SideCounts.None : counts[record.Id];
                return new AppealListItem(
                    record?.Id, b, AppealStatuses.Open, c, AppealReads.Threshold(record, c, threshold), planned.GetValueOrDefault(b.Number),
                    record is null ? 0 : arguments.GetValueOrDefault(record.Id));
            });
        var ordered = BillListRules.Order(candidates, BillSorts.Of(query.Sort), i => i.Counts.Total, i => i.Arguments, i => i.Planned?.Date, i => i.Bill.Number);
        var page = ordered.Skip(Math.Min(offset, ordered.Count)).Take(PageSize).ToList();
        return new AppealPage(page, ordered.Count, Next(offset, page.Count, ordered.Count));
    }

    /// <summary>How many published arguments each given record has.</summary>
    private static async Task<Dictionary<Guid, int>> ArgumentCountsAsync(AppealsDb db, IReadOnlyCollection<Guid> appealIds, CancellationToken ct) =>
        await db.Reasons.AsNoTracking()
            .Where(r => appealIds.Contains(r.AppealId) && r.Status == ReasonStatuses.Published)
            .GroupBy(r => r.AppealId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private static string? Next(int offset, int taken, int total) =>
        offset + taken < total ? (offset + taken).ToString(CultureInfo.InvariantCulture) : null;

    public async Task<Result<AppealInfo>> GetAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken) is { } record)
        {
            return await InfoAsync(db, record, cancellationToken);
        }

        // No record: an active bill is open with zero counts (design 8.12.1); a finished one shows the bill only.
        var bill = await records.ActiveBillAsync(billNumber, cancellationToken);
        if (!bill.IsSuccess)
        {
            return bill.Error!.Code == "appeal.not-open" ? Error.NotFound("appeal.not-found") : bill.Error!;
        }

        var threshold = await reads.ThresholdSettingsAsync(cancellationToken);
        return new AppealInfo(
            null, bill.Value, AppealStatuses.Open, "", null, null, null, null, SideCounts.None, AppealReads.Threshold(null, SideCounts.None, threshold), [], null, null, null,
            (await agenda.ForBillsAsync([bill.Value.Number], cancellationToken)).GetValueOrDefault(bill.Value.Number), null);
    }

    public async Task<Result<IReadOnlyList<(DateTimeOffset ChangedAt, string? OldText, string? NewText)>>> HistoryAsync(string billNumber, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            var bill = await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken);
            return bill is null
                ? Error.NotFound("bill.not-found")
                : Result<IReadOnlyList<(DateTimeOffset, string?, string?)>>.Success([]);
        }

        if (record.Status == AppealStatuses.Rejected)
        {
            return Error.NotFound("appeal.not-found");
        }

        var rows = await db.Revisions.AsNoTracking()
            .Where(r => r.EntityKind == "appeal" && r.EntityId == record.Id && r.Field == "description")
            .OrderBy(r => r.ChangedAt)
            .Select(r => new { r.ChangedAt, r.OldValue, r.NewValue })
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<(DateTimeOffset, string?, string?)>>.Success(rows.Select(r => (r.ChangedAt, r.OldValue, r.NewValue)).ToList());
    }

    public async Task<Result<MyAppeal>> MineAsync(string billNumber, Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        var facts = await participants.FindAsync(userId, cancellationToken);
        var reasonLimit = await settings.GetAsync(SettingKeys.AppealsReasonsPerPerson, cancellationToken);
        var endorsementLimit = await settings.GetAsync(SettingKeys.AppealsEndorsementsPerPerson, cancellationToken);
        if (record is null || record.Status == AppealStatuses.Rejected)
        {
            // Nothing of the person's on a bill without a record; on a hidden one nothing is shown (design 8.12.3).
            var bill = await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken);
            return bill is null
                ? Error.NotFound("bill.not-found")
                : new MyAppeal(null, [], AppealRules.Left(0, endorsementLimit), [], AppealRules.Left(0, reasonLimit), facts?.HasPositionConsent == true);
        }

        var side = await db.Positions.AsNoTracking()
            .Where(p => p.AppealId == record.Id && p.UserId == userId)
            .Select(p => p.Side)
            .FirstOrDefaultAsync(cancellationToken);
        var endorsed = await db.Endorsements.AsNoTracking()
            .Where(e => e.AppealId == record.Id && e.UserId == userId)
            .Select(e => e.ReasonId)
            .ToListAsync(cancellationToken);
        var mine = await db.Reasons.AsNoTracking()
            .Where(r => r.AppealId == record.Id && r.AuthorUserId == userId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        var names = await reads.NamesAsync([userId], cancellationToken);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, mine.Select(r => r.Id).ToList(), cancellationToken);
        return new MyAppeal(
            side,
            endorsed,
            AppealRules.Left(endorsed.Count, endorsementLimit),
            mine.Select(r => AppealReads.ToInfo(r, names, endorsements)).ToList(),
            AppealRules.Left(mine.Count(r => AppealRules.CountedReasonStatuses.Contains(r.Status)), reasonLimit),
            facts?.HasPositionConsent == true);
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

        var appeal = await OpenRecordAsync(db, billNumber, cancellationToken);
        if (!appeal.IsSuccess)
        {
            return appeal.Error!;
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

        // Clearing creates nothing: on a bill without a record there is no side to clear.
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            return await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken) is null ? Error.NotFound("bill.not-found") : Result.Ok();
        }

        if (record.Status != AppealStatuses.Open)
        {
            return Error.Conflict("appeal.not-open");
        }

        await db.Endorsements.Where(e => e.AppealId == record.Id && e.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Positions.Where(p => p.AppealId == record.Id && p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
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
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            // An active bill nobody wrote on has nothing similar yet.
            var bill = await records.ActiveBillAsync(billNumber, cancellationToken);
            return bill.IsSuccess ? Result<IReadOnlyList<ReasonInfo>>.Success([]) : bill.Error!;
        }

        if (record.Status != AppealStatuses.Open)
        {
            return Error.Conflict("appeal.not-open");
        }

        var existing = await db.Reasons.AsNoTracking()
            .Where(r => r.AppealId == record.Id && r.Side == request.Side && r.Status == ReasonStatuses.Published)
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
        if (await participants.FindAsync(userId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        if (await RestrictedAsync(db, userId, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        var appeal = await OpenRecordAsync(db, billNumber, cancellationToken);
        if (!appeal.IsSuccess)
        {
            return appeal.Error!;
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
    /// The author withdraws their own argument (design 8.2, decided 2026-11-13), while the bill is open: the row is
    /// deleted with its "Man svarbu" marks, arguments once merged into it stay merged but point nowhere, and open reports
    /// on it are closed. A summary already made keeps its frozen text. The audit entry names the bill, not the text or
    /// the side. Someone else's argument: <c>reason.author-only</c>; a hidden, merged or unknown one: not found; on a
    /// record that is not open (closed or hidden): <c>appeal.not-open</c>.
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

    public async Task<MyRestriction?> MyRestrictionAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var holding = (await db.WritingRestrictions.AsNoTracking().Where(w => w.UserId == userId && w.LiftedAt == null).ToListAsync(cancellationToken))
            .Where(w => WritingRules.Holds(w.EndsAt, w.LiftedAt, now))
            .OrderBy(w => w.EndsAt is null ? 0 : 1)
            .ThenByDescending(w => w.EndsAt)
            .FirstOrDefault();
        return holding is null ? null : new MyRestriction(WritingRules.Until(holding.EndsAt), holding.Statement);
    }

    /// <summary>
    /// A moderator's restriction on writing (design 8.3) blocks what the person writes for others to read: arguments and
    /// date flags. Never sides, marks, reports or anything about the account.
    /// </summary>
    private async Task<Error?> RestrictedAsync(AppealsDb db, Guid userId, CancellationToken ct)
    {
        var rows = await db.WritingRestrictions.AsNoTracking()
            .Where(w => w.UserId == userId && w.LiftedAt == null)
            .Select(w => new { w.EndsAt, w.LiftedAt })
            .ToListAsync(ct);
        return WritingRules.MayWrite(rows.Select(r => (r.EndsAt, r.LiftedAt)), clock.GetUtcNow()) ? null : Error.Forbidden("writing.restricted");
    }

    public async Task<Result<Unit>> ReportAsync(Guid userId, NewReport request, CancellationToken cancellationToken)
    {
        if (!ReportGrounds.For(request.TargetKind).Contains(request.Ground))
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
                // What is reported on a bill's record is its description (design 8.13): a record without one has nothing to report.
                var appeal = await db.Appeals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.TargetId, cancellationToken);
                if (appeal is null || appeal.Status is not (AppealStatuses.Open or AppealStatuses.Closed) || appeal.Description.Length == 0)
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

        // Participants only (design 8.4): someone with a side on the bill, so a bill without a record has none.
        var record = await AppealReads.FindRecordAsync(db, billNumber, tracking: false, cancellationToken);
        if (record is null)
        {
            return await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), cancellationToken) is null
                ? Error.NotFound("bill.not-found")
                : Error.Conflict("flag.participants-only");
        }

        if (record.Status != AppealStatuses.Open)
        {
            return Error.Conflict("appeal.not-open");
        }

        if (!await db.Positions.AnyAsync(p => p.AppealId == record.Id && p.UserId == userId, cancellationToken))
        {
            return Error.Conflict("flag.participants-only");
        }

        if (await RestrictedAsync(db, userId, cancellationToken) is { } restricted)
        {
            return restricted;
        }

        if (await db.Flags.AnyAsync(f => f.AppealId == record.Id && f.ReporterUserId == userId && f.Status == "open", cancellationToken))
        {
            return Result.Ok();
        }

        db.Flags.Add(new Flag
        {
            Id = Ids.New(clock),
            AppealId = record.Id,
            Kind = request.Kind,
            Link = request.Link.Trim(),
            Note = note,
            ReporterUserId = userId,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// The bill's open record for a member taking part, created if the bill is active and has none (design 8.12.1). A
    /// closed or hidden record and a finished bill refuse with <c>appeal.not-open</c>.
    /// </summary>
    private async Task<Result<Appeal>> OpenRecordAsync(AppealsDb db, string billNumber, CancellationToken ct)
    {
        var record = await records.EnsureAsync(db, billNumber, ct);
        if (!record.IsSuccess)
        {
            return record.Error!;
        }

        return RecordRules.CheckParticipation(record.Value.Status, billFinished: false) is { } refused ? RuleErrors.Of(refused) : record.Value;
    }

    private async Task<AppealInfo> InfoAsync(AppealsDb db, Appeal appeal, CancellationToken ct)
    {
        var bill = await reads.BillAsync(appeal.BillNumber, ct);
        var threshold = await reads.ThresholdSettingsAsync(ct);
        if (appeal.Status == AppealStatuses.Rejected)
        {
            // Taking part is closed by the moderators (design 8.12.3): the bill and the statement, nothing of the record.
            var statement = await db.ModerationActions.AsNoTracking()
                .Where(m => m.TargetId == appeal.Id && m.Action == "hide")
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => m.Statement)
                .FirstOrDefaultAsync(ct);
            return new AppealInfo(
                appeal.Id, bill, appeal.Status, "", null, null, null, null, SideCounts.None, AppealReads.Threshold(null, SideCounts.None, threshold), [], null, null, null, null,
                statement ?? "");
        }

        var counts = ClosingRules.Shown(
            (await reads.CountsAsync(db, [appeal.Id], ct))[appeal.Id],
            appeal.ClosedFor, appeal.ClosedForOlderThan30Days, appeal.ClosedAgainst, appeal.ClosedAgainstOlderThan30Days);
        var reasons = await db.Reasons.AsNoTracking()
            .Where(r => r.AppealId == appeal.Id && r.Status == ReasonStatuses.Published)
            .ToListAsync(ct);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, reasons.Select(r => r.Id).ToList(), ct);

        // An initiator's name only beside a description they wrote, before 2026-11-14 (design 8.12.4).
        var author = appeal.DescriptionSource == DescriptionSources.Initiator && appeal.InitiatorUserId is { } initiator ? initiator : (Guid?)null;
        var names = await reads.NamesAsync(reasons.Select(r => r.AuthorUserId).Concat(author is { } a ? [a] : []), ct);
        var ordered = reasons
            .OrderByDescending(r => endorsements.GetValueOrDefault(r.Id))
            .ThenBy(r => r.CreatedAt)
            .Select(r => AppealReads.ToInfo(r, names, endorsements))
            .ToList();
        return new AppealInfo(
            appeal.Id,
            bill,
            appeal.Status,
            appeal.Description,
            appeal.Description.Length == 0 ? null : appeal.DescriptionSource,
            author is { } by ? names.GetValueOrDefault(by) : null,
            appeal.Description.Length == 0 ? null : appeal.DescriptionUrl,
            appeal.Description.Length == 0 ? null : appeal.DescriptionAt,
            counts,
            AppealReads.Threshold(appeal, counts, threshold),
            ordered,
            appeal.Status == AppealStatuses.Open ? await reads.SurgeAsync(db, appeal.Id, ct) : null,
            appeal.ClosingVoteId,
            appeal.ClosedAt,
            appeal.Status == AppealStatuses.Closed ? null : (await agenda.ForBillsAsync([appeal.BillNumber], ct)).GetValueOrDefault(appeal.BillNumber),
            null);
    }

    public async Task<IReadOnlyDictionary<string, BillAppealState>> BillStatesAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken)
    {
        var numbers = billNumbers.Select(AppealReads.NormaliseBillNumber).Distinct().ToList();
        if (numbers.Count == 0)
        {
            return new Dictionary<string, BillAppealState>(StringComparer.Ordinal);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var known = await reads.BillsAsync(numbers, cancellationToken);
        var recordsOnBills = await db.Appeals.AsNoTracking()
            .Where(a => numbers.Contains(a.BillNumber) && (a.Status == AppealStatuses.Open || a.Status == AppealStatuses.Closed || a.Status == AppealStatuses.Rejected))
            .ToDictionaryAsync(a => a.BillNumber, StringComparer.Ordinal, cancellationToken);
        var counts = await reads.CountsAsync(db, recordsOnBills.Values.Where(a => a.Status != AppealStatuses.Rejected).Select(a => a.Id).ToList(), cancellationToken);
        var threshold = await reads.ThresholdSettingsAsync(cancellationToken);
        return numbers.Where(known.ContainsKey).ToDictionary(
            n => n,
            n =>
            {
                if (recordsOnBills.GetValueOrDefault(n) is not { } record)
                {
                    return new BillAppealState(n, null, AppealStatuses.Open, SideCounts.None, AppealReads.Threshold(null, SideCounts.None, threshold));
                }

                // A hidden record shows no numbers (design 8.12.3).
                var shown = record.Status == AppealStatuses.Rejected
                    ? SideCounts.None
                    : ClosingRules.Shown(counts[record.Id], record.ClosedFor, record.ClosedForOlderThan30Days, record.ClosedAgainst, record.ClosedAgainstOlderThan30Days);
                return new BillAppealState(n, record.Id, record.Status, shown, AppealReads.Threshold(record, shown, threshold));
            },
            StringComparer.Ordinal);
    }

    private DateOnly VilniusToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Vilnius).DateTime);

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

/// <summary>
/// Grounds for a report (design 8.3, Digital Services Act notice and action). A bill's description may also be reported as
/// inaccurate ("Pranešti apie klaidą", design 8.13); an argument may not: it is its author's own opinion.
/// </summary>
internal static class ReportGrounds
{
    public const int TextMaxLength = 1000;

    public const string Inaccurate = "inaccurate";

    public static readonly IReadOnlyList<string> All = ["illegal", "personal-data", "insult", "spam", "other"];

    public static readonly IReadOnlyList<string> Description = [Inaccurate, .. All];

    /// <summary>The grounds a report on this target may give.</summary>
    public static IReadOnlyList<string> For(string targetKind) => targetKind == "appeal" ? Description : All;
}

/// <summary>
/// Serialises one person's participation (positions, arguments, endorsements, consent withdrawal) with a transaction
/// advisory lock, so per-person limits and the consent check hold under concurrent requests.
/// </summary>
internal static class ParticipationLock
{
    public static Task TakeAsync(AppealsDb db, Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.participation:" + userId.ToString("N")], ct);

    /// <summary>Serialises creating a bill's record (design 8.12.1), taken after a person's lock when both are needed.</summary>
    public static Task TakeBillAsync(AppealsDb db, string billNumber, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.appeal-bill:" + billNumber], ct);

    public static string KeyOf(Guid userId) => "lt2.participation:" + userId.ToString("N");
}

internal static class TransactionExtensions
{
    public static System.Data.Common.DbTransaction Db(this IDbContextTransaction transaction) => transaction.GetDbTransaction();
}
