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
                planned.GetValueOrDefault(b.Bill.Number));
        });
        var ordered = UndescribedRules.Order(rows, r => r.Participants, r => r.Arguments, r => r.Followers, r => r.Planned?.Date, r => r.Bill.Number);
        return new UndescribedBills(ordered.Take(Limit).ToList(), ordered.Count);
    }

    /// <summary>How many bills the list holds (the tab's count, design 8.3): decided as the list decides, without its rows.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return (await ListedAsync(db, cancellationToken)).Count;
    }

    /// <summary>A bill on the list and the numbers that put it there.</summary>
    private sealed record Listed(BillFacts Bill, BillRecord? Record, int Participants, int Arguments, int Followers);

    /// <summary>The bills on the list, unordered: the one filter of the list and of its count.</summary>
    private async Task<List<Listed>> ListedAsync(AppealsDb db, CancellationToken cancellationToken)
    {
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

        var openIds = wanted.Select(b => recordsOnBills.GetValueOrDefault(b.Number)?.Id).OfType<Guid>().ToList();
        var counts = await reads.CountsAsync(db, openIds, cancellationToken);
        var arguments = await AppealReads.ArgumentCountsAsync(db, openIds, cancellationToken);
        var following = await followers.BillsAsync(wanted.Select(b => b.Number).ToList(), cancellationToken);
        return wanted
            .Select(b =>
            {
                var record = recordsOnBills.GetValueOrDefault(b.Number);
                return new Listed(
                    b,
                    record,
                    record is null ? 0 : (counts.GetValueOrDefault(record.Id) ?? SideCounts.None).Total,
                    record is null ? 0 : arguments.GetValueOrDefault(record.Id),
                    following.GetValueOrDefault(b.Number));
            })
            .Where(b => UndescribedRules.IsListed(b.Participants, b.Arguments, b.Followers))
            .ToList();
    }
}
