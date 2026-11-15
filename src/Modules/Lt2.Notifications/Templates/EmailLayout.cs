using System.Globalization;
using System.Net;
using Microsoft.Extensions.Localization;

namespace Lt2.Notifications.Templates;

/// <summary>Marker type for Resources/EmailTemplates.resx.</summary>
internal sealed class EmailTemplates;

/// <summary>
/// The one shape of every email: paragraphs as a plain-text part and as light HTML (one <c>&lt;p&gt;</c> each, line breaks
/// kept as <c>&lt;br&gt;</c>), no tracking of any kind (AGENTS.md). Optionally one paragraph, a code, shown large.
/// </summary>
internal static class EmailLayout
{
    public static EmailMessage Build(string to, string subject, IReadOnlyList<string> paragraphs, int? highlighted = null)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select((p, i) => i == highlighted
            ? $"<p style=\"font-size:28px;font-weight:700;letter-spacing:4px\">{Encode(p)}</p>"
            : $"<p>{Encode(p)}</p>"));
        return new EmailMessage(
            to,
            subject,
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>");
    }

    private static string Encode(string paragraph) =>
        WebUtility.HtmlEncode(paragraph.ReplaceLineEndings("\n")).Replace("\n", "<br>", StringComparison.Ordinal);
}

/// <summary>The resource keys every email shares, and formatting with the invariant culture.</summary>
internal static class EmailText
{
    public const string Greeting = "Email.Greeting";
    public const string Signature = "Email.Signature";
    public const string CodeValidity = "Email.CodeValidity";
    public const string Contact = "Email.Contact";
    public const string Statement = "Email.Statement";

    public static string Format(this IStringLocalizer l, string key, params object?[] values) =>
        string.Format(CultureInfo.InvariantCulture, l[key], values);
}
