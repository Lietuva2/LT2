using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>The moderators' restrictions on writing (design 8.3) that hold now, as every handler reads them.</summary>
internal static class WritingRestrictionReads
{
    /// <summary>The restrictions holding at <paramref name="now"/>, oldest first; one person's when <paramref name="userId"/> is given.</summary>
    public static async Task<List<WritingRestriction>> HoldingAsync(AppealsDb db, Guid? userId, DateTimeOffset now, CancellationToken ct) =>
        (await Unlifted(db, userId)
            .OrderBy(w => w.SetAt)
            .ToListAsync(ct))
        .Where(w => WritingRules.Holds(w.EndsAt, w.LiftedAt, now))
        .ToList();

    /// <summary>
    /// How many restrictions hold at <paramref name="now"/> (the moderators' tab "Apribojimai", design 8.3): the same filter as
    /// <see cref="HoldingAsync"/>, reading only the two times it needs.
    /// </summary>
    public static async Task<int> CountHoldingAsync(AppealsDb db, DateTimeOffset now, CancellationToken ct) =>
        (await Unlifted(db, null).Select(w => new { w.EndsAt, w.LiftedAt }).ToListAsync(ct))
        .Count(w => WritingRules.Holds(w.EndsAt, w.LiftedAt, now));

    private static IQueryable<WritingRestriction> Unlifted(AppealsDb db, Guid? userId) =>
        db.WritingRestrictions.AsNoTracking().Where(w => w.LiftedAt == null && (userId == null || w.UserId == userId));

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
