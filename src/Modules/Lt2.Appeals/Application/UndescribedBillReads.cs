using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// "Projektai be aprašymo" (design 8.3, owner 2026-11-25): where a description is most wanted. Composed in memory like the
/// active list of <c>/projektai</c> (<see cref="AppealsService"/>): the active bills and their planned dates are the
/// Parliament module's (<see cref="IPlannedAgenda"/>), the followers the Notifications module's (<see cref="IFollowerCounts"/>,
/// numbers only), the records, sides, arguments and members' asks for a description (8.13.2) this module's. Inclusion and order are <see cref="UndescribedRules"/>.
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
        var listed = await ListedAsync(db, cancellationToken);
        var planned = await agenda.ForBillsAsync(listed.Select(b => b.Bill.Number).ToList(), cancellationToken);
        var rows = listed.Select(b =>
        {
            var summarised = b.Record is { Description.Length: > 0 };
            return new UndescribedBill(
                b.Bill,
                summarised ? b.Record!.PlainTitle : null,
                summarised ? b.Record!.DescriptionSource : null,
                b.Participants,
                b.Arguments,
                b.Followers,
                planned.GetValueOrDefault(b.Bill.Number),
                b.Asks,
                b.Finished);
        });
        var ordered = UndescribedRules.Order(rows, r => r.Asks, r => r.Participants, r => r.Arguments, r => r.Followers, r => r.Planned?.Date, r => r.Bill.Number);
        return new UndescribedBills(ordered.Take(Limit).ToList(), ordered.Count);
    }

    /// <summary>How many bills the list holds (the tab's count, design 8.3): decided as the list decides, without its rows.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await ListedAsync(db, cancellationToken)).Count;
    }

    /// <summary>A bill on the list and the numbers that put it there.</summary>
    private sealed record Listed(BillFacts Bill, BillRecord? Record, int Asks, bool Finished, int Participants, int Arguments, int Followers);

    /// <summary>The bills on the list, unordered: the one filter of the list and of its count.</summary>
    private async Task<List<Listed>> ListedAsync(AppealsDb db, CancellationToken cancellationToken)
    {
        // Active bills (design 8.12.1), and every bill members asked a description for (8.13.2), a finished one too.
        var active = await records.ActiveAsync(db, cancellationToken);
        var activeNumbers = active.Select(b => b.Number).ToHashSet(StringComparer.Ordinal);
        var asks = await db.DescriptionAsks.AsNoTracking()
            .GroupBy(a => a.BillNumber)
            .Select(g => new { Bill = g.Key, Count = g.Count() })
            .ToDictionaryAsync(a => a.Bill, a => a.Count, StringComparer.Ordinal, cancellationToken);
        var askedElsewhere = asks.Keys.Where(n => !activeNumbers.Contains(n)).ToList();
        var finished = await reads.BillsAsync(askedElsewhere, cancellationToken);
        var candidates = active.Concat(askedElsewhere.Select(n => finished.GetValueOrDefault(n)).OfType<BillFacts>()).ToList();

        var numbers = candidates.Select(b => b.Number).ToList();
        var recordsOnBills = await db.BillRecords.AsNoTracking().Records()
            .Where(a => numbers.Contains(a.BillNumber))
            .ToDictionaryAsync(a => a.BillNumber, StringComparer.Ordinal, cancellationToken);

        // Without a record, or with an open or closed one that wants a description; a hidden record is not listed.
        var wanted = candidates
            .Where(b => recordsOnBills.GetValueOrDefault(b.Number) is not { } r
                || (r.Status != BillRecordStatuses.Rejected && UndescribedRules.WantsDescription(r.Description, r.DescriptionSource)))
            .ToList();

        var recordIds = wanted.Select(b => recordsOnBills.GetValueOrDefault(b.Number)?.Id).OfType<Guid>().ToList();
        var counts = await reads.CountsAsync(db, recordIds, cancellationToken);
        var arguments = await AppealReads.ArgumentCountsAsync(db, recordIds, cancellationToken);
        var following = await followers.BillsAsync(wanted.Select(b => b.Number).ToList(), cancellationToken);
        return wanted
            .Select(b =>
            {
                var record = recordsOnBills.GetValueOrDefault(b.Number);
                return new Listed(
                    b,
                    record,
                    asks.GetValueOrDefault(b.Number),
                    !activeNumbers.Contains(b.Number) || record?.Status == BillRecordStatuses.Closed,
                    record is null ? 0 : (counts.GetValueOrDefault(record.Id) ?? SideCounts.None).Total,
                    record is null ? 0 : arguments.GetValueOrDefault(record.Id),
                    following.GetValueOrDefault(b.Number));
            })
            .Where(b => UndescribedRules.IsListed(b.Asks, !b.Finished, b.Participants, b.Arguments, b.Followers))
            .ToList();
    }
}
