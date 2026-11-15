using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.Identity.Infrastructure;

/// <summary>
/// Keeps authentication tickets in <c>identity.session</c>; the cookie carries only the session id. This is what makes
/// "my sessions" possible: a person can see where they are signed in and end any session at once.
/// </summary>
internal sealed class SessionTicketStore(
    IServiceScopeFactory scopes,
    IDataProtectionProvider dataProtection,
    TimeProvider clock) : ITicketStore
{
    /// <summary>The claim the cookie handler adds with the session key.</summary>
    public const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    /// <summary>Set at passkey sign-in: the credential id (base64url) of the passkey used, kept on the session row.</summary>
    public const string PasskeyClaim = "lt2:passkey";

    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Lt2.Identity.SessionTicket.v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, null!, CancellationToken.None);

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        StoreAsync(ticket, null!, cancellationToken);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var userId = ticket.Principal.UserId() ?? throw new InvalidOperationException("The ticket has no user id.");
        var now = clock.GetUtcNow();
        var passkey = ticket.Principal.FindFirstValue(PasskeyClaim) is { Length: > 0 } encoded
            ? Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(encoded)
            : null;
        var session = new Session
        {
            Id = Ids.New(clock),
            UserId = userId,
            Ticket = Protect(ticket),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = ticket.Properties.ExpiresUtc ?? now + DefaultLifetime,
            Method = ticket.Principal.FindFirstValue(AuthenticationMethods.ClaimType) ?? "",
            Device = DeviceDescription.FromUserAgent(httpContext?.Request.Headers.UserAgent.ToString()),
            PasskeyId = passkey,
            PasskeyConfirmedAt = passkey is null ? null : now,
        };

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session.Id.ToString();
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, null!, CancellationToken.None);

    public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        RenewAsync(key, ticket, null!, cancellationToken);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var bytes = Protect(ticket);
        var expires = ticket.Properties.ExpiresUtc ?? now + DefaultLifetime;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
        await db.Sessions.Where(s => s.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(x => x.Ticket, bytes).SetProperty(x => x.ExpiresAt, expires).SetProperty(x => x.LastSeenAt, now),
            cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, null!, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken) =>
        RetrieveAsync(key, null!, cancellationToken);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
        var session = await db.Sessions.AsNoTracking()
            .Where(s => s.Id == id && s.ExpiresAt > now)
            .Select(s => new { s.Ticket, s.LastSeenAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (now - session.LastSeenAt > LastSeenResolution)
        {
            await db.Sessions.Where(s => s.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), cancellationToken);
        }

        AuthenticationTicket? ticket;
        try
        {
            ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Ticket));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }

        // The ticket is stored before its key exists, so it lacks the session-id claim the cookie handler puts only in
        // the cookie. Add it back so the signed-in person's principal says which session is the current one.
        if (ticket?.Principal.Identity is ClaimsIdentity identity && !identity.HasClaim(c => c.Type == SessionIdClaim))
        {
            identity.AddClaim(new Claim(SessionIdClaim, key));
        }

        return ticket;
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, null!, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) => RemoveAsync(key, null!, cancellationToken);

    public async Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(key, out var id))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDb>();
        await db.Sessions.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    private byte[] Protect(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));
}

/// <summary>A coarse "browser, system" label from a user agent, enough to recognise a session and no more.</summary>
internal static class DeviceDescription
{
    public static string? FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var browser = userAgent switch
        {
            _ when userAgent.Contains("Edg/", StringComparison.Ordinal) => "Edge",
            _ when userAgent.Contains("OPR/", StringComparison.Ordinal) => "Opera",
            _ when userAgent.Contains("Firefox/", StringComparison.Ordinal) => "Firefox",
            _ when userAgent.Contains("Chrome/", StringComparison.Ordinal) => "Chrome",
            _ when userAgent.Contains("Safari/", StringComparison.Ordinal) => "Safari",
            _ => null,
        };
        var system = userAgent switch
        {
            _ when userAgent.Contains("Android", StringComparison.Ordinal) => "Android",
            _ when userAgent.Contains("iPhone", StringComparison.Ordinal) || userAgent.Contains("iPad", StringComparison.Ordinal) => "iOS",
            _ when userAgent.Contains("Windows", StringComparison.Ordinal) => "Windows",
            _ when userAgent.Contains("Mac OS X", StringComparison.Ordinal) => "macOS",
            _ when userAgent.Contains("Linux", StringComparison.Ordinal) => "Linux",
            _ => null,
        };

        return (browser, system) switch
        {
            (null, null) => null,
            (not null, null) => browser,
            (null, not null) => system,
            _ => $"{browser}, {system}",
        };
    }
}
