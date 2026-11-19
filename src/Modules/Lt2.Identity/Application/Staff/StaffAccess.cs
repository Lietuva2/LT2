using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Staff;

/// <summary>The current session as the staff rules see it.</summary>
internal sealed record StaffState(
    Guid UserId,
    Session? Session,
    Profile? Profile,
    IReadOnlyList<string> AccountActiveRoles,
    bool StaffPasskeySession,
    bool RecentlyConfirmed)
{
    /// <summary>The account has a staff role that has been activated (whatever this session is).</summary>
    public bool AccountHasActiveStaffRole => AccountActiveRoles.Count > 0;

    public StaffSession ToSession() =>
        StaffPasskeySession ? new StaffSession(AccountActiveRoles, RecentlyConfirmed) : StaffSession.None;
}

/// <summary>
/// Reads the staff rules from the database on every call (design 6.2), so a reset or a removed passkey takes effect at
/// once: a staff role is active only if it was granted, its activation code was used, the account has a staff passkey,
/// and this session was signed in with that passkey. A context of its own per call: the moderators' panels of a page ask
/// while other components of the same page read the scoped context.
/// </summary>
internal sealed class StaffAccess(IDbContextFactory<IdentityDb> contexts, TimeProvider clock)
{
    public async Task<StaffState?> ForAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = clock.GetUtcNow();
        Session? session = null;
        if (Guid.TryParse(principal.FindFirstValue(SessionTicketStore.SessionIdClaim), out var sessionId))
        {
            session = await db.Sessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.ExpiresAt > now, cancellationToken);
        }

        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var active = await ActiveRolesAsync(db, userId, profile, cancellationToken);
        var staffSession = profile?.StaffPasskeyId is { } staff && session?.PasskeyId is { } used && staff.AsSpan().SequenceEqual(used);
        return new StaffState(
            userId,
            session,
            profile,
            active,
            staffSession,
            staffSession && StaffRules.IsFresh(session!.PasskeyConfirmedAt, now));
    }

    /// <summary>Granted staff roles whose activation code was used, on an account that has a staff passkey.</summary>
    private static async Task<IReadOnlyList<string>> ActiveRolesAsync(IdentityDb db, Guid userId, Profile? profile, CancellationToken cancellationToken)
    {
        if (profile?.StaffPasskeyId is null)
        {
            return [];
        }

        var granted = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(cancellationToken);
        var activated = await db.RoleActivations
            .Where(a => a.UserId == userId && a.UsedAt != null)
            .Select(a => a.Role)
            .Distinct()
            .ToListAsync(cancellationToken);
        return Roles.Staff.Where(r => granted.Contains(r) && activated.Contains(r)).ToList();
    }
}

/// <summary>Writes the account notice email (design 6.2: the person is told by email) and the audit entry together.</summary>
internal sealed class AccountEvents(IOutbox outbox, IAuditLog audit)
{
    public async Task RecordAsync(
        DbTransaction transaction,
        string action,
        Guid targetUserId,
        Actor actor,
        object? details,
        AccountNoticeEmail? notice,
        CancellationToken cancellationToken)
    {
        await audit.WriteAsync(
            new AuditEntry(
                action,
                actor.UserId,
                actor.Role,
                targetUserId.ToString(),
                details is null ? null : JsonSerializer.SerializeToElement(details, JsonSerializerOptions.Web),
                actor.IpHash),
            transaction,
            cancellationToken);
        if (notice is not null)
        {
            await outbox.EnqueueAsync(OutboxMessage.Create(MessageKinds.AccountNoticeEmail, notice), transaction, cancellationToken);
        }
    }
}
