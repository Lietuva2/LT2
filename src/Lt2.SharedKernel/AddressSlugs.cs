// One source file, compiled twice: into Lt2.SharedKernel for the modules (MP addresses, the links in follow emails) and, linked
// by Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share
// (ProfileRoutes.Page).
using System.Text;

#if LT2_CONTRACTS
namespace Lt2.Api.Contracts;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>
/// A readable part of a public address (design 3, 8.25.2): <c>Jonas Petraitis</c> → <c>jonas-petraitis</c>. Lithuanian letters
/// lose their marks, everything is lower case, any run of other characters becomes one hyphen, and no hyphen stands at either
/// end. Empty when nothing of the text is a letter or a digit. MPs' <c>/p/{slug}</c> and members' <c>/nariai/{slug}-{id}</c>
/// are made by this one function.
/// </summary>
public static class AddressSlugs
{
    public static string Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var slug = new StringBuilder(text.Length);
        foreach (var c in text.Trim().ToLowerInvariant())
        {
            var letter = c switch
            {
                'ą' => 'a', 'č' => 'c', 'ę' => 'e', 'ė' => 'e', 'į' => 'i', 'š' => 's', 'ų' => 'u', 'ū' => 'u', 'ž' => 'z',
                _ => c,
            };
            if (letter is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(letter);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().Trim('-');
    }
}

/// <summary>
/// A member's public page (design 8.25.2, owner 2026-11-16): <c>/nariai/{name-slug}-{id}</c>, for example
/// <c>/nariai/jonas-petraitis-7k3qm2x9ab</c>. The id (the profile's random public id, letters and digits, no hyphen) decides
/// which profile it is; the name part is made from the current display name by <see cref="AddressSlugs.Of"/> and only helps
/// a reader and a search engine. A name with nothing to make a slug of gives the id alone.
/// </summary>
public static class MemberAddress
{
    public const string Prefix = "/nariai/";

    /// <summary>The page's last segment: the name part and the id, or the id alone.</summary>
    public static string Segment(string profileId, string? displayName)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        var slug = AddressSlugs.Of(displayName);
        return slug.Length == 0 ? profileId : slug + "-" + profileId;
    }

    /// <summary>The page's address for the profile and its current display name.</summary>
    public static string Page(string profileId, string? displayName) => Prefix + Segment(profileId, displayName);

    /// <summary>The id in a segment: what follows its last hyphen (the id itself has none), or the whole segment.</summary>
    public static string IdOf(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var at = segment.LastIndexOf('-');
        return at < 0 ? segment : segment[(at + 1)..];
    }
}
