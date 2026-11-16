using System.Security.Claims;
using System.Text.Json;
using Lt2.Identity.Application.Staff;
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
/// and the account with everything that hangs on it (profile, consents, party support, sessions, known browsers, codes,
/// passkeys, roles)
/// and the unsent emails to it.
/// Counts fall; frozen summary snapshots keep their totals. Nothing is logged or audited with the address.
/// </summary>
internal sealed class AccountDataHandlers(
    IdentityDb db, IEnumerable<IAccountData> modules, UserManager<User> users, SignInManager<User> signIn, IOutbox outbox, StaffAccess staff, Politicians.PoliticianHandlers politicians, TimeProvider clock)
{
    internal static readonly Error StaffPasskeyRequired = Error.Forbidden("account.staff-passkey-required");

    public async Task<Result<object>> ExportAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
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
        var passkeys = (await users.GetPasskeysAsync(user)).Select(p => new { name = p.Name, createdAt = p.CreatedAt }).ToList();
        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                           where ur.UserId == userId
                           select r.Name).ToListAsync(cancellationToken);
        var export = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exportedAt"] = clock.GetUtcNow(),
            ["account"] = new
            {
                email = user.Email,
                displayName = profile.DisplayName,
                verificationLevel = profile.VerificationLevel,
                createdAt = profile.CreatedAt,
                registeredAt = profile.RegisteredAt,
                // An organisation's code is not kept (design 8.14.14, stage B): only the level, as for any code.
                invitationCode = profile.InvitationCode,
                invitationLevel = profile.InvitationLevel,
                myInvitation = await MyInvitationAsync(userId, cancellationToken),
                roles,
                passkeys,
            },
            ["consents"] = consents,
            ["partySupport"] = party,

            // Only the feedback linked to the account (design 8.10); unlinked items lead to nobody.
            ["feedback"] = (await db.Feedback.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.CreatedOn).ToListAsync(cancellationToken))
                .Select(f => new { f.Kind, f.Text, f.PagePath, f.Browser, f.CreatedOn, f.Status, replies = JsonSerializer.Deserialize<JsonElement>(f.Replies) }),
            ["sessions"] = sessions,

            // Design 8.20.2: the browsers known to the account; never the cookie or its hash.
            ["knownBrowsers"] = await db.KnownDevices.AsNoTracking().Where(d => d.UserId == userId).OrderBy(d => d.FirstSeenAt)
                .Select(d => new { browser = d.Label, firstSeenAt = d.FirstSeenAt, lastSeenAt = d.LastSeenAt }).ToListAsync(cancellationToken),

            // Design 8.5: the account's links to an MP (as the MP or an office member) and the links and invitations it made;
            // never a token.
            ["politicianLinks"] = await db.PoliticianLinks.AsNoTracking().Where(l => l.UserId == userId).OrderBy(l => l.CreatedAt)
                .Select(l => new { l.SeimasPersonId, l.Part, l.VerifiedBy, l.CreatedAt, l.EndedAt, l.EndReason }).ToListAsync(cancellationToken),
            ["politicianTokens"] = await db.PoliticianTokens.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.CreatedAt)
                .Select(t => new { t.Kind, t.SeimasPersonId, t.CreatedAt, t.ExpiresAt, t.UsedAt }).ToListAsync(cancellationToken),
        };

        // Each module's part: appeals (sides, arguments, reports, flags, organisations …) and follows (design 8.11.2), each the person's own.
        foreach (var module in modules)
        {
            export[module.Section] = await module.ExportAsync(userId, cancellationToken);
        }

        return export;
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
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        // Design 6.9: an account with an active staff role is deleted only from a session signed in with its staff passkey,
        // so getting into the mailbox is not enough to remove a staff member.
        if (await staff.ForAsync(principal, cancellationToken) is { AccountHasActiveStaffRole: true, StaffPasskeySession: false })
        {
            return StaffPasskeyRequired;
        }

        var address = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            foreach (var module in modules)
            {
                await module.EraseAsync(userId, transaction.GetDbTransaction(), cancellationToken);
            }

            // The person's invitation codes stop working and keep nothing of them (design 6.2); their counts, levels and
            // daily totals stay as anonymous numbers. Codes the person registered with keep counting what they brought.
            var now = clock.GetUtcNow();
            await db.Invitations.Where(i => i.IssuedByUserId == userId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(i => i.IssuedByUserId, (Guid?)null)
                        .SetProperty(i => i.SwitchedOffAt, i => i.Kind == InvitationKinds.Personal ? (i.SwitchedOffAt ?? now) : i.SwitchedOffAt),
                    cancellationToken);

            // Emails to the person that were not sent go too (design 6.8: the outbox forgets).
            if (address is not null)
            {
                await outbox.ForgetAsync([address], transaction.GetDbTransaction(), cancellationToken);
            }

            // Design 8.5.2: deleting the account ends its link to an MP, audited and told to the official address; the link
            // and token rows go with the account.
            await politicians.OnAccountDeletedAsync(userId, address, transaction.GetDbTransaction(), cancellationToken);

            // The user row cascades to profile, consents, party support, sessions, known browsers, codes, passkeys, roles,
            // activations.
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await signIn.SignOutAsync();
        return Result.Ok();
    }
}