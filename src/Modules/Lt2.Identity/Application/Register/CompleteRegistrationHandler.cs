using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Register;

/// <summary>
/// Completes a pending registration once the address is proven (design 6.2). In one transaction: the display name,
/// the declaration of being 18 or older and the accepted terms of use (design 8.22.2) are stored, the invitation use is counted, and the account becomes a member.
/// If the invitation held has run out by now, the person is told so and may give another code.
/// </summary>
internal sealed class CompleteRegistrationHandler(
    IdentityDb db,
    UserManager<User> users,
    SignInManager<User> signIn,
    ISettings settings,
    IMpDirectory mps,
    SignIn.KnownBrowsers browsers,
    TimeProvider clock)
{
    public async Task<Result<Unit>> HandleAsync(ClaimsPrincipal principal, RegistrationCompletion completion, CancellationToken cancellationToken)
    {
        if (!completion.ConfirmsAdult)
        {
            return Error.Validation("adult.required", "confirmsAdult");
        }

        // Design 8.22.2: the account rests on the terms of use; checked here, not only by the form.
        if (!completion.AcceptsTerms)
        {
            return Error.Validation("terms.not-accepted", "acceptsTerms");
        }

        // Checked here as well as by the form's validator (design 6.8): callers inside the server skip the validator.
        if (ProfileRules.CheckDisplayName(completion.DisplayName) is { } nameError)
        {
            return Error.Validation(nameError, "displayName");
        }

        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        // The profile row is locked for the transaction: of two completions at once, the second sees it completed.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var profile = await db.Profiles
            .FromSqlInterpolated($"SELECT * FROM identity.profile WHERE user_id = {userId} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
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

        Invitation? invitation = null;
        OfficeInvitationGate.Pass? office = null;
        if (code is null && !string.IsNullOrWhiteSpace(completion.OfficeInvitation))
        {
            // Design 8.5.3 (owner, 2026-11-17): an MP office member's invitation in place of an invitation code, checked again
            // now; it is used up only when it is accepted on the office page.
            // Where no invitation is needed, one that no longer works changes nothing.
            office = await OfficeInvitationGate.CheckAsync(db, completion.OfficeInvitation, now, cancellationToken);
            if (office is null
                && await settings.GetAsync(SettingKeys.RegistrationRequiresInvitation, cancellationToken)
                && !await OfficialAddressGate.LetsThroughAsync(settings, mps, (await users.FindByIdAsync(userId.ToString()))?.Email, cancellationToken))
            {
                return Error.Validation("invitation.invalid", "invitationCode");
            }
        }
        else if (code is not null)
        {
            // One answer for every code that does not work (design 6.2): unknown, switched off, replaced, expired, used up.
            var lookup = InvitationRules.Lookup(code);
            invitation = await db.Invitations.AsNoTracking().FirstOrDefaultAsync(i => i.Lookup == lookup, cancellationToken);
            var limit = invitation is null ? 0 : await Invitations.InvitationHandlers.LimitAsync(settings, invitation.Kind, cancellationToken);
            if (invitation is null
                || !InvitationRules.Usable(invitation, limit, now)

                // Counted atomically on the code and in the day's total: of two completions racing for the last use, one gets it.
                || !await Invitations.InvitationHandlers.CountUseAsync(db, invitation, limit, now, cancellationToken))
            {
                return Error.Validation("invitation.invalid", "invitationCode");
            }

            code = invitation.Code;
        }
        else if (await settings.GetAsync(SettingKeys.RegistrationRequiresInvitation, cancellationToken)
            && !await OfficialAddressGate.LetsThroughAsync(
                settings, mps, (await users.FindByIdAsync(userId.ToString()))?.Email, cancellationToken))
        {
            return Error.Validation("invitation.required", "invitationCode");
        }

        // An organisation's code is kept only as its level (design 8.14.14, stage B): nothing on the account names the code
        // or the organisation. Its counter and daily total rose above, as for any code.
        // An office invitation leaves only its level too (owner, 2026-11-17).
        (profile.InvitationId, profile.InvitationCode, profile.InvitationLevel) = office is not null
            ? (null, null, office.Level)
            : InvitationRules.KeptOnProfile(invitation?.Kind, invitation?.Id, code, invitation?.Level);
        profile.DisplayName = ProfileRules.DisplayName(completion.DisplayName);
        profile.RegisteredAt = now;
        db.Consents.Add(new Consent
        {
            Id = Ids.New(clock),
            UserId = userId,
            Kind = ConsentKinds.Adult,
            TextVersion = ConsentKinds.AdultTextVersion,
            GivenAt = now,
        });
        db.Consents.Add(Consents.TermsHandlers.Given(userId, clock));
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = SeedRoles.MemberId });
        await db.SaveChangesAsync(cancellationToken);

        // The new member's own invitation code, one level below them (design 6.2), under the member's code lock.
        await IdentityLocks.PersonalCodeAsync(db, userId, cancellationToken);
        await Invitations.InvitationHandlers.CreatePersonalAsync(
            db, clock, userId, InvitationRules.PersonalCodeLevel(InvitationRules.MemberLevel(profile.InvitationLevel)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Re-issue the session so it carries the member role.
        if (await users.FindByIdAsync(userId.ToString()) is { } user)
        {
            await signIn.RefreshSignInAsync(user);
        }

        // Design 8.20.2: the browser that finished the registration is known to the account from now on.
        await browsers.RememberAsync(userId, cancellationToken);
        return Result.Ok();
    }
}
