using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>The writing restriction as other modules ask about it (design 8.3, 6.2), read as every handler here reads it.</summary>
internal sealed class WritingRestrictions(IDbContextFactory<AppealsDb> contexts, TimeProvider clock) : IWritingRestrictions
{
    public async Task<bool> IsRestrictedAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await WritingRestrictionReads.IsRestrictedAsync(db, userId, clock.GetUtcNow(), cancellationToken);
    }
}
