namespace Lt2.Parliament.Import;

/// <summary>Public address parts: <c>Jonas Petraitis</c> → <c>jonas-petraitis</c> (design 3: <c>/p/jonas-petraitis</c>).</summary>
internal static class Slugs
{
    public static string Of(string text) => Lt2.SharedKernel.AddressSlugs.Of(text);

    /// <summary>The slug, or with a suffix when another row already holds it.</summary>
    public static string Unique(string text, string suffix, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var slug = Of(text);
        return taken(slug) ? $"{slug}-{suffix}" : slug;
    }
}
