using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.Identity.Infrastructure;

/// <summary>
/// Signs the browser in again under a new session key (design 6.2: a code session on a staff account becoming a staff session).
/// The cookie handler of a request keeps the key it read from the cookie, also after signing out, and a sign-in through it
/// renews that row (<see cref="SessionTicketStore.RenewAsync(string, AuthenticationTicket, HttpContext, CancellationToken)"/>)
/// instead of making a new one. So the current session is ended through that handler (its row is deleted, its cookie
/// cleared, and it no longer renews anything when the response starts), and the new session is signed in through a handler
/// of its own, made the way ASP.NET Core makes every handler: the old cookie it reads leads to no row, so it stores a new
/// session and writes its key into the cookie. The browser keeps the last cookie it is given, the new one.
/// </summary>
internal static class SessionRotation
{
    public static async Task SignInWithNewKeyAsync(HttpContext context, ClaimsPrincipal principal, AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(context);
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);

        var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemes.GetSchemeAsync(IdentityConstants.ApplicationScheme)
            ?? throw new InvalidOperationException("The application cookie scheme is not registered.");
        var handler = (IAuthenticationSignInHandler)ActivatorUtilities.CreateInstance(context.RequestServices, scheme.HandlerType);
        await handler.InitializeAsync(scheme, context);
        await handler.SignInAsync(principal, properties);
        context.User = principal;
    }
}
