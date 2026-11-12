using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lt2.Identity.Application.Passkeys;

/// <summary>
/// Passkey registration (for a signed-in person) and passkey sign-in, on ASP.NET Core Identity's WebAuthn support.
/// The challenge state lives in a short-lived encrypted cookie set by <see cref="SignInManager{TUser}"/>.
/// </summary>
internal sealed class PasskeyHandlers(
    IdentityDb db,
    UserManager<User> users,
    SignInManager<User> signIn,
    ILogger<PasskeyHandlers> logger)
{
    internal static readonly Error InvalidPasskey = Error.Validation("passkey.invalid", "credential");
    private const string DefaultName = "Prieigos raktas";

    public async Task<Result<string>> CreationOptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Error.Unauthorized("auth.required");
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

    public async Task<Result<Unit>> AddAsync(ClaimsPrincipal principal, string credentialJson, string? name, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return Error.Unauthorized("auth.required");
        }

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(credentialJson);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation(ex, "Passkey attestation without a pending challenge");
            return InvalidPasskey;
        }

        if (!attestation.Succeeded || attestation.UserEntity.Id != user.Id.ToString())
        {
            logger.LogInformation("Passkey attestation failed: {Reason}", attestation.Failure?.Message);
            return InvalidPasskey;
        }

        attestation.Passkey.Name = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        var saved = await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        return saved.Succeeded ? Result.Ok() : InvalidPasskey;
    }

    public async Task<Result<string>> RequestOptionsAsync() => await signIn.MakePasskeyRequestOptionsAsync(user: null);

    public async Task<Result<Unit>> SignInAsync(string credentialJson)
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

        await signIn.SignInWithClaimsAsync(
            assertion.User,
            new AuthenticationProperties { IsPersistent = true },
            [new Claim(AuthenticationMethods.ClaimType, AuthenticationMethods.Passkey)]);
        return Result.Ok();
    }
}
