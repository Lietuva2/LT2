using System.Text;

namespace Lt2.Parliament.Import;

/// <summary>Public address parts: <c>Jonas Petraitis</c> → <c>jonas-petraitis</c> (design 3: <c>/p/jonas-petraitis</c>).</summary>
internal static class Slugs
{
    public static string Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
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

    /// <summary>The slug, or with a suffix when another row already holds it.</summary>
    public static string Unique(string text, string suffix, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var slug = Of(text);
        return taken(slug) ? $"{slug}-{suffix}" : slug;
    }
}
