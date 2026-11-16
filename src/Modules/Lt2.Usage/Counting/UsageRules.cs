using System.Net;
using System.Net.Sockets;

namespace Lt2.Usage.Counting;

/// <summary>The counted rules of the site's own usage statistics (design 8.27), as pure functions.</summary>
internal static class UsageRules
{
    public const string Day = "day";
    public const string Month = "month";
    public const string Year = "year";

    public const string Visitor = "visitor";
    public const string Member = "member";

    /// <summary>"Dabar svetainėje": a page in the last five minutes.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

    /// <summary>Distinct paths kept a day; the rest are counted together under <see cref="OtherPaths"/>.</summary>
    public const int PathsPerDay = 2000;

    /// <summary>Distinct referring sites kept a day; the rest are counted together under <see cref="OtherHosts"/>.</summary>
    public const int HostsPerDay = 500;

    public const string OtherPaths = "(kiti)";
    public const string OtherHosts = "(kitos)";

    public const int MaxPathLength = 300;
    public const int MaxHostLength = 253;

    private static readonly string[] NotPages = ["/api", "/_framework", "/_content", "/_blazor", "/health", "/openapi"];

    /// <summary>
    /// Words that only automated clients put in their user agent. A browser's always starts with "Mozilla/"; what does not is
    /// a tool or a robot as well.
    /// </summary>
    private static readonly string[] CrawlerWords =
    [
        "bot", "crawl", "spider", "slurp", "archiver", "preview", "facebookexternalhit", "embedly", "headless", "lighthouse",
        "pingdom", "uptime", "monitor", "scrapy", "python", "curl", "wget", "httpclient", "okhttp", "java/", "go-http",
        "libwww", "axios", "node-fetch", "postman", "feedfetcher", "validator",
    ];

    /// <summary>
    /// A request counts as a page view when it is a GET of a page (not the API, the framework's files, health checks or the
    /// API description), answered 200 with HTML, and not a prefetch the browser made on its own.
    /// </summary>
    public static bool IsPageView(string method, string path, int status, string? contentType, bool prefetch) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && status == 200
        && !prefetch
        && contentType is not null
        && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
        && !NotPages.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>A user agent that is empty, is not a browser's, or names a robot or a tool.</summary>
    public static bool IsCrawler(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent)
        || !userAgent.StartsWith("Mozilla/", StringComparison.Ordinal)
        || CrawlerWords.Any(word => userAgent.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The network a visitor is counted by: an IPv4 address whole, an IPv6 address by its first 64 bits (phones and home
    /// routers change the rest every day or so). Null without an address.
    /// </summary>
    public static byte[]? Network(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetworkV6 ? bytes[..8] : bytes;
    }

    /// <summary>
    /// The path a page view is counted under: without the trailing slash (but "/" itself), and too long a path is counted with
    /// the others. A link's token is replaced before this (<c>Lt2.Hosting.SecretPaths</c>); the query is never part of it.
    /// </summary>
    public static string PathOf(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "/";
        }

        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed.Length > MaxPathLength || trimmed.Any(char.IsControl) ? OtherPaths : trimmed;
    }

    /// <summary>
    /// The referring site: only the host of an http or https address, in lower case and without "www.", and nothing when it is
    /// this site itself (moving between its own pages) or not an address at all.
    /// </summary>
    public static string? ReferrerHost(string? referrer, string? ownHost)
    {
        if (string.IsNullOrWhiteSpace(referrer)
            || !Uri.TryCreate(referrer, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var host = WithoutWww(uri.Host.ToLowerInvariant().TrimEnd('.'));
        if (host.Length is 0 or > MaxHostLength)
        {
            return null;
        }

        return ownHost is not null && host == WithoutWww(ownHost.ToLowerInvariant().TrimEnd('.')) ? null : host;
    }

    private static string WithoutWww(string host) => host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;

    /// <summary>The periods a Vilnius day belongs to: the day, its month and its year, each by its first day.</summary>
    public static IReadOnlyList<(string Period, DateOnly Starts)> PeriodsOf(DateOnly day) =>
        [(Day, day), (Month, new DateOnly(day.Year, day.Month, 1)), (Year, new DateOnly(day.Year, 1, 1))];

    /// <summary>Whether a period is over on <paramref name="today"/> (a Vilnius day): then its salt and sketch are deleted.</summary>
    public static bool HasEnded(string period, DateOnly starts, DateOnly today) => period switch
    {
        Day => today > starts,
        Month => today >= starts.AddMonths(1),
        Year => today >= starts.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown period."),
    };

    /// <summary>Whether a visitor or account last seen at <paramref name="lastSeen"/> is "now on the site".</summary>
    public static bool IsOnline(DateTimeOffset lastSeen, DateTimeOffset now) => now - lastSeen <= OnlineWindow;
}
