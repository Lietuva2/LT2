using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>The two threshold settings (design 8.12.2): the default of all bills and the administrator's minimum.</summary>
internal sealed record ThresholdSettings(int Default, int Minimum);

/// <summary>Query filters used by every read of bills' records.</summary>
internal static class RecordQueries
{
    /// <summary>The rows that are a bill's record (open, closed or hidden; <see cref="RecordRules.RecordStatuses"/>).</summary>
    public static IQueryable<BillRecord> Records(this IQueryable<BillRecord> appeals) => appeals.Where(a => RecordRules.RecordStatuses.Contains(a.Status));
}

/// <summary>
/// Reads shared by the handlers: counts, thresholds, arguments with their authors' names, the bill's record. Counts are
/// aggregates read from <c>appeals.position</c>; no read here returns who took which side.
/// </summary>
internal sealed class AppealReads(
    IParticipants participants, IPublicProfiles profiles, IBillCatalogue bills, ISettings settings, IAiAssistant assistant, IBillTextSource texts, TimeProvider clock)
{
    public async Task<int> MinimumAsync(CancellationToken ct) => await settings.GetAsync(SettingKeys.DigestThresholdMinimum, ct);

    public async Task<ThresholdSettings> ThresholdSettingsAsync(CancellationToken ct) =>
        new(await settings.GetAsync(SettingKeys.DigestThresholdDefault, ct), await MinimumAsync(ct));

    public async Task<AppealRulesInfo> RulesAsync(CancellationToken ct)
    {
        var threshold = await ThresholdSettingsAsync(ct);
        return new AppealRulesInfo(
            ThresholdRules.Offered(threshold.Default, threshold.Minimum),
            threshold.Minimum,
            await settings.GetAsync(SettingKeys.TextsMaxLength, ct),
            await settings.GetAsync(SettingKeys.AppealsReasonsPerPerson, ct),
            await settings.GetAsync(SettingKeys.AppealsEndorsementsPerPerson, ct),
            AppealRules.DescriptionMaxLength,
            assistant.IsConfigured && texts.CanRead && await settings.GetAsync(SettingKeys.AiSummariesPerDay, ct) > 0)
        {
            MinReasonWords = await settings.GetAsync(SettingKeys.AppealsMinReasonWords, ct),
        };
    }

    /// <summary>The live counts of each given record: both sides, each with the count for accounts older than 30 days.</summary>
    public async Task<Dictionary<Guid, SideCounts>> CountsAsync(AppealsDb db, IReadOnlyCollection<Guid> recordIds, CancellationToken ct)
    {
        if (recordIds.Count == 0)
        {
            return [];
        }

        var cutoff = CountRules.EstablishedBefore(clock.GetUtcNow());
        var rows = await db.Positions.AsNoTracking()
            .Where(p => recordIds.Contains(p.RecordId))
            .GroupBy(p => new { p.RecordId, p.Side })
            .Select(g => new { g.Key.RecordId, g.Key.Side, Total = g.Count(), Older = g.Count(p => p.MemberSince <= cutoff) })
            .ToListAsync(ct);
        return recordIds.Distinct().ToDictionary(
            id => id,
            id =>
            {
                var forRow = rows.FirstOrDefault(r => r.RecordId == id && r.Side == Sides.For);
                var againstRow = rows.FirstOrDefault(r => r.RecordId == id && r.Side == Sides.Against);
                return CountRules.Publish(forRow?.Total ?? 0, forRow?.Older ?? 0, againstRow?.Total ?? 0, againstRow?.Older ?? 0);
            });
    }

    /// <summary>
    /// The counts each given record shows (design 8.1): frozen ones once closed, live ones before. Live counts are read only
    /// for records that have none frozen.
    /// </summary>
    public async Task<Dictionary<Guid, SideCounts>> ShownCountsAsync(AppealsDb db, IReadOnlyCollection<BillRecord> records, CancellationToken ct)
    {
        var live = await CountsAsync(db, records.Where(r => !ClosingRules.IsFrozen(r)).Select(r => r.Id).ToList(), ct);
        return records.ToDictionary(r => r.Id, r => ClosingRules.Shown(r, live.GetValueOrDefault(r.Id) ?? SideCounts.None));
    }

    /// <summary>
    /// The surge note of each given record that has one (design 6.6, three settings): records where the rule does not hold
    /// are left out. One query for all of them.
    /// </summary>
    public async Task<Dictionary<Guid, SurgeInfo>> SurgesAsync(AppealsDb db, IReadOnlyCollection<Guid> recordIds, CancellationToken ct)
    {
        if (recordIds.Count == 0)
        {
            return [];
        }

        var hours = await settings.GetAsync(SettingKeys.SurgeWindowHours, ct);
        var share = await settings.GetAsync(SettingKeys.SurgeMinimumShare, ct);
        var minimum = await settings.GetAsync(SettingKeys.SurgeMinimumAccounts, ct);
        var since = clock.GetUtcNow() - TimeSpan.FromHours(hours);
        var rows = await db.Positions.AsNoTracking()
            .Where(p => recordIds.Contains(p.RecordId) && p.CreatedAt >= since)
            .GroupBy(p => p.RecordId)
            .Select(g => new { RecordId = g.Key, Recent = g.Count(), New = g.Count(p => p.MemberSince >= since) })
            .ToListAsync(ct);
        return rows
            .Where(r => SurgeRules.IsSurge(r.Recent, r.New, share, minimum))
            .ToDictionary(r => r.RecordId, r => new SurgeInfo(SurgeRules.SharePercent(r.Recent, r.New), hours, r.Recent, r.New));
    }

    /// <summary>The surge note for a record, or <c>null</c> when the rule (design 6.6) does not hold.</summary>
    public async Task<SurgeInfo?> SurgeAsync(AppealsDb db, Guid recordId, CancellationToken ct) =>
        (await SurgesAsync(db, [recordId], ct)).GetValueOrDefault(recordId);

    /// <summary>The threshold of a bill as everyone sees it; <paramref name="record"/> null for a bill without a record.</summary>
    public static ThresholdInfo Threshold(BillRecord? record, SideCounts counts, ThresholdSettings threshold)
    {
        var effective = ThresholdRules.Effective(record?.Threshold, threshold.Default, threshold.Minimum);
        return new ThresholdInfo(
            effective,
            record?.ThresholdSetBy ?? ThresholdSetters.Default,
            record?.ThresholdNote,
            counts.Total,
            ThresholdRules.Reached(counts.Total, effective, record?.FirstSummarisedAt),
            record?.FirstSummarisedAt is not null);
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return ids.Count == 0 ? new Dictionary<Guid, string?>() : await participants.DisplayNamesAsync(ids, ct);
    }

    /// <summary>The public profiles among the given authors (design 8.25.4): the link and the card beside their names.</summary>
    public async Task<IReadOnlyDictionary<Guid, PublicProfileFacts>> ProfilesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return ids.Count == 0 ? new Dictionary<Guid, PublicProfileFacts>() : await profiles.ForAccountsAsync(ids, ct);
    }

    public async Task<BillFacts> BillAsync(string number, CancellationToken ct) =>
        await bills.FindAsync(number, ct) ?? new BillFacts(number, number);

    public async Task<IReadOnlyDictionary<string, BillFacts>> BillsAsync(IReadOnlyCollection<string> numbers, CancellationToken ct) =>
        numbers.Count == 0 ? new Dictionary<string, BillFacts>() : await bills.FindManyAsync(numbers, ct);

    /// <summary>
    /// The counted "Man svarbu" of the given arguments (owner, 2026-11-18, <see cref="AppealRules.MarkCounts"/>): only marks
    /// whose giver's current side on the bill is the argument's side, joined with the positions when read, so a changed side
    /// moves the counts and nothing stored can drift. Every figure, order and summary choice uses these. One query: the marks
    /// by the endorsement's key (reason first), the argument and the position by theirs.
    /// </summary>
    public static async Task<Dictionary<Guid, int>> EndorsementCountsAsync(AppealsDb db, IReadOnlyCollection<Guid> reasonIds, CancellationToken ct) =>
        reasonIds.Count == 0
            ? []
            : await (from e in db.Endorsements.AsNoTracking()
                     join r in db.Reasons.AsNoTracking() on e.ReasonId equals r.Id
                     join p in db.Positions.AsNoTracking() on new { e.RecordId, e.UserId } equals new { p.RecordId, p.UserId }
                     where reasonIds.Contains(e.ReasonId) && p.Side == r.Side
                     group e by e.ReasonId into g
                     select new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    /// <summary>
    /// Every "Man svarbu" of the given arguments, counted or not: any mark is a reaction to the words (design 8.2, editing
    /// until someone reacts), whether or not it counts.
    /// </summary>
    public static async Task<Dictionary<Guid, int>> ReactionCountsAsync(AppealsDb db, IReadOnlyCollection<Guid> reasonIds, CancellationToken ct) =>
        reasonIds.Count == 0
            ? []
            : await db.Endorsements.AsNoTracking()
                .Where(e => reasonIds.Contains(e.ReasonId))
                .GroupBy(e => e.ReasonId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public static ReasonInfo ToInfo(
        Reason r, IReadOnlyDictionary<Guid, string?> names, IReadOnlyDictionary<Guid, int> endorsements, IReadOnlyDictionary<Guid, PublicProfileFacts>? profiles = null) =>
        new(r.Id, r.Side, r.Text, names.GetValueOrDefault(r.AuthorUserId), endorsements.GetValueOrDefault(r.Id), r.Status, r.CreatedAt,
            profiles?.GetValueOrDefault(r.AuthorUserId)?.ProfileId);

    /// <summary>The bill's record (open, closed or hidden), at most one (design 8.12.1); null while the bill has none.</summary>
    public static Task<BillRecord?> FindRecordAsync(AppealsDb db, string billNumber, bool tracking, CancellationToken ct)
    {
        var number = NormaliseBillNumber(billNumber);
        var query = tracking ? db.BillRecords : db.BillRecords.AsNoTracking();
        return query.Records().FirstOrDefaultAsync(a => a.BillNumber == number, ct);
    }

    public static string NormaliseBillNumber(string number) => number.Trim().ToUpperInvariant();
}

/// <summary>
/// Finding or creating a bill's record (design 8.12.1). The record is created when first needed, under the bill's lock and
/// behind the unique index, so two people arriving at the same moment land on one row. Nothing about who caused it is kept.
/// </summary>
internal sealed class BillRecords(IBillCatalogue bills, TimeProvider clock)
{
    /// <summary>
    /// The bill's record, created as an open one if the bill is active and has none. Must run inside a transaction (the
    /// bill's lock is held until it ends). <c>bill.not-found</c> for a bill not in the catalogue, <c>record.not-open</c>
    /// for a finished bill without a record. An existing record is returned whatever its status; the caller checks it.
    /// With <paramref name="forWrite"/> the bill's lock is taken first and the record comes back tracked: the caller
    /// changes it, and writes to one bill's record are serialised (design 6.8).
    /// </summary>
    public async Task<Result<BillRecord>> EnsureAsync(AppealsDb db, string billNumber, CancellationToken ct, bool forWrite = false)
    {
        var number = AppealReads.NormaliseBillNumber(billNumber);
        if (forWrite)
        {
            await AdvisoryLocks.BillAsync(db, number, ct);
        }

        if (await AppealReads.FindRecordAsync(db, number, tracking: forWrite, ct) is { } existing)
        {
            return existing;
        }

        var bill = await bills.FindAsync(number, ct);
        if (bill is null)
        {
            return Error.NotFound("bill.not-found");
        }

        if (RecordRules.CheckParticipation(null, await IsFinishedAsync(null, bill.Number, ct)) is { } finished)
        {
            return RuleErrors.Of(finished);
        }

        // Only creating, or writing, takes the bill's lock, so taking part on a bill that has its record is never serialised
        // per bill.
        if (!forWrite)
        {
            await AdvisoryLocks.BillAsync(db, number, ct);
            if (await AppealReads.FindRecordAsync(db, number, tracking: false, ct) is { } meanwhile)
            {
                return meanwhile;
            }
        }

        var now = clock.GetUtcNow();
        var record = new BillRecord
        {
            Id = Ids.New(clock),
            BillNumber = bill.Number,
            Status = BillRecordStatuses.Open,
            OpenedAt = now,
            PublishedAt = now,
            ChangedAt = now,
            ThresholdSetBy = ThresholdSetters.Default,
        };
        db.BillRecords.Add(record);
        await db.SaveChangesAsync(ct);
        if (!forWrite)
        {
            db.Entry(record).State = EntityState.Detached;
        }

        return record;
    }

    /// <summary>Whether the bill is active: in the catalogue and not finished by the Seimas (design 8.12.1).</summary>
    public async Task<Result<BillFacts>> ActiveBillAsync(string billNumber, CancellationToken ct)
    {
        var bill = await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), ct);
        if (bill is null)
        {
            return Error.NotFound("bill.not-found");
        }

        return await IsFinishedAsync(null, bill.Number, ct) ? Error.Conflict("record.not-open") : bill;
    }

    public async Task<IReadOnlyList<BillFacts>> ActiveAsync(AppealsDb db, CancellationToken ct)
    {
        var active = await bills.ActiveAsync(ct);
        var rejections = await bills.RejectionsAsync(active.Select(b => b.Number).ToList(), ct);
        if (rejections.Count == 0)
        {
            return active;
        }

        var numbers = rejections.Keys.ToList();
        var records = await db.BillRecords.AsNoTracking().Records().Where(r => numbers.Contains(r.BillNumber))
            .ToDictionaryAsync(r => r.BillNumber, ct);
        var now = clock.GetUtcNow();
        return active.Where(b => !IsPendingRejection(records.GetValueOrDefault(b.Number), rejections.GetValueOrDefault(b.Number), now)).ToList();
    }

    /// <summary>Whether the bill has finished, including between the import and the hourly closing job.</summary>
    public async Task<bool> IsFinishedAsync(BillRecord? record, string billNumber, CancellationToken ct) =>
        record?.Status == BillRecordStatuses.Closed
        || await bills.FinalVoteAsync(billNumber, ct) is not null
        || await PendingRejectionAsync(record, billNumber, ct) is not null;

    public async Task<BillRejection?> PendingRejectionAsync(BillRecord? record, string billNumber, CancellationToken ct)
    {
        var number = AppealReads.NormaliseBillNumber(billNumber);
        var decision = (await bills.RejectionsAsync([number], ct)).GetValueOrDefault(number);
        return IsPendingRejection(record, decision, clock.GetUtcNow()) ? decision : null;
    }

    public async Task<IReadOnlyList<BillRecord>> WithoutPendingRejectionsAsync(IReadOnlyList<BillRecord> records, CancellationToken ct)
    {
        var rejections = await bills.RejectionsAsync(records.Select(r => r.BillNumber).ToList(), ct);
        var now = clock.GetUtcNow();
        return records.Where(r => !IsPendingRejection(r, rejections.GetValueOrDefault(r.BillNumber), now)).ToList();
    }

    private static bool IsPendingRejection(BillRecord? record, BillRejection? decision, DateTimeOffset now) =>
        decision is not null && decision.HeldAt <= now && record?.LastAutomaticRejectionVoteId != decision.VoteId;
}
