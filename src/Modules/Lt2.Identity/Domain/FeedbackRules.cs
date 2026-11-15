using System.Text.RegularExpressions;

namespace Lt2.Identity.Domain;

/// <summary>Feedback from members (design 8.10) as pure functions.</summary>
internal static partial class FeedbackRules
{
    public const int TextMaxLength = 2000;


    public static readonly IReadOnlyList<string> Kinds = ["bug", "idea", "unclear", "other"];
    public static readonly IReadOnlyList<string> Statuses = ["new", "seen", "done"];

    public static string? CheckText(string? text)
    {
        var trimmed = (text ?? "").Trim();
        return trimmed.Length == 0 ? "feedback.empty" : trimmed.Length > TextMaxLength ? "feedback.too-long" : null;
    }

    public static string? CheckKind(string? kind) => kind is null || Kinds.Contains(kind) ? null : "feedback.kind";

    /// <summary>The page a member was on, as a path only: no query, no fragment, no host; null when it is not one.</summary>
    public static string? PagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cut = path.Trim().Split('?', '#')[0];
        return cut.StartsWith('/') && !cut.StartsWith("//", StringComparison.Ordinal) && cut.Length <= 200 && !cut.Any(char.IsControl) ? cut : null;
    }

    /// <summary>The browser's name and major version from a User-Agent header, for example "Edge 131"; null when unknown.</summary>
    public static string? Browser(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        foreach (var (pattern, name) in new[] { ("Edg/", "Edge"), ("OPR/", "Opera"), ("Firefox/", "Firefox"), ("Chrome/", "Chrome") })
        {
            if (Version(userAgent, pattern) is { } version)
            {
                return $"{name} {version}";
            }
        }

        return userAgent.Contains("Safari/", StringComparison.Ordinal) && Version(userAgent, "Version/") is { } safari ? $"Safari {safari}" : null;
    }

    /// <summary>A browser name as the member's form sent it: short and plain, or nothing.</summary>
    public static string? BrowserAsSent(string? browser) =>
        string.IsNullOrWhiteSpace(browser) ? null : BrowserText().IsMatch(browser.Trim()) ? browser.Trim() : null;

    /// <summary>Items older than the setting's months are deleted by the daily clean-up.</summary>
    public static DateOnly KeepFrom(DateOnly today, int months) => today.AddMonths(-months);

    private static string? Version(string userAgent, string marker)
    {
        var at = userAgent.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var digits = new string(userAgent[(at + marker.Length)..].TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 4 ? digits : null;
    }

    [GeneratedRegex("^[A-Za-z]{2,16} [0-9]{1,4}$")]
    private static partial Regex BrowserText();
}
