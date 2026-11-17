using System.Text.RegularExpressions;

namespace Lt2.Api.Contracts.Me;

/// <summary>
/// The page a person comes back to after signing in or registering (design 6.9, 8.11.5): the parameter <c>tikslas</c> after
/// the <c>#</c> of a sign-in link (design 6.8). Accepted only as a path on this site from a short list: one leading slash, no
/// scheme, host, backslash, dot segment, query or fragment; anything else is ignored. It stays in the browser and is never sent
/// to the server. The share line's link (<see cref="ShareLink"/>) is the bill's own address with the invitation code after the
/// <c>#</c>: nothing ties an invitation to a bill, and the link is the same whichever side the sharer took.
/// </summary>
public static partial class LandingPath
{
    /// <summary>The parameter's name.</summary>
    public const string Parameter = "tikslas";

    private const int MaxLength = 200;

    /// <summary>The path when it is acceptable, otherwise null.</summary>
    public static string? Accept(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxLength || path.Contains("//", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        return Shape().IsMatch(path) ? path : null;
    }

    /// <summary>
    /// The share line's link (design 8.11.5, owner 2026-11-17): the page <paramref name="path"/> itself, so a link preview shows
    /// the page, with the invitation <paramref name="code"/> after the <c>#</c> (<c>/seimas/XVP-0412#kodas=…</c>), which the
    /// browser never sends; without a code (off or used up) the plain address.
    /// </summary>
    public static string ShareLink(string? code, string path)
    {
        var accepted = Accept(path) ?? throw new ArgumentException("Not a page a share link may lead to.", nameof(path));
        return string.IsNullOrWhiteSpace(code)
            ? accepted
            : accepted + "#" + InvitationLink.CodeParameter + "=" + Uri.EscapeDataString(code.Trim());
    }

    /// <summary>
    /// The sign-in page that brings the person back to <paramref name="path"/> after signing in or registering (design 6.9:
    /// the sign-in links on a bill's page), with <c>tikslas</c> after the <c>#</c> (design 6.8). Only a path that
    /// <see cref="Accept"/> takes is carried; for any other the plain sign-in page.
    /// </summary>
    public static string SignInLink(string? path) =>
        Accept(path) is { } accepted ? InvitationLink.Page + "#" + Parameter + "=" + Uri.EscapeDataString(accepted) : InvitationLink.Page;

    /// <summary>The path of the page the browser is on (no query, no fragment), from a navigation address and its base.</summary>
    public static string PathOf(string address, string baseAddress)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(baseAddress);
        var relative = address.StartsWith(baseAddress, StringComparison.Ordinal) ? address[baseAddress.Length..] : address;
        return "/" + relative.Split('?', '#')[0].TrimStart('/');
    }

    /// <summary>
    /// A bill's page and the pages under <c>/seimas/</c>; the account screens (design 8.21: a visitor sent to sign in from one
    /// comes back to it); a member's public page (design 8.25.2: its "Sekti" for a visitor); the statistics page (design 8.26.4:
    /// a member's own comparison, owner 2026-11-17 at <c>/statistika</c>).
    /// </summary>
    [GeneratedRegex(@"^(/seimas/[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*|/paskyra(/[a-z]+)?|/nariai/([a-z0-9]+-)*[a-z0-9]{10}|/statistika)$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
