using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lt2.Identity.Application.Account;

/// <summary>
/// Design 6.5: a person downloads everything they gave and deletes the account. Deletion removes, in one transaction,
/// what the Appeals module holds of them (positions, marks, arguments, reports, flags; through <see cref="IAccountData"/>)
/// and the account with everything that hangs on it (profile, consents, party support, sessions, codes, passkeys, roles).
/// Counts fall; frozen summary snapshots keep their totals. Nothing is logged or audited with the address.
/// </summary>
internal sealed class AccountDataHandlers(IdentityDb db, IAccountData appeals, SignInManager<User> signIn, TimeProvider clock)
{
    public async Task<Result<object>> ExportAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Error.Unauthorized("auth.required");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (user is null || profile is null)
        {
            return Error.Unauthorized("auth.required");
        }

        var consents = await db.Consents.AsNoTracking().Where(c => c.UserId == userId)
            .Select(c => new { c.Kind, c.TextVersion, c.GivenAt, c.WithdrawnAt }).ToListAsync(cancellationToken);
        var party = await db.PartySupports.AsNoTracking().Where(p => p.UserId == userId).Select(p => new { p.Party, p.GivenAt }).FirstOrDefaultAsync(cancellationToken);
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.UserId == userId)
            .Select(s => new { s.CreatedAt, s.LastSeenAt, s.Device, s.Method }).ToListAsync(cancellationToken);
        var passkeys = await db.Set<IdentityUserPasskey<Guid>>().AsNoTracking().CountAsync(p => p.UserId == userId, cancellationToken);
        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                           where ur.UserId == userId
                           select r.Name).ToListAsync(cancellationToken);
        return new
        {
            exportedAt = DateTimeOffset.UtcNow,
            account = new
            {
                email = user.Email,
                displayName = profile.DisplayName,
                verificationLevel = profile.VerificationLevel,
                createdAt = profile.CreatedAt,
                registeredAt = profile.RegisteredAt,
                invitationCode = profile.InvitationCode,
                myInvitation = await MyInvitationAsync(userId, cancellationToken),
                roles,
                passkeys,
            },
            consents,
            partySupport = party,
            sessions,
            appeals = await appeals.ExportAsync(userId, cancellationToken),
        };
    }

    /// <summary>The person's own current code and how many completed registration with their codes; no names.</summary>
    private async Task<object?> MyInvitationAsync(Guid userId, CancellationToken ct)
    {
        var own = await db.Invitations.AsNoTracking()
            .Where(i => i.Kind == InvitationKinds.Personal && i.IssuedByUserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        return own.Count == 0 ? null : new { code = own.FirstOrDefault(i => i.ReplacedAt == null)?.Code, completed = InvitationRules.MemberCount(own.Select(i => i.UsedCount)) };
    }

    public async Task<Result<Unit>> DeleteAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Error.Unauthorized("auth.required");
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await appeals.EraseAsync(userId, transaction.GetDbTransaction(), cancellationToken);

            // The person's invitation codes stop working and keep nothing of them (design 6.2); their counts, levels and
            // daily totals stay as anonymous numbers. Codes the person registered with keep counting what they brought.
            var now = clock.GetUtcNow();
            await db.Invitations.Where(i => i.IssuedByUserId == userId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(i => i.IssuedByUserId, (Guid?)null)
                        .SetProperty(i => i.SwitchedOffAt, i => i.Kind == InvitationKinds.Personal ? (i.SwitchedOffAt ?? now) : i.SwitchedOffAt),
                    cancellationToken);

            // The user row cascades to profile, consents, party support, sessions, codes, passkeys, roles, activations.
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await signIn.SignOutAsync();
        return Result.Ok();
    }
}