using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Lt2.Identity.Infrastructure;

/// <summary>
/// The claims this site puts on a session's principal beside those ASP.NET Core Identity makes from the account (the id, the
/// address, the roles, the security stamp): how the session was signed in, the passkey used, and the session's key. Identity
/// rebuilds the principal from the account when it checks the security stamp (every 30 minutes, <see cref="SecurityStampValidator"/>);
/// <see cref="KeepAsync"/> carries these over, so a passkey session stays one and the request still knows its session row.
/// </summary>
internal static class SessionClaims
{
    public static readonly IReadOnlyList<string> Types =
        [AuthenticationMethods.ClaimType, SessionTicketStore.PasskeyClaim, SessionTicketStore.SessionIdClaim];

    /// <summary>
    /// <see cref="SecurityStampValidatorOptions.OnRefreshingPrincipal"/>: runs only after the stamp matched, so a changed stamp
    /// still rejects the principal and signs the session out; this only keeps what the account alone cannot tell.
    /// </summary>
    public static Task KeepAsync(SecurityStampRefreshingPrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CurrentPrincipal is not { } current || context.NewPrincipal?.Identities.FirstOrDefault() is not { } identity)
        {
            return Task.CompletedTask;
        }

        foreach (var claim in current.Claims.Where(c => Types.Contains(c.Type)))
        {
            if (!identity.HasClaim(c => c.Type == claim.Type))
            {
                identity.AddClaim(new Claim(claim.Type, claim.Value, claim.ValueType, claim.Issuer));
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Every sign-in (an emailed code, a passkey, the take-over of an MP's profile, a code session becoming a passkey or staff
/// session) starts a new session under a new key (<see cref="SessionRotation"/>): the browser's current session, of this
/// account or of another, ends as by signing out (its row is deleted) and a new row is stored with the way this sign-in was
/// made. So a session's row never changes account or way of signing in after it was stored.
/// </summary>
internal static class SessionSignIn
{
    /// <summary>
    /// Signs <paramref name="user"/> in by <paramref name="method"/> (with <paramref name="passkey"/> for a passkey) and says
    /// whether the browser was signed in to another account before, which then ended.
    /// </summary>
    public static async Task<bool> SignInAsync(SignInManager<User> signIn, User user, string method, byte[]? passkey = null)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(user);
        var before = signIn.Context.User.UserId();
        var principal = await signIn.CreateUserPrincipalAsync(user);
        var identity = principal.Identities.First();
        identity.AddClaim(new Claim(AuthenticationMethods.ClaimType, method));
        if (passkey is not null)
        {
            identity.AddClaim(new Claim(SessionTicketStore.PasskeyClaim, WebEncoders.Base64UrlEncode(passkey)));
        }

        var properties = new AuthenticationProperties { IsPersistent = true };
        if (signIn.Context.User.Identity?.IsAuthenticated == true || signIn.Context.Request.Cookies.ContainsKey(IdentityModule.SessionCookieName))
        {
            // A browser with a session (or a cookie that named one): that session ends and the new one gets a new key.
            await SessionRotation.SignInWithNewKeyAsync(signIn.Context, principal, properties);
        }
        else
        {
            // A browser with no session: a new one is stored, nothing to end.
            await signIn.Context.SignInAsync(IdentityConstants.ApplicationScheme, principal, properties);
            signIn.Context.User = principal;
        }

        return before is { } previous && previous != user.Id;
    }
}
