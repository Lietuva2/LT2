using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Register;

/// <summary>
/// Completes a pending registration once the address is proven (design 6.2). In one transaction: the display name and
/// the declaration of being 18 or older are stored, the invitation use is counted, and the account becomes a member.
/// If the invitation held has run out by now, the person is told so and may give another code.
/// </summary>
internal sealed class CompleteRegistrationHandler(
    IdentityDb db,
    UserManager<User> users,
    SignInManager<User> signIn,
    ISettings settings,
    TimeProvider clock)
{
    public async Task<Result<Unit>> HandleAsync(ClaimsPrincipal principal, RegistrationCompletion completion, CancellationToken cancellationToken)
    {
        if (!completion.ConfirmsAdult)
        {
            return Error.Validation("adult.required", "confirmsAdult");
        }

        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Error.Unauthorized("auth.required");
        }

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return Error.Unauthorized("auth.required");
        }

        if (!profile.IsPending)
        {
            return Error.Conflict("registration.complete");
        }

        var now = clock.GetUtcNow();
        var code = string.IsNullOrWhiteSpace(completion.InvitationCode)
            ? profile.InvitationCode
            : completion.InvitationCode;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Invitation? invitation = null;
        if (code is not null)
        {
            // One answer for every code that does not work (design 6.2): unknown, switched off, replaced, expired, used up.
            var lookup = InvitationRules.Lookup(code);
            invitation = await db.Invitations.AsNoTracking().FirstOrDefaultAsync(i => i.Lookup == lookup, cancellationToken);
            var limit = invitation is null ? 0 : await Invitations.InvitationHandlers.LimitAsync(settings, invitation.Kind, cancellationToken);
            if (invitation is null
                || !InvitationRules.Usable(invitation.Kind, invitation.UsesLeft, invitation.UsedCount, invitation.ExpiresAt, invitation.SwitchedOffAt, invitation.ReplacedAt, limit, now, invitation.OrganisationOff)

                // Counted atomically on the code and in the day's total: of two completions racing for the last use, one gets it.
                || !await Invitations.InvitationHandlers.CountUseAsync(db, invitation, limit, now, cancellationToken))
            {
                return Error.Validation("invitation.invalid", "invitationCode");
            }

            code = invitation.Code;
        }
        else if (await settings.GetAsync(SettingKeys.RegistrationRequiresInvitation, cancellationToken))
        {
            return Error.Validation("invitation.required", "invitationCode");
        }

        // An organisation's code is kept only as its level (design 8.14.14, stage B): nothing on the account names the code
        // or the organisation. Its counter and daily total rose above, as for any code.
        (profile.InvitationId, profile.InvitationCode, profile.InvitationLevel) =
            InvitationRules.KeptOnProfile(invitation?.Kind, invitation?.Id, code, invitation?.Level);
        profile.DisplayName = string.IsNullOrWhiteSpace(completion.DisplayName) ? null : completion.DisplayName.Trim();
        profile.RegisteredAt = now;
        db.Consents.Add(new Consent
        {
            Id = Ids.New(clock),
            UserId = userId,
            Kind = ConsentKinds.Adult,
            TextVersion = ConsentKinds.AdultTextVersion,
            GivenAt = now,
        });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = SeedRoles.MemberId });
        await db.SaveChangesAsync(cancellationToken);

        // The new member's own invitation code, one level below them (design 6.2).
        await Invitations.InvitationHandlers.CreatePersonalAsync(
            db, clock, userId, InvitationRules.PersonalCodeLevel(InvitationRules.MemberLevel(invitation?.Level)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Re-issue the session so it carries the member role.
        if (await users.FindByIdAsync(userId.ToString()) is { } user)
        {
            await signIn.RefreshSignInAsync(user);
        }

        return Result.Ok();
    }
}
