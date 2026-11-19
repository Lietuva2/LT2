using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IRecordClosureFeed"/> for the follow emails (design 8.1 step 6, 8.6): the records closed now because a moderator
/// recorded that the Seimas rejected the bill or its initiators withdrew it, closed in a window. A record reopened since, or hidden
/// by the moderators, is not closed now and is left out. Nothing about who closed it leaves the module.
/// </summary>
internal sealed class RecordClosureFeed(IDbContextFactory<AppealsDb> contexts) : IRecordClosureFeed
{
    public async Task<IReadOnlyList<RecordClosureFacts>> ClosedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var closed = await db.BillRecords.AsNoTracking()
            .Where(r => r.Status == BillRecordStatuses.Closed
                && (r.ClosedReason == ClosedReasons.Rejected || r.ClosedReason == ClosedReasons.Withdrawn)
                && r.ClosedAt >= from && r.ClosedAt < to
                && r.ClosedDecidedOn != null && r.ClosedSourceUrl != null)
            .OrderBy(r => r.ClosedAt)
            .Select(r => new { r.BillNumber, r.ClosedReason, r.ClosedDecidedOn, r.ClosedSourceUrl, r.ClosedAt })
            .ToListAsync(cancellationToken);
        return closed.Select(r => new RecordClosureFacts(r.BillNumber, r.ClosedReason!, r.ClosedDecidedOn!.Value, r.ClosedSourceUrl!, r.ClosedAt!.Value)).ToList();
    }
}
