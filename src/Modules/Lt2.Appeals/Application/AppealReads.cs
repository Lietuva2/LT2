using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>The two threshold settings (design 8.12.2): the default of all bills and the administrator's minimum.</summary>
internal sealed record ThresholdSettings(int Default, int Minimum);

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
            AppealRules.ReasonMaxLength,
            await settings.GetAsync(SettingKeys.AppealsReasonsPerPerson, ct),
            await settings.GetAsync(SettingKeys.AppealsEndorsementsPerPerson, ct),
            AppealRules.DescriptionMaxLength,
            assistant.IsConfigured && texts.CanRead && await settings.GetAsync(SettingKeys.AiSummariesPerDay, ct) > 0);
    }

    /// <summary>The published counts of each given record: both sides, each with the count for accounts older than 30 days.</summary>
    public async Task<Dictionary<Guid, SideCounts>> CountsAsync(AppealsDb db, IReadOnlyCollection<Guid> appealIds, CancellationToken ct)
    {
        var cutoff = CountRules.EstablishedBefore(clock.GetUtcNow());
        var rows = await db.Positions.AsNoTracking()
            .Where(p => appealIds.Contains(p.AppealId))
            .GroupBy(p => new { p.AppealId, p.Side })
            .Select(g => new { g.Key.AppealId, g.Key.Side, Total = g.Count(), Older = g.Count(p => p.MemberSince <= cutoff) })
            .ToListAsync(ct);
        return appealIds.ToDictionary(
            id => id,
            id =>
            {
                var forRow = rows.FirstOrDefault(r => r.AppealId == id && r.Side == Sides.For);
                var againstRow = rows.FirstOrDefault(r => r.AppealId == id && r.Side == Sides.Against);
                return CountRules.Publish(forRow?.Total ?? 0, forRow?.Older ?? 0, againstRow?.Total ?? 0, againstRow?.Older ?? 0);
            });
    }

    /// <summary>The surge note for a record, or <c>null</c> when the rule (design 6.6, three settings) does not hold.</summary>
    public async Task<SurgeInfo?> SurgeAsync(AppealsDb db, Guid appealId, CancellationToken ct)
    {
        var hours = await settings.GetAsync(SettingKeys.SurgeWindowHours, ct);
        var share = await settings.GetAsync(SettingKeys.SurgeMinimumShare, ct);
        var minimum = await settings.GetAsync(SettingKeys.SurgeMinimumAccounts, ct);
        var since = clock.GetUtcNow() - TimeSpan.FromHours(hours);
        var counts = await db.Positions.AsNoTracking()
            .Where(p => p.AppealId == appealId && p.CreatedAt >= since)
            .GroupBy(p => 1)
            .Select(g => new { Recent = g.Count(), New = g.Count(p => p.MemberSince >= since) })
            .FirstOrDefaultAsync(ct);
        if (counts is null || !SurgeRules.IsSurge(counts.Recent, counts.New, share, minimum))
        {
            return null;
        }

        return new SurgeInfo(SurgeRules.SharePercent(counts.Recent, counts.New), hours, counts.Recent, counts.New);
    }

    /// <summary>The threshold of a bill as everyone sees it; <paramref name="appeal"/> null for a bill without a record.</summary>
    public static ThresholdInfo Threshold(Appeal? appeal, SideCounts counts, ThresholdSettings threshold)
    {
        var effective = ThresholdRules.Effective(appeal?.Threshold, threshold.Default, threshold.Minimum);
        return new ThresholdInfo(
            effective,
            appeal?.ThresholdSetBy ?? ThresholdSetters.Default,
            appeal?.ThresholdNote,
            counts.Total,
            ThresholdRules.Reached(counts.Total, effective, appeal?.FirstSummarisedAt),
            appeal?.FirstSummarisedAt is not null);
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
    public static Task<Appeal?> FindRecordAsync(AppealsDb db, string billNumber, bool tracking, CancellationToken ct)
    {
        var number = NormaliseBillNumber(billNumber);
        var query = tracking ? db.Appeals : db.Appeals.AsNoTracking();
        return query.FirstOrDefaultAsync(
            a => a.BillNumber == number && (a.Status == AppealStatuses.Open || a.Status == AppealStatuses.Closed || a.Status == AppealStatuses.Rejected),
            ct);
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
    /// bill's lock is held until it ends). <c>bill.not-found</c> for a bill not in the catalogue, <c>appeal.not-open</c>
    /// for a finished bill without a record. An existing record is returned whatever its status; the caller checks it.
    /// </summary>
    public async Task<Result<Appeal>> EnsureAsync(AppealsDb db, string billNumber, CancellationToken ct)
    {
        if (await AppealReads.FindRecordAsync(db, billNumber, tracking: false, ct) is { } existing)
        {
            return existing;
        }

        var bill = await bills.FindAsync(AppealReads.NormaliseBillNumber(billNumber), ct);
        if (bill is null)
        {
            return Error.NotFound("bill.not-found");
        }

        if (RecordRules.CheckParticipation(null, await bills.FinalVoteAsync(bill.Number, ct) is not null) is { } finished)
        {
            return RuleErrors.Of(finished);
        }

        // Only creating takes the bill's lock, so taking part on a bill that has its record is never serialised per bill.
        await ParticipationLock.TakeBillAsync(db, bill.Number, ct);
        if (await AppealReads.FindRecordAsync(db, bill.Number, tracking: false, ct) is { } meanwhile)
        {
            return meanwhile;
        }

        var now = clock.GetUtcNow();
        var record = new Appeal
        {
            Id = Ids.New(clock),
            BillNumber = bill.Number,
            Status = AppealStatuses.Open,
            OpenedAt = now,
            PublishedAt = now,
            ChangedAt = now,
            ThresholdSetBy = ThresholdSetters.Default,
        };
        db.Appeals.Add(record);
        await db.SaveChangesAsync(ct);
        db.Entry(record).State = EntityState.Detached;
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

        return await bills.FinalVoteAsync(bill.Number, ct) is null ? bill : Error.Conflict("appeal.not-open");
    }
}
