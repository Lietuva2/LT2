using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Profiles;

/// <summary>
/// The public-profile consent that counts (design 8.25.1): given to the text version in force and not withdrawn. A profile switched
/// on under an earlier text stays on without one (owner, 2026-11-19) and its person is asked on the account page.
/// </summary>
internal static class PublicProfileConsents
{
    public static Task<bool> CurrentAsync(IdentityDb db, Guid userId, CancellationToken cancellationToken) =>
        db.Consents.AsNoTracking().AnyAsync(
            c => c.UserId == userId && c.Kind == PublicProfileConsent.Kind && c.TextVersion == PublicProfileConsent.TextVersion && c.WithdrawnAt == null,
            cancellationToken);
}
