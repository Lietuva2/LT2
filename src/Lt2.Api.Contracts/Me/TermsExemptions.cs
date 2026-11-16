using Lt2.Api.Contracts.Digests;

namespace Lt2.Api.Contracts.Me;

/// <summary>
/// Design 8.22.2: the writes a member who has not accepted the terms of use in force may still make. Every other write of the API
/// from a signed-in session is refused with <c>terms.required</c> by the web host's <c>TermsGate</c>, so a route added later is
/// covered without being listed anywhere. Reads (GET, HEAD, OPTIONS) are never refused: the export included.
/// </summary>
public static class TermsExemptions
{
    /// <summary>
    /// Accepting, signing out and deleting the account (design 8.22.2), withdrawing what the member consented to (switching the
    /// public profile off, the positions and party support consents; owner, 2026-11-17), and the MP's mail links (design 8.19.4), which act for
    /// whoever holds the link's token, not for the account signed in, and are used by people without an account. Method and
    /// route template exactly as mapped.
    /// </summary>
    public static readonly IReadOnlyList<(string Method, string Route)> Routes =
    [
        ("PUT", ApiRoutes.Me.Terms),
        ("POST", ApiRoutes.Auth.SignOut),
        ("DELETE", ApiRoutes.Me.Root),

        // Withdrawals (owner, 2026-11-17): switching the public profile off, the positions consent, the party support consent.
        ("DELETE", ProfileRoutes.MinePublic),
        ("DELETE", ApiRoutes.Me.PositionConsent),
        ("DELETE", ApiRoutes.Me.PartySupport),
        ("PUT", MpMailRoutes.Token),
        ("POST", MpMailRoutes.Renew),
        ("PUT", MpMailRoutes.Extra),
        ("DELETE", MpMailRoutes.Extra),
        ("POST", MpMailRoutes.ConfirmExtra),
        ("POST", MpMailRoutes.StopCopies),
        ("POST", MpMailRoutes.Stop),
    ];

    /// <summary>A request that changes something: anything but GET, HEAD and OPTIONS.</summary>
    public static bool IsWrite(string method) =>
        !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase);

    /// <summary>The route template <paramref name="route"/> with <paramref name="method"/> is one of <see cref="Routes"/>.</summary>
    public static bool IsExempt(string method, string? route) =>
        route is not null && Routes.Any(e =>
            string.Equals(e.Method, method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Route.TrimStart('/'), route.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
}
