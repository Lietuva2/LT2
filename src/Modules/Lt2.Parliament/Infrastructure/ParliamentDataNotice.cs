using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lt2.Parliament.Infrastructure;

/// <summary>
/// Whether the stored parliament data is invented: true as soon as any stored response came from the stand-in. Asked on
/// every page, so the answer is cached for a minute.
/// </summary>
internal sealed class ParliamentDataNotice(ParliamentDb db, IMemoryCache cache) : IParliamentDataNotice
{
    private const string CacheKey = "parliament:synthetic";

    public async Task<bool> IsParliamentDataInventedAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return await db.RawDocuments.AnyAsync(r => r.Synthetic, cancellationToken);
        });
}
