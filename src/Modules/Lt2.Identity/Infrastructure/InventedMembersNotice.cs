using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lt2.Identity.Infrastructure;

/// <summary>
/// <see cref="IInventedMembersNotice"/>: any profile with an address of the demonstration's invented members
/// (<see cref="InventedMembers.Prefix"/> on <see cref="InventedMembers.Domain"/>). Asked on every page, so the answer is cached
/// for a minute; each call has its own context, as the layout and the page ask at the same time.
/// </summary>
internal sealed class InventedMembersNotice(IDbContextFactory<IdentityDb> contexts, IMemoryCache cache) : IInventedMembersNotice
{
    /// <summary>The cached answer's key (a test forgets it to ask the database again).</summary>
    internal const string CacheKey = "identity:invented-members";

    public async Task<bool> HoldsInventedMembersAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            var pattern = InventedMembers.Prefix + "%@" + InventedMembers.Domain;
            return await db.Profiles.AnyAsync(p => EF.Functions.Like(p.NormalizedEmail, pattern), cancellationToken);
        });
}
