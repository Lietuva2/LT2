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
    IParticipants participants, IBillCatalogue bills, ISettings settings, IAiAssistant assistant, IBillTextSource texts, TimeProvider clock)
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
            assistant.IsConfigured && texts.CanRead && await settings.GetAsync(SettingKeys.AiSummariesPerDay, ct) > 0);
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

    public async Task<BillFacts> BillAsync(string number, CancellationToken ct) =>
        await bills.FindAsync(number, ct) ?? new BillFacts(number, number);

    public async Task<IReadOnlyDictionary<string, BillFacts>> BillsAsync(IReadOnlyCollection<string> numbers, CancellationToken ct) =>
        numbers.Count == 0 ? new Dictionary<string, BillFacts>() : await bills.FindManyAsync(numbers, ct);

    /// <summary>Endorsement counts of the given arguments.</summary>
    public static async Task<Dictionary<Guid, int>> EndorsementCountsAsync(AppealsDb db, IReadOnlyCollection<Guid> reasonIds, CancellationToken ct) =>
        await db.Endorsements.AsNoTracking()
            .Where(e => reasonIds.Contains(e.ReasonId))
            .GroupBy(e => e.ReasonId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public static ReasonInfo ToInfo(Reason r, IReadOnlyDictionary<Guid, string?> names, IReadOnlyDictionary<Guid, int> endorsements) =>
        new(r.Id, r.Side, r.Text, names.GetValueOrDefault(r.AuthorUserId), endorsements.GetValueOrDefault(r.Id), r.Status, r.CreatedAt);

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

        if (RecordRules.CheckParticipation(null, await bills.FinalVoteAsync(bill.Number, ct) is not null) is { } finished)
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

        return await bills.FinalVoteAsync(bill.Number, ct) is null ? bill : Error.Conflict("record.not-open");
    }

    /// <summary>
    /// Whether the Seimas has finished the bill (design 6.8, <see cref="RecordRules.IsFinished"/>): the one answer for taking
    /// part and for organisations' statements.
    /// </summary>
    public async Task<bool> IsFinishedAsync(string? recordStatus, string billNumber, CancellationToken ct) =>
        RecordRules.IsFinished(
            recordStatus,
            finalVoteRecorded: recordStatus != BillRecordStatuses.Closed && await bills.FinalVoteAsync(AppealReads.NormaliseBillNumber(billNumber), ct) is not null);
}
