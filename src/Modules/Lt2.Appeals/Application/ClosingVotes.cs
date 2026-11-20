using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IClosingVotes"/> for the Parliament module (design 7.2, 8.1 step 6): whether a vote closed a bill's record. A
/// record keeps its closing vote while it is closed and while it is hidden after closing (restoring brings it back closed);
/// nothing else about the record leaves the module.
/// </summary>
internal sealed class ClosingVotes(IDbContextFactory<AppealsDb> contexts) : IClosingVotes
{
    public async Task<bool> ClosesARecordAsync(long voteId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.BillRecords.AsNoTracking().AnyAsync(r => r.ClosingVoteId == voteId, cancellationToken);
    }
}
