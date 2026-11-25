using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// "Projektai be aprašymo" (design 8.3, owner 2026-11-25): where a description is most wanted. Composed in memory like the
/// active list of <c>/projektai</c> (<see cref="AppealsService"/>): the active bills and their planned dates are the
/// Parliament module's (<see cref="IPlannedAgenda"/>), the followers the Notifications module's (<see cref="IFollowerCounts"/>,
/// numbers only), the records, sides and arguments this module's. Inclusion and order are <see cref="UndescribedRules"/>.
/// Holds only numbers the bill's page shows, never who.
/// </summary>
internal sealed class UndescribedBillReads(
    IDbContextFactory<AppealsDb> contexts, AppealReads reads, BillRecords records, IPlannedAgenda agenda, IFollowerCounts followers)
{
    /// <summary>The most bills the list shows, like each of the queue's lists (owner, 2026-11-18).</summary>
    public const int Limit = 100;

    public async Task<UndescribedBills> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // Active bills (design 8.12.1) with an open record or none; a hidden or closed record is not active.
        var active = await records.ActiveAsync(db, cancellationToken);
        var numbers = active.Select(b => b.Number).ToList();
        var recordsOnBills = await db.BillRecords.AsNoTracking().Records()
            .Where(a => numbers.Contains(a.BillNumber))
            .ToDictionaryAsync(a => a.BillNumber, StringComparer.Ordinal, cancellationToken);
        var wanted = active
            .Where(b => recordsOnBills.GetValueOrDefault(b.Number) is not { } r
                || (r.Status == BillRecordStatuses.Open && UndescribedRules.WantsDescription(r.Description, r.DescriptionSource)))
            .ToList();

        var wantedNumbers = wanted.Select(b => b.Number).ToList();
        var openIds = wanted.Select(b => recordsOnBills.GetValueOrDefault(b.Number)?.Id).OfType<Guid>().ToList();
        var counts = await reads.CountsAsync(db, openIds, cancellationToken);
        var arguments = await db.Reasons.AsNoTracking()
            .Where(r => openIds.Contains(r.RecordId) && r.Status == ReasonStatuses.Published)
            .GroupBy(r => r.RecordId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var following = await followers.BillsAsync(wantedNumbers, cancellationToken);
        var planned = await agenda.ForBillsAsync(wantedNumbers, cancellationToken);

        var rows = wanted.Select(b =>
        {
            var record = recordsOnBills.GetValueOrDefault(b.Number);
            var summarised = record is { Description.Length: > 0 };
            return new UndescribedBill(
                b,
                summarised ? record!.PlainTitle : null,
                summarised ? record!.DescriptionSource : null,
                record is null ? 0 : (counts.GetValueOrDefault(record.Id) ?? SideCounts.None).Total,
                record is null ? 0 : arguments.GetValueOrDefault(record.Id),
                following.GetValueOrDefault(b.Number),
                planned.GetValueOrDefault(b.Number));
        });
        var ordered = UndescribedRules.Order(rows, r => r.Participants, r => r.Arguments, r => r.Followers, r => r.Planned?.Date, r => r.Bill.Number);
        return new UndescribedBills(ordered.Take(Limit).ToList(), ordered.Count);
    }
}
