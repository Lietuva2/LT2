using System.Globalization;
using System.Text;
using Lt2.SharedKernel;

namespace Lt2.Web.Endpoints;

/// <summary>
/// Design 6.7: <c>/.well-known/security.txt</c> (RFC 9116), served only when the deployment names an address for security
/// reports in <c>Site:SecurityContact</c> (<c>LT2_SECURITY_CONTACT</c>, a <c>mailto:</c> or https address); without one the
/// answer is 404, so no file points to an address nobody reads. <c>Expires</c> is made on each request (now plus 180 days,
/// UTC), so the file never goes stale while the site runs.
/// </summary>
internal static class SecurityTxt
{
    public const string Path = "/.well-known/security.txt";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet(Path, (HttpContext context, IConfiguration configuration, TimeProvider time) =>
            {
                var contact = configuration["Site:SecurityContact"];
                if (string.IsNullOrWhiteSpace(contact))
                {
                    return Results.NotFound();
                }

                var site = configuration["Site:Address"];
                var canonical = SiteLinks.IsValid(site) && !string.IsNullOrWhiteSpace(site)
                    ? SiteLinks.Absolute(site, Path)
                    : $"{context.Request.Scheme}://{context.Request.Host}{Path}";
                context.Response.Headers.CacheControl = "public, max-age=3600";
                return Results.Text(Body(contact.Trim(), time.GetUtcNow() + Lifetime, canonical), "text/plain", Encoding.UTF8);
            })
            .ExcludeFromDescription();

    /// <summary>The file's text: its fields in the order RFC 9116 shows them, one per line.</summary>
    public static string Body(string contact, DateTimeOffset expires, string canonical) =>
        $"Contact: {contact}\n"
        + $"Expires: {expires.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}\n"
        + "Preferred-Languages: lt, en\n"
        + $"Canonical: {canonical}\n";

    /// <summary>Empty, a <c>mailto:</c> address, or an https address: what RFC 9116 allows as a contact that this site offers.</summary>
    public static bool IsValidContact(string? contact)
    {
        if (string.IsNullOrWhiteSpace(contact))
        {
            return true;
        }

        var value = contact.Trim();
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > "mailto:".Length && value.Contains('@', StringComparison.Ordinal) && !value.Any(char.IsWhiteSpace);
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0;
    }
}
