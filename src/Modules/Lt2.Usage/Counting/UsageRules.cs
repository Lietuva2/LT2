using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Lt2.Usage.Counting;

/// <summary>The counted rules of the site's own usage statistics (design 8.27), as pure functions.</summary>
internal static partial class UsageRules
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

    /// <summary>Referring sites counted for one visitor (or account) a day: one client cannot fill the list.</summary>
    public const int HostsPerVisitor = 3;

    public const string OtherPaths = "(kiti)";
    public const string OtherHosts = "(kitos)";

    /// <summary>A route parameter's value that is not a public object's, in a counted path.</summary>
    public const string Hidden = "***";

    public const int MaxHostLength = 253;

    private const int MaxValueLength = 60;

    private static readonly string[] NotPages = ["/api", "/_framework", "/_content", "/_blazor", "/health", "/openapi", "/nerasta", "/klaida"];

    /// <summary>
    /// Pages left out of the list of pages (their views still count in the totals): staff screens, an MP's and an office's own
    /// pages, an MP's mail links, a member's stop link (design 8.24.1) and a representative's invitation. At the trial's numbers their counts would tell when one
    /// moderator, administrator, MP or office member was active.
    /// </summary>
    private static readonly string[] Unlisted =
        ["/administravimas", "/moderavimas", "/seimo-narys", "/suvestines/nustatymai", "/suvestines/kopijos", "/laiskai/nebegauti", "/organizacijos/kvietimas", "/prisijungti/raktas"];

    /// <summary>
    /// Route parameters that name a public object (a bill's number, an MP's, a faction's, a committee's or an organisation's
    /// address, a vote, a summary's day): their value is kept in the counted path. Any other parameter (a token) is
    /// <see cref="Hidden"/>.
    /// </summary>
    private static readonly Dictionary<string, Func<string, string>> PublicParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Number"] = v => v.ToUpperInvariant(),
        ["Slug"] = v => v.ToLowerInvariant(),
        ["Id"] = v => v,
        ["Date"] = v => v,
    };

    /// <summary>
    /// A request counts as a page view when it is a GET of a page (not the API, the framework's files, health checks, the API
    /// description or the "not found" and error pages asked for directly), answered 200 with HTML, and not one the browser
    /// fetched or prerendered on its own.
    /// </summary>
    public static bool IsPageView(string method, string path, int status, string? contentType, bool prefetch) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && status == 200
        && !prefetch
        && contentType is not null
        && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
        && !NotPages.Any(prefix => Within(path, prefix));

    /// <summary>
    /// A user agent that is empty, is not a browser's (every browser's starts with "Mozilla/"), or names a robot or a tool. A
    /// robot's name ends in "bot" followed by its version or a separator ("Googlebot/2.1", "Slackbot-…", "(… Twitterbot)"), so a
    /// phone whose make contains the letters does not count as one. A signed-in session is a person whatever it sends.
    /// </summary>
    public static bool IsCrawler(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent)
        || !userAgent.StartsWith("Mozilla/", StringComparison.Ordinal)
        || Robot().IsMatch(userAgent);

    [GeneratedRegex(
        @"bot[/;)@\-]|bot$|\+https?:|crawl|spider|slurp|archiver|preview|facebookexternalhit|embedly|headless|lighthouse|pingdom|uptime|monitor|scrapy|python|curl|wget|httpclient|okhttp|java/|go-http|libwww|axios|node-fetch|postman|feedfetcher|validator",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Robot();

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
    /// The path a page view is counted under: the page's route, not the address asked for, so "/METODIKA" and "/metodika" are
    /// one page and nothing typed into an address can become a page. A public object's parameter keeps its value (a bill's
    /// number in capitals, an address in small letters); any other parameter, a link's token first of all, is
    /// <see cref="Hidden"/>. No route: <see cref="OtherPaths"/>.
    /// </summary>
    public static string CountedPath(string? template, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (template is null)
        {
            return OtherPaths;
        }

        var segments = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = new StringBuilder();
        foreach (var segment in segments)
        {
            path.Append('/');
            if (!segment.StartsWith('{') || !segment.EndsWith('}'))
            {
                path.Append(segment.ToLowerInvariant());
                continue;
            }

            var name = segment[1..^1].TrimStart('*').Split(':', 2)[0].TrimEnd('?');
            var value = values.GetValueOrDefault(name);
            path.Append(PublicParameters.TryGetValue(name, out var normalise) && IsPlainValue(value) ? normalise(value!) : Hidden);
        }

        return path.Length == 0 ? "/" : path.ToString();
    }

    private static bool IsPlainValue(string? value) =>
        value is { Length: > 0 and <= MaxValueLength } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_');

    /// <summary>Whether a counted path appears in the list of pages (see <see cref="Unlisted"/>).</summary>
    public static bool IsListed(string path) => !Unlisted.Any(prefix => Within(path, prefix));

    /// <summary>
    /// The referring site: only the host of an http or https address, in lower case and without "www." or a port, and nothing
    /// when it is this site itself (moving between its own pages) or not an address at all.
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

        var host = WithoutWww(uri.IdnHost.ToLowerInvariant().TrimEnd('.'));
        if (host.Length is 0 or > MaxHostLength)
        {
            return null;
        }

        return ownHost is not null && host == WithoutWww(ownHost.ToLowerInvariant().TrimEnd('.')) ? null : host;
    }

    private static string WithoutWww(string host) => host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;

    private static bool Within(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

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
