using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// <see cref="IMpResponseFeed"/> for the follow email "Seimo narių atsakymai" (design 8.20.1): the responses public now whose
/// stance became public in a window. Withdrawn and hidden responses are left out, and so are those on a bill whose record the
/// moderators hid (its page shows the bill only, design 8.12.3). No text and no writer leave the module.
/// </summary>
internal sealed class MpResponseFeed(IDbContextFactory<AppealsDb> contexts) : IMpResponseFeed
{
    public async Task<IReadOnlyList<PublicResponseFacts>> AnnouncedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.MpResponses.AsNoTracking()
            .Where(r => r.Status == ResponseStatuses.Published && r.AnnouncedAt >= from && r.AnnouncedAt < to)
            .Where(r => !db.BillRecords.Any(b => b.BillNumber == r.BillNumber && b.Status == BillRecordStatuses.Rejected))
            .OrderBy(r => r.AnnouncedAt)
            .Select(r => new PublicResponseFacts(r.BillNumber, r.SeimasPersonId, r.Stance, r.AnnouncedAt))
            .ToListAsync(cancellationToken);
    }
}
