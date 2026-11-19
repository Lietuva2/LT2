using Lt2.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Staff;

/// <summary>
/// Design 8.3, the full lock of an account: while an administrator's lock stands no sign-in works. Every place that would sign an
/// account in or send it a sign-in code asks here, so the answers stay those of an unknown address (no code sent, the ordinary
/// invalid-code and invalid-passkey answers) and nothing tells whether an address is registered or locked.
/// </summary>
internal static class AccountLocks
{
    public static Task<bool> IsLockedAsync(IdentityDb db, Guid userId, CancellationToken cancellationToken) =>
        db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.LockedAt != null, cancellationToken);
}
