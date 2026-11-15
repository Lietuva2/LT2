using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IDigestSource"/>: every open bill record with the numbers a summary would carry. Only arguments a moderator has
/// seen (design 8.3), the two most endorsed per side (design 8.4); no author, no person.
/// </summary>
internal sealed class DigestSource(
    IDbContextFactory<AppealsDb> contexts, AppealReads reads, IBillCatalogue bills, IPlannedAgenda agenda, IPartySupporters supporters) : IDigestSource
{
    public const int ReasonsPerSide = 2;

    public async Task<IReadOnlyList<DigestCandidate>> CandidatesAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeals = await db.BillRecords.AsNoTracking().Where(a => a.Status == BillRecordStatuses.Open).ToListAsync(cancellationToken);
        if (appeals.Count == 0)
        {
            return [];
        }

        var ids = appeals.Select(a => a.Id).ToList();
        var counts = await reads.CountsAsync(db, ids, cancellationToken);
        var thresholdSettings = await reads.ThresholdSettingsAsync(cancellationToken);
        var titles = await bills.FindManyAsync(appeals.Select(a => a.BillNumber).ToList(), cancellationToken);
        var planned = await agenda.ForBillsAsync(appeals.Select(a => a.BillNumber).ToList(), cancellationToken);
        var seen = await db.Reasons.AsNoTracking()
            .Where(r => ids.Contains(r.RecordId) && r.Status == ReasonStatuses.Published && r.SeenAt != null)
            .Select(r => new { r.Id, r.RecordId, r.Side, r.Text, r.CreatedAt })
            .ToListAsync(cancellationToken);
        var endorsements = await AppealReads.EndorsementCountsAsync(db, seen.Select(r => r.Id).ToList(), cancellationToken);
        var surges = await reads.SurgesAsync(db, ids, cancellationToken);

        var result = new List<DigestCandidate>();
        foreach (var record in appeals)
        {
            var c = counts[record.Id];
            var threshold = AppealReads.Threshold(record, c, thresholdSettings);
            var surge = surges.GetValueOrDefault(record.Id);
            var top = Sides.All
                .SelectMany(side => seen.Where(r => r.RecordId == record.Id && r.Side == side)
                    .OrderByDescending(r => endorsements.GetValueOrDefault(r.Id)).ThenBy(r => r.CreatedAt)
                    .Take(ReasonsPerSide)
                    .Select(r => new DigestReason(r.Side, r.Text, endorsements.GetValueOrDefault(r.Id))))
                .ToList();
            result.Add(new DigestCandidate(
                record.Id,
                record.BillNumber,
                titles.GetValueOrDefault(record.BillNumber)?.Title ?? record.BillNumber,
                c.For,
                c.ForOlderThan30Days,
                c.Against,
                c.AgainstOlderThan30Days,
                threshold.Value,
                threshold.Reached,
                record.Sending == "held",
                surge?.SharePercent,
                surge?.WindowHours,
                planned.GetValueOrDefault(record.BillNumber),
                top));
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<PartyNumbers>>> PartyNumbersAsync(IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken)
    {
        var byParty = await supporters.SupportersByPartyAsync(cancellationToken);
        var result = new Dictionary<Guid, IReadOnlyList<PartyNumbers>>();
        if (byParty.Count == 0 || recordIds.Count == 0)
        {
            return result;
        }

        var ids = recordIds.ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var totals = await reads.CountsAsync(db, ids, cancellationToken);

        // One query per party for all the records, not one per record and party.
        var sidesByParty = new Dictionary<string, List<(Guid RecordId, string Side, int Count)>>();
        foreach (var (party, users) in byParty)
        {
            var userList = users.ToList();
            sidesByParty[party] = (await db.Positions.AsNoTracking()
                    .Where(p => ids.Contains(p.RecordId) && userList.Contains(p.UserId))
                    .GroupBy(p => new { p.RecordId, p.Side })
                    .Select(g => new { g.Key.RecordId, g.Key.Side, Count = g.Count() })
                    .ToListAsync(cancellationToken))
                .Select(r => (r.RecordId, r.Side, r.Count))
                .ToList();
        }

        foreach (var recordId in ids)
        {
            var numbers = new List<PartyNumbers>();
            foreach (var (party, _) in byParty)
            {
                var sides = sidesByParty[party].Where(s => s.RecordId == recordId).ToList();
                var forCount = sides.FirstOrDefault(s => s.Side == Sides.For).Count;
                var againstCount = sides.FirstOrDefault(s => s.Side == Sides.Against).Count;

                // Each side's group is published only from 20 people (design 6.3, 8.4), and only when the rest of that
                // side is not a smaller group that subtraction from the total shown beside it would reveal.
                var published = new PartyNumbers(
                    party,
                    GroupRules.PublishableBeside(forCount, totals[recordId].For),
                    GroupRules.PublishableBeside(againstCount, totals[recordId].Against));
                if (published.For is not null || published.Against is not null)
                {
                    numbers.Add(published);
                }
            }

            result[recordId] = numbers;
        }

        return result;
    }
}
