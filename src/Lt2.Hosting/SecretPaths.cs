using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Lt2.Hosting;

/// <summary>
/// Design 8.19.4: an MP's link carries its token in the path (<c>/suvestines/nustatymai/{token}</c>, the confirmation and copy
/// pages, and <c>/api/v1/mp-mail/{token}/…</c>, whose one-click form mail programs need). Request logs and traces keep such a
/// path with the token replaced, as Caddy's log filter does for the proxy's own log.
/// </summary>
public static partial class SecretPaths
{
    public const string Replacement = "***";

    /// <summary>The path with an MP link's token replaced; any other text unchanged.</summary>
    public static string? Redact(string? text) =>
        text is null || !MayHoldToken(text) ? text : Token().Replace(text, m => m.Groups["prefix"].Value + Replacement);

    private static bool MayHoldToken(string text) =>
        text.Contains("mp-mail/", StringComparison.OrdinalIgnoreCase)
        || text.Contains("suvestines/nustatymai/", StringComparison.OrdinalIgnoreCase)
        || text.Contains("suvestines/kopijos/", StringComparison.OrdinalIgnoreCase);

    // The path with or without its leading slash: routing's own debug lines quote a route value, which has none.
    [GeneratedRegex(
        "(?<prefix>(?:^|/)(?:api/v1/)?(?:mp-mail|suvestines/nustatymai(?:/patvirtinti)?|suvestines/kopijos)/)(?!extra/(?:confirm|stop)\\b)(?!patvirtinti\\b)[A-Za-z0-9_%-]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}

/// <summary>Replaces an MP link's token in every text property of every log event (<see cref="SecretPaths"/>).</summary>
public sealed class SecretPathEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            if (value is ScalarValue { Value: { } raw } && raw.ToString() is { } text && SecretPaths.Redact(text) is { } redacted && redacted != text)
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(redacted)));
            }
        }
    }
}
