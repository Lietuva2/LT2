using System.Text.RegularExpressions;

namespace Lt2.Web.Client;

/// <summary>A piece of a member's plain text: words, or an <c>https://</c> link with its address shown as written.</summary>
public sealed record UserTextPart(string Text, string? Link);

/// <summary>
/// Links in a member's plain text ("Apie mane", design 8.25.3, as comments do, 8.23.3): only <c>https://</c> addresses become
/// links, shown as written so the host is always visible; the page renders them with <c>rel="nofollow ugc noopener
/// noreferrer"</c> in a new tab, and nothing is fetched. Everything else stays text, encoded on output.
/// </summary>
public static partial class UserText
{
    public static IReadOnlyList<UserTextPart> Parts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<UserTextPart>();
        var at = 0;
        foreach (Match m in Address().Matches(text))
        {
            var address = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '"', '\'', '»', '“', '”');
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0)
            {
                continue;
            }

            if (m.Index > at)
            {
                parts.Add(new UserTextPart(text[at..m.Index], null));
            }

            parts.Add(new UserTextPart(address, uri.AbsoluteUri));
            at = m.Index + address.Length;
        }

        if (at < text.Length)
        {
            parts.Add(new UserTextPart(text[at..], null));
        }

        return parts;
    }

    [GeneratedRegex(@"https://[^\s<>""]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Address();
}
