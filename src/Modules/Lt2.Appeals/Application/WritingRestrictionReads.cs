using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>The moderators' restrictions on writing (design 8.3) that hold now, as every handler reads them.</summary>
internal static class WritingRestrictionReads
{
    /// <summary>The restrictions holding at <paramref name="now"/>, oldest first; one person's when <paramref name="userId"/> is given.</summary>
    public static async Task<List<WritingRestriction>> HoldingAsync(AppealsDb db, Guid? userId, DateTimeOffset now, CancellationToken ct) =>
        (await db.WritingRestrictions.AsNoTracking()
            .Where(w => w.LiftedAt == null && (userId == null || w.UserId == userId))
            .OrderBy(w => w.SetAt)
            .ToListAsync(ct))
        .Where(w => WritingRules.Holds(w.EndsAt, w.LiftedAt, now))
        .ToList();

    /// <summary>A restriction holds for the person now: they cannot write arguments, date flags or for an organisation.</summary>
    public static async Task<bool> IsRestrictedAsync(AppealsDb db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.WritingRestrictions.AsNoTracking()
            .Where(w => w.UserId == userId && w.LiftedAt == null)
            .Select(w => new { w.EndsAt, w.LiftedAt })
            .ToListAsync(ct);
        return !WritingRules.MayWrite(rows.Select(r => (r.EndsAt, r.LiftedAt)), now);
    }
}
