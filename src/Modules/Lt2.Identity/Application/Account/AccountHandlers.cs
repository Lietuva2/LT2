using System.Security.Claims;
using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Account;

/// <summary>The signed-in person's account and sessions.</summary>
internal sealed class AccountHandlers(IdentityDb db, UserManager<User> users, StaffAccess staff, TimeProvider clock)
{
    public async Task<Result<AccountSummary>> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Error.Unauthorized("auth.required");
        }

        var profile = await db.Profiles.AsNoTracking().FirstAsync(p => p.UserId == user.Id, cancellationToken);
        var roles = await users.GetRolesAsync(user);
        var passkeys = await users.GetPasskeysAsync(user);
        var session = (await staff.ForAsync(principal, cancellationToken))?.ToSession() ?? StaffSession.None;
        var now = clock.GetUtcNow();
        var awaitingActivation = await db.RoleActivations.AnyAsync(
            a => a.UserId == user.Id && a.UsedAt == null && a.ExpiresAt > now,
            cancellationToken);
        return new AccountSummary(
            user.Id,
            user.Email!,
            profile.DisplayName,
            profile.VerificationLevel,
            roles.Order(StringComparer.Ordinal).ToList(),
            passkeys.Count,
            profile.CreatedAt,
            !profile.IsPending,
            session.ActiveRoles,
            awaitingActivation,
            await db.Consents.AnyAsync(
                c => c.UserId == user.Id && c.Kind == PositionConsent.Kind && c.TextVersion == PositionConsent.TextVersion && c.WithdrawnAt == null,
                cancellationToken));
    }

    public async Task<IReadOnlyList<SessionSummary>> SessionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return [];
        }

        var current = principal.FindFirstValue(SessionTicketStore.SessionIdClaim);
        var now = clock.GetUtcNow();
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new { s.Id, s.CreatedAt, s.LastSeenAt, s.Device, s.Method })
            .ToListAsync(cancellationToken);
        return sessions
            .Select(s => new SessionSummary(s.Id, s.CreatedAt, s.LastSeenAt, s.Device, s.Method, s.Id.ToString() == current))
            .ToList();
    }

    public async Task<Result<Unit>> EndSessionAsync(ClaimsPrincipal principal, Guid sessionId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Error.Unauthorized("auth.required");
        }

        var deleted = await db.Sessions
            .Where(s => s.Id == sessionId && s.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 1 ? Result.Ok() : Error.NotFound("session.not-found");
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}

/// <summary>
/// Housekeeping: old sign-in codes, expired sessions, and accounts still pending after the configured number of days
/// (design 6.2), so an address typed by a stranger is not kept.
/// </summary>
internal sealed class MaintenanceHandler(IdentityDb db, ISettings settings, TimeProvider clock) : IIdentityMaintenance
{
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var codeCutoff = now - TimeSpan.FromDays(1);
        var codes = await db.SignInCodes.Where(c => c.CreatedAt < codeCutoff).ExecuteDeleteAsync(cancellationToken);
        var sessions = await db.Sessions.Where(s => s.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

        var pendingCutoff = now - TimeSpan.FromDays(await settings.GetAsync(SettingKeys.PendingAccountDays, cancellationToken));
        var pending = await db.Users
            .Where(u => db.Profiles.Any(p => p.UserId == u.Id && p.RegisteredAt == null && p.CreatedAt < pendingCutoff))
            .ExecuteDeleteAsync(cancellationToken);
        return codes + sessions + pending;
    }
}
