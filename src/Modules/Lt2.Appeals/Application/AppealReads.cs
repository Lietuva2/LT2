using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// Reads shared by the handlers: counts, thresholds, arguments with their authors' names. Counts are aggregates read
/// from <c>appeals.position</c>; no read here returns who took which side.
/// </summary>
internal sealed class AppealReads(IParticipants participants, IBillCatalogue bills, ISettings settings, TimeProvider clock)
{
    public async Task<int> MinimumAsync(CancellationToken ct) => await settings.GetAsync(SettingKeys.DigestThresholdMinimum, ct);

    public async Task<AppealRulesInfo> RulesAsync(CancellationToken ct)
    {
        var minimum = await MinimumAsync(ct);
        var offered = ThresholdRules.Offered(await settings.GetAsync(SettingKeys.DigestThresholdDefault, ct), minimum);
        return new AppealRulesInfo(
            offered,
            minimum,
            AppealRules.ReasonMaxLength,
            await settings.GetAsync(SettingKeys.AppealsReasonsPerPerson, ct),
            await settings.GetAsync(SettingKeys.AppealsEndorsementsPerPerson, ct),
            AppealRules.DescriptionMaxLength);
    }

    /// <summary>The published counts of each given appeal: both sides, each with the count for accounts older than 30 days.</summary>
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

    /// <summary>The surge note for an appeal, or <c>null</c> when the rule (design 6.6, three settings) does not hold.</summary>
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

    public static ThresholdInfo Threshold(Appeal appeal, SideCounts counts, int minimum)
    {
        var effective = ThresholdRules.Effective(appeal.Threshold, minimum, appeal.FirstSummarisedAt);
        return new ThresholdInfo(
            effective,
            appeal.ThresholdSetBy,
            appeal.ThresholdNote,
            counts.Total,
            ThresholdRules.Reached(counts.Total, effective),
            appeal.FirstSummarisedAt is not null);
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

    /// <summary>The bill's published appeal (open or closed), at most one (design 8.1).</summary>
    public static Task<Appeal?> FindPublishedAsync(AppealsDb db, string billNumber, bool tracking, CancellationToken ct)
    {
        var number = NormaliseBillNumber(billNumber);
        var query = tracking ? db.Appeals : db.Appeals.AsNoTracking();
        return query.FirstOrDefaultAsync(a => a.BillNumber == number && (a.Status == AppealStatuses.Open || a.Status == AppealStatuses.Closed), ct);
    }

    /// <summary>
    /// The appeal a person sees on a bill (design 8.1): the published one if there is one, otherwise their own waiting or
    /// returned proposal. Someone else's proposal is never found here.
    /// </summary>
    public static async Task<Appeal?> FindForAsync(AppealsDb db, string billNumber, Guid? viewer, bool tracking, CancellationToken ct)
    {
        if (await FindPublishedAsync(db, billNumber, tracking, ct) is { } published)
        {
            return published;
        }

        if (viewer is not { } userId)
        {
            return null;
        }

        var number = NormaliseBillNumber(billNumber);
        var query = tracking ? db.Appeals : db.Appeals.AsNoTracking();
        return await query.FirstOrDefaultAsync(
            a => a.BillNumber == number && a.InitiatorUserId == userId && (a.Status == AppealStatuses.Pending || a.Status == AppealStatuses.Returned),
            ct);
    }

    public static string NormaliseBillNumber(string number) => number.Trim().ToUpperInvariant();

    /// <summary>A pending or returned proposal is visible only to its author (design 8.1); a rejected or superseded one to nobody.</summary>
    public static bool IsVisibleTo(Appeal appeal, Guid? viewer) => appeal.Status switch
    {
        AppealStatuses.Open or AppealStatuses.Closed => true,
        AppealStatuses.Pending or AppealStatuses.Returned => viewer is { } v && v == appeal.InitiatorUserId,
        _ => false,
    };
}
