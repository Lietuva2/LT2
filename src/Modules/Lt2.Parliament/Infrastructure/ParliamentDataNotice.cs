using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lt2.Parliament.Infrastructure;

/// <summary>
/// Whether the stored parliament data is invented: true as soon as any stored response came from the stand-in (not the
/// migrator's stricter rule in <see cref="ParliamentMaintenance"/>, which also asks that nothing real is stored). Asked on
/// every page, so the answer is cached for a minute. Each call has its own context: the layout and the page ask at the
/// same time while a page is rendered.
/// </summary>
internal sealed class ParliamentDataNotice(IDbContextFactory<ParliamentDb> contexts, IMemoryCache cache) : IParliamentDataNotice
{
    /// <summary>The cached answer's key (a test forgets it to ask the database again).</summary>
    internal const string CacheKey = "parliament:synthetic";

    public async Task<bool> IsParliamentDataInventedAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            return await db.RawDocuments.AnyAsync(r => r.Synthetic, cancellationToken);
        });
}
