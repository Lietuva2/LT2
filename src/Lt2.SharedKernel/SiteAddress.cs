namespace Lt2.SharedKernel;

/// <summary>
/// The site's own public address (design 5.6): deployment configuration <c>Site:Address</c>, the address Caddy serves
/// (<c>LT2_SITE_ADDRESS</c>). Not a runtime setting, so no administrator can point emails at another site. Empty: emails
/// carry the site's paths only.
/// </summary>
public sealed class SiteOptions
{
    public const string Section = "Site";

    /// <summary>For example <c>https://lietuva2.lt</c>: https, a host, an optional port, nothing after it.</summary>
    public string? Address { get; set; }
}

/// <summary>Full addresses of the site's pages for emails, which are read outside the site.</summary>
public static class SiteLinks
{
    /// <summary>Empty, or an https address of a host and nothing more: no user, path, query or fragment.</summary>
    public static bool IsValid(string? address) => string.IsNullOrWhiteSpace(address) || Root(address) is not null;

    /// <summary>
    /// <paramref name="path"/> on the site at <paramref name="address"/>; the path as it is when no valid address is
    /// configured or it is not a path on this site.
    /// </summary>
    public static string Absolute(string? address, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal))
        {
            return path;
        }

        return Root(address) is { } root ? root + path : path;
    }

    /// <summary>Scheme, host and a port other than 443, without a trailing slash; null when the address is not valid.</summary>
    private static string? Root(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host.Length == 0
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
