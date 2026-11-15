using System.Text.RegularExpressions;

namespace Lt2.Api.Contracts.Me;

/// <summary>
/// The page a share link lands on after registering or signing in (design 8.11.5): the parameter <c>tikslas</c> of the
/// personal invitation link, after the <c>#</c> beside the code (design 6.8), or in the query of an old link. Accepted only as a path on this site under <c>/seimas/</c>: one leading slash, no scheme,
/// host, backslash, dot segment, query or fragment; anything else is ignored. It stays in the browser and is never sent to
/// the server, so nothing ties an invitation to a bill; the link is the same whichever side the sharer took.
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

    /// <summary>The personal invitation link (<see cref="InvitationLink.For"/>) that lands on <paramref name="path"/>.</summary>
    public static string ShareLink(string invitationLink, string path)
    {
        ArgumentNullException.ThrowIfNull(invitationLink);
        var accepted = Accept(path) ?? throw new ArgumentException("Not a page a share link may land on.", nameof(path));
        var separator = invitationLink.Contains('#', StringComparison.Ordinal) || invitationLink.Contains('?', StringComparison.Ordinal) ? "&" : "#";
        return invitationLink + separator + Parameter + "=" + Uri.EscapeDataString(accepted);
    }

    [GeneratedRegex(@"^/seimas/[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
