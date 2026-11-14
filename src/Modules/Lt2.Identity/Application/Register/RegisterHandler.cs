using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Identity.Application.Register;

/// <summary>
/// Registration by email and invitation code (design 6.2). It creates a <em>pending</em> account: the normalised address
/// and the invitation code entered, no name, no declaration, no role. The invitation use is counted only when the
/// registration is completed, so addresses nobody confirms cannot use invitations up.
/// The response is the same in every case, and a sign-in code is emailed in every case:
/// <list type="bullet">
/// <item>a new address gets a pending account;</item>
/// <item>an address that is still pending keeps its account, and the invitation code it holds is replaced;</item>
/// <item>an address with a completed account is not changed; what was typed is discarded.</item>
/// </list>
/// </summary>
internal sealed class RegisterHandler(
    IdentityDb db,
    UserManager<User> users,
    SignInCodeIssuer codes,
    ISettings settings,
    TimeProvider clock)
{
    public async Task<Result<Unit>> HandleAsync(Registration registration, CancellationToken cancellationToken)
    {
        var normalized = EmailAddress.Normalize(registration.Email);
        if (normalized is null)
        {
            return Error.Validation("email.invalid", "email");
        }

        if (DisposableDomains.Contains(normalized))
        {
            return Error.Validation("email.disposable", "email");
        }

        // The invitation is checked before the address is looked up, so the answer never depends on the address.
        var now = clock.GetUtcNow();
        string? invitationCode = null;
        if (!string.IsNullOrWhiteSpace(registration.InvitationCode))
        {
            // Case, dashes and spaces do not matter (design 6.2). One answer for every code that does not work.
            var lookup = InvitationRules.Lookup(registration.InvitationCode);
            var invitation = await db.Invitations.AsNoTracking().FirstOrDefaultAsync(i => i.Lookup == lookup, cancellationToken);
            if (invitation is null
                || !InvitationRules.Usable(
                    invitation.Kind, invitation.UsesLeft, invitation.UsedCount, invitation.ExpiresAt, invitation.SwitchedOffAt, invitation.ReplacedAt,
                    await Invitations.InvitationHandlers.LimitAsync(settings, invitation.Kind, cancellationToken), now, invitation.OrganisationOff))
            {
                return Error.Validation("invitation.invalid", "invitationCode");
            }

            // An organisation's code is not written to the pending account (design 8.14.14, stage B): the browser sends it
            // again when the registration is completed, so no row ever ties this address to the organisation.
            invitationCode = InvitationRules.HeldWhilePending(invitation.Kind, invitation.Code);
        }
        else if (await settings.GetAsync(SettingKeys.RegistrationRequiresInvitation, cancellationToken))
        {
            return Error.Validation("invitation.required", "invitationCode");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Profiles.FirstOrDefaultAsync(p => p.NormalizedEmail == normalized, cancellationToken);
        if (existing is not null)
        {
            if (existing.IsPending)
            {
                existing.InvitationCode = invitationCode;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (await users.FindByIdAsync(existing.UserId.ToString()) is { } existingUser)
            {
                await codes.IssueAsync(existingUser, transaction, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Result.Ok();
        }

        var user = new User { Id = Ids.New(clock), Email = registration.Email.Trim(), EmailConfirmed = false };
        user.UserName = user.Id.ToString("N");
        try
        {
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Creating the account failed: " + string.Join(", ", created.Errors.Select(e => e.Code)));
            }

            db.Profiles.Add(new Profile
            {
                UserId = user.Id,
                NormalizedEmail = normalized,
                VerificationLevel = VerificationLevels.None,
                InvitationCode = invitationCode,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);

            await codes.IssueAsync(user, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent registration of the same address won; answer as for an existing account.
            await transaction.RollbackAsync(cancellationToken);
            return Result.Ok();
        }
    }
}
