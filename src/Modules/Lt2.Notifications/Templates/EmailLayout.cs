using System.Globalization;
using System.Net;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;

namespace Lt2.Notifications.Templates;

/// <summary>Marker type for Resources/EmailTemplates.resx.</summary>
internal sealed class EmailTemplates;

/// <summary>
/// The one shape of every email: paragraphs as a plain-text part and as light HTML (one <c>&lt;p&gt;</c> each, line breaks
/// kept as <c>&lt;br&gt;</c>), no tracking of any kind (AGENTS.md). Optionally one paragraph, a code, shown large. Every email
/// ends with the footer (<see cref="Footer"/>): where the privacy notice is (design 8.22.3).
/// </summary>
internal static class EmailLayout
{
    /// <summary>The privacy notice's page, linked from every email's footer.</summary>
    public const string PrivacyPath = "/privatumas";

    /// <summary>The footer every email ends with: the privacy notice's full address (<see cref="SiteLinks.Absolute"/>).</summary>
    public static string Footer(IStringLocalizer l, string? siteAddress)
    {
        ArgumentNullException.ThrowIfNull(l);
        return l.Format(EmailText.Privacy, SiteLinks.Absolute(siteAddress, PrivacyPath));
    }

    /// <summary>
    /// A line that leads somewhere on the site (owner, 2026-11-17): "<paramref name="label"/>: <paramref name="url"/>". The plain
    /// part shows it so; in the HTML part every line of this shape is a link (see <see cref="LinkLineHtml"/>).
    /// </summary>
    public static string LinkLine(string label, string url) => $"{label}: {url}";

    /// <summary>
    /// A member's email (design 5.6): paragraphs as plain text and as light HTML. In the HTML part (owner, 2026-11-17: "make it a
    /// normal link with the text and url under it") a line that ends "text: https://…" shows that text, from its last sentence,
    /// as the link, with the address printed in small type under it; any other address of the site in a line is a plain link.
    /// The links are the addresses themselves: nothing is rewritten or tracked (AGENTS.md). The plain part is unchanged.
    /// </summary>
    public static EmailMessage Build(string to, string subject, IReadOnlyList<string> paragraphs, string footer, int? highlighted = null)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var text = string.Join("\n\n", paragraphs) + "\n\n" + footer + "\n";
        var html = string.Concat(paragraphs.Select((p, i) => i == highlighted
            ? $"<p style=\"font-size:28px;font-weight:700;letter-spacing:4px\">{Encode(p)}</p>"
            : $"<p>{LinkedHtml(p)}</p>"));
        return new EmailMessage(
            to,
            subject,
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}{MemberFooterHtml(footer)}</body></html>");
    }

    /// <summary>A paragraph's HTML, each of its lines through <see cref="LinkLineHtml"/>.</summary>
    internal static string LinkedHtml(string paragraph) =>
        string.Join("<br>", paragraph.ReplaceLineEndings("\n").Split('\n').Select(LinkLineHtml));

    /// <summary>
    /// One line of a member's email as HTML: "…sentence. Link text: https://…" becomes the earlier sentences as text, then the
    /// link text as a link to the address, then the address in small grey type under it; any other line keeps its text with its
    /// addresses as plain links.
    /// </summary>
    internal static string LinkLineHtml(string line)
    {
        var match = LinkAtEnd.Match(line);
        if (!match.Success)
        {
            return Links.Replace(Encode(line), m => $"<a href=\"{m.Value}\">{m.Value}</a>");
        }

        var url = WebUtility.HtmlEncode(match.Groups["url"].Value);
        return Encode(match.Groups["lead"].Value)
            + $"<a href=\"{url}\" style=\"font-weight:600;color:#1f6b3a\">{Encode(match.Groups["label"].Value.Trim())}</a>"
            + $"<br><span style=\"font-size:13px;color:#64748b;word-break:break-all\">{url}</span>";
    }

    /// <summary>
    /// A line ending "text: https://…": <c>lead</c> is the line's earlier sentences (and a list's dash), <c>label</c> the last
    /// sentence before the colon, <c>url</c> the address.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex LinkAtEnd =
        new(@"^(?<lead>(?:.*[.!?]\s+)?(?:[–-]\s+)?)(?<label>\S[^\n]*?):\s+(?<url>https://[^\s<>""]+[^\s<>"".,;:)])\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The member's footer in small grey type under a rule, its link as every other line's.</summary>
    private static string MemberFooterHtml(string footer) =>
        $"<p style=\"margin-top:24px;padding-top:8px;border-top:1px solid #cbd5e1;font-size:13px;color:#64748b\">{LinkedHtml(footer)}</p>";

    /// <summary>
    /// An email to an MP or for an MP (design 8.19.3): the same shape, on the MP stream (its own sender and, when configured,
    /// its own SMTP account), with its headers (<c>List-Unsubscribe</c>), and with the site's addresses written in it as plain
    /// links in the HTML part. The links are the addresses themselves: nothing is rewritten or tracked.
    /// </summary>
    public static EmailMessage BuildMp(string to, string subject, IReadOnlyList<string> paragraphs, string footer, IReadOnlyDictionary<string, string>? headers)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var text = string.Join("\n\n", paragraphs) + "\n\n" + footer + "\n";
        var html = string.Concat(paragraphs.Select(p => $"<p>{Links.Replace(Encode(p), m => $"<a href=\"{m.Value}\">{m.Value}</a>")}</p>"));
        return new EmailMessage(
            to,
            subject,
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b;max-width:42rem\">{html}{FooterHtml(footer)}</body></html>",
            MpMail.MpMailKinds.Stream,
            headers);
    }

    private static readonly System.Text.RegularExpressions.Regex Links =
        new(@"https://[^\s<>""]+[^\s<>"".,;:)]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The footer in small grey type under a rule, its address a plain link (nothing rewritten or tracked).</summary>
    private static string FooterHtml(string footer) =>
        $"<p style=\"margin-top:24px;padding-top:8px;border-top:1px solid #cbd5e1;font-size:13px;color:#64748b\">{Links.Replace(Encode(footer), m => $"<a href=\"{m.Value}\">{m.Value}</a>")}</p>";

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
    public const string Privacy = "Email.Privacy";

    /// <summary>The way without an email's link (owner, 2026-11-17): the page to open and the code to type there.</summary>
    public const string WithoutLink = "Email.WithoutLink";

    public static string Format(this IStringLocalizer l, string key, params object?[] values) =>
        string.Format(CultureInfo.InvariantCulture, l[key], values);
}
