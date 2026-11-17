using System.Security.Claims;
using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lt2.Identity.Application.Passkeys;

/// <summary>
/// Passkey registration (for a signed-in member) and passkey sign-in, on ASP.NET Core Identity's WebAuthn support.
/// The challenge state lives in a short-lived encrypted cookie set by <see cref="SignInManager{TUser}"/>.
/// On an account with an active staff role, adding a passkey needs a staff-passkey session with a fresh confirmation
/// (design 6.2); every account is told by email when a passkey is added.
/// </summary>
internal sealed class PasskeyHandlers(
    IdentityDb db,
    UserManager<User> users,
    SignInManager<User> signIn,
    StaffAccess access,
    Politicians.PoliticianAccess politicians,
    AccountEvents events,
    SignIn.KnownBrowsers browsers,
    ILogger<PasskeyHandlers> logger)
{
    internal static readonly Error InvalidPasskey = Error.Validation("passkey.invalid", "credential");

    /// <summary>
    /// Adding a passkey failed. Said separately from <see cref="InvalidPasskey"/>: the browser or password manager may
    /// already have saved the key, so the person is told it was not added here and may try again.
    /// </summary>
    internal static readonly Error NotAdded = Error.Validation("passkey.not-added", "credential");
    internal static readonly Error ConfirmationRequired = Error.Forbidden("passkey.confirmation-required");
    private const string DefaultName = "Prieigos raktas";

    /// <summary>The staff rule for passkey changes: nothing extra unless the account has an active staff role.</summary>
    private async Task<Error?> CheckStaffRuleAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var state = await access.ForAsync(principal, cancellationToken);
        if (state is null || !state.AccountHasActiveStaffRole)
        {
            return null;
        }

        if (!state.StaffPasskeySession)
        {
            return StaffHandlers.StaffPasskeyRequired;
        }

        return state.RecentlyConfirmed ? null : ConfirmationRequired;
    }

    public async Task<Result<string>> CreationOptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Error.Unauthorized("auth.required");
        }

        if (await CheckStaffRuleAsync(principal, cancellationToken) is { } refused)
        {
            return refused;
        }

        var displayName = await db.Profiles.AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .Select(p => p.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        var entity = new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.Email!,
            DisplayName = displayName ?? user.Email!,
        };
        return await signIn.MakePasskeyCreationOptionsAsync(entity);
    }

    public async Task<Result<Unit>> AddAsync(ClaimsPrincipal principal, string credentialJson, string? name, string? ipHash, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Error.Unauthorized("auth.required");
        }

        if (await CheckStaffRuleAsync(principal, cancellationToken) is { } refused)
        {
            return refused;
        }

        // Design 8.5.2, 8.5.3: on a linked account (the MP's or an office member's) a code alone never changes the passkeys;
        // the take-over link is the other way, with its own request. The creation options above stay open to a code session:
        // they change nothing, and the take-over page needs them.
        var linked = await politicians.ForAsync(principal, cancellationToken);
        if (linked is not null && !PoliticianRules.MayChangePasskeys(linked.Link is not null, linked.PasskeySession))
        {
            return Politicians.PoliticianAccess.PasskeyRequired;
        }

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(credentialJson);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation(ex, "Passkey attestation without a pending challenge");
            return NotAdded;
        }

        if (!attestation.Succeeded || attestation.UserEntity.Id != user.Id.ToString())
        {
            logger.LogInformation("Passkey attestation failed: {Reason}", attestation.Failure?.Message);
            return NotAdded;
        }

        attestation.Passkey.Name = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var saved = await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        if (!saved.Succeeded)
        {
            return NotAdded;
        }

        await events.RecordAsync(
            transaction.GetDbTransaction(),
            "passkey.added",
            user.Id,
            new Actor(user.Id, Roles.Member, ipHash),
            new { credential = WebEncoders.Base64UrlEncode(attestation.Passkey.CredentialId)[..12] },
            new AccountNoticeEmail(user.Email!, AccountNotices.PasskeyAdded),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<string>> RequestOptionsAsync() => await signIn.MakePasskeyRequestOptionsAsync(user: null);

    public async Task<Result<SignInOutcome>> SignInAsync(string credentialJson)
    {
        PasskeyAssertionResult<User> assertion;
        try
        {
            assertion = await signIn.PerformPasskeyAssertionAsync(credentialJson);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation(ex, "Passkey assertion without a pending challenge");
            return InvalidPasskey;
        }

        if (!assertion.Succeeded || !await signIn.CanSignInAsync(assertion.User))
        {
            logger.LogInformation("Passkey assertion failed: {Reason}", assertion.Failure?.Message);
            return InvalidPasskey;
        }

        // Store the new signature counter and backup state.
        var updated = await users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey);
        if (!updated.Succeeded)
        {
            return InvalidPasskey;
        }

        // A new session under a new key, whichever account the browser held before (design 6.2): never a silent switch.
        var changed = await SessionSignIn.SignInAsync(signIn, assertion.User, AuthenticationMethods.Passkey, assertion.Passkey.CredentialId);

        // Design 8.20.2: the browser becomes known to the account, or is renewed.
        await browsers.RememberAsync(assertion.User.Id, CancellationToken.None);
        return new SignInOutcome(changed);
    }
}
