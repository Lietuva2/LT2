using System.Text;

namespace Lt2.Web.Client.Components;

/// <summary>
/// The initials badge of a member with a public profile (design 8.25.4, owner 2026-11-19: members have no uploaded picture):
/// the letters and the colour are worked out from the name and the profile's public id, nothing is stored.
/// </summary>
public static class MemberBadges
{
    /// <summary>The colour pairs in <c>lt2.css</c> (<c>--badge-0-bg</c> … <c>--badge-7-ink</c>), light and dark.</summary>
    public const int PaletteSize = 8;

    /// <summary>
    /// The first letter of the first and of the last word of the name, upper case (Lithuanian letters as they are: "Rūta
    /// Mockutė" is "RM", "ąžuolas" is "Ą"); one word gives one letter; a word without a letter is not counted; a name with
    /// no letter gives none (no badge).
    /// </summary>
    public static string? Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string? first = null, last = null;
        foreach (var word in name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (FirstLetter(word) is { } letter)
            {
                first ??= letter;
                last = letter;
            }
        }

        return first is null ? null : ReferenceEquals(first, last) ? first : first + last;
    }

    private static string? FirstLetter(string word)
    {
        foreach (var rune in word.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                return Rune.ToUpperInvariant(rune).ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Which of the <see cref="PaletteSize"/> colours a profile has: a fixed hash (FNV-1a over the id's UTF-8 bytes, never the
    /// runtime's per-process string hash) of its public id, so the same person has the same colour on every page and
    /// after every restart, and a changed name changes the letters only.
    /// </summary>
    public static int Colour(string publicId)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(publicId))
        {
            hash = unchecked((hash ^ b) * 16777619u);
        }

        return (int)(hash % PaletteSize);
    }
}
