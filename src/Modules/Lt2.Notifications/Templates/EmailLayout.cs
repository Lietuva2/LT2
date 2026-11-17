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
    /// normal link"; the address printed under it dropped the same day as noise, the bold green kept) a line that ends
    /// "text: https://…" shows that text, from its last sentence, as a bold green link, and nothing more; any other address of
    /// the site in a line is a plain link.
    /// The links are the addresses themselves: nothing is rewritten or tracked (AGENTS.md). The plain part is unchanged.
    /// </summary>
    public static EmailMessage Build(
        string to, string subject, IReadOnlyList<string> paragraphs, string footer, int? highlighted = null, IReadOnlyDictionary<string, string>? headers = null) =>
        Compose(to, subject, paragraphs, footer, LinkedHtml(footer, footer: true), highlighted, headers);

    /// <summary>
    /// An email of the member's rhythm (design 8.24, 8.24.1; owner, 2026-11-17): a member's email whose footer, in small grey type
    /// under the rule, says why it came (<paramref name="reason"/>, one sentence without a link), then "Keisti periodiškumą arba
    /// atsisakyti laiškų", "Keisti periodiškumą" a link to the choice of how often (<paramref name="rhythmPath"/>) and "atsisakyti
    /// laiškų" a link to the stop page (<c>/laiskai/nebegauti/{token}</c>, the page, never the API), then the privacy line. The
    /// plain part, which holds no links, gives the addresses: "Keisti periodiškumą: https://… · Atsisakyti laiškų: https://…".
    /// With the stop link's headers (<see cref="StopHeaders"/>). Without a token (a test), the choice's link alone and no headers.
    /// </summary>
    public static EmailMessage BuildNotice(
        IStringLocalizer l, string? siteAddress, string to, string subject, IReadOnlyList<string> paragraphs, string reason, string rhythmPath, string? stopToken)
    {
        ArgumentNullException.ThrowIfNull(l);
        var privacy = Footer(l, siteAddress);
        var rhythm = SiteLinks.Absolute(siteAddress, rhythmPath);
        var stop = stopToken is null ? null : SiteLinks.Absolute(siteAddress, MemberMailLinks.Page(stopToken));
        var links = stop is null
            ? LinkLine(l[EmailText.ChangeRhythm], rhythm)
            : l.Format(EmailText.NoticeLinksText, rhythm, stop);
        var linksHtml = stop is null
            ? Anchor(rhythm, l[EmailText.ChangeRhythm], FooterLinkStyle)
            : string.Format(
                CultureInfo.InvariantCulture,
                WebUtility.HtmlEncode(l[EmailText.NoticeLinks]),
                Anchor(rhythm, l[EmailText.ChangeRhythm], FooterLinkStyle),
                Anchor(stop, l[EmailText.StopNotices], FooterLinkStyle));
        return Compose(
            to,
            subject,
            paragraphs,
            reason + "\n" + links + "\n" + privacy,
            Encode(reason) + "<br>" + linksHtml + "<br>" + LinkedHtml(privacy, footer: true),
            null,
            StopHeaders(siteAddress, stopToken));
    }

    private static EmailMessage Compose(
        string to, string subject, IReadOnlyList<string> paragraphs, string footer, string footerHtml, int? highlighted, IReadOnlyDictionary<string, string>? headers)
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
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}{MemberFooterHtml(footerHtml)}</body></html>",
            Headers: headers);
    }

    /// <summary>
    /// The headers of every email in the member's rhythm (design 8.24.1): <c>List-Unsubscribe</c> with the one-click stop and
    /// <c>List-Unsubscribe-Post</c> (RFC 8058), as an MP's summary has (design 8.19.3). None without a token.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? StopHeaders(string? siteAddress, string? stopToken) =>
        stopToken is null
            ? null
            : new Dictionary<string, string>
            {
                [MpMailTemplates.ListUnsubscribe] = "<" + SiteLinks.Absolute(siteAddress, MemberMailLinks.OneClick(stopToken)) + ">",
                [MpMailTemplates.ListUnsubscribePost] = "List-Unsubscribe=One-Click",
            };

    /// <summary>
    /// A paragraph's HTML, each of its lines through <see cref="LinkLineHtml"/>. In the footer (<paramref name="footer"/>) its
    /// links take the footer's small grey type, as every link there does; only the body's are bold green.
    /// </summary>
    internal static string LinkedHtml(string paragraph, bool footer = false) =>
        string.Join("<br>", paragraph.ReplaceLineEndings("\n").Split('\n').Select(line => LinkLineHtml(line, footer)));

    /// <summary>
    /// One line of a member's email as HTML: "…sentence. Link text: https://…" becomes the earlier sentences as text, then the
    /// link text as a bold link in the site's green (owner, 2026-11-17: "I had no problems with those green links"), without the
    /// address printed under it (owner, 2026-11-17: it was noise); any other line keeps its text with its addresses as plain links.
    /// In the footer (<paramref name="footer"/>) every link is the footer's grey, underlined (<see cref="FooterLinkStyle"/>).
    /// </summary>
    internal static string LinkLineHtml(string line, bool footer = false)
    {
        var match = LinkAtEnd.Match(line);
        if (!match.Success)
        {
            return footer
                ? Links.Replace(Encode(line), m => $"<a href=\"{m.Value}\" style=\"{FooterLinkStyle}\">{m.Value}</a>")
                : Links.Replace(Encode(line), m => $"<a href=\"{m.Value}\">{m.Value}</a>");
        }

        return Encode(match.Groups["lead"].Value) + Anchor(match.Groups["url"].Value, match.Groups["label"].Value.Trim(), footer ? FooterLinkStyle : ActionLinkStyle);
    }

    /// <summary>The look of a "text: url" line's link in a member's email: bold, in the site's green.</summary>
    private const string ActionLinkStyle = "font-weight:600;color:#1f6b3a";

    /// <summary>
    /// The look of every link in a member's email footer ("Keisti periodiškumą", "atsisakyti laiškų", the privacy notice): the
    /// footer's grey, underlined, so it is grey in every mail program rather than the program's own link colour.
    /// </summary>
    private const string FooterLinkStyle = "color:#64748b;text-decoration:underline";

    /// <summary>A link: the address itself (nothing rewritten or tracked) around the encoded text, with its look when given.</summary>
    private static string Anchor(string url, string text, string? style = null) =>
        $"<a href=\"{WebUtility.HtmlEncode(url)}\"{(style is null ? "" : $" style=\"{style}\"")}>{Encode(text)}</a>";

    /// <summary>
    /// A line ending "text: https://…": <c>lead</c> is the line's earlier sentences (and a list's dash), <c>label</c> the last
    /// sentence before the colon, <c>url</c> the address.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex LinkAtEnd =
        new(@"^(?<lead>(?:.*[.!?]\s+)?(?:[–-]\s+)?)(?<label>\S[^\n]*?):\s+(?<url>https://[^\s<>""]+[^\s<>"".,;:)])\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The member's footer, already HTML, in small grey type under a rule.</summary>
    private static string MemberFooterHtml(string footerHtml) =>
        $"<p style=\"margin-top:24px;padding-top:8px;border-top:1px solid #cbd5e1;font-size:13px;color:#64748b\">{footerHtml}</p>";

    /// <summary>
    /// An email to an MP or for an MP (design 8.19.3): the same shape, on the MP stream (its own sender and, when configured,
    /// its own SMTP account), with its headers (<c>List-Unsubscribe</c>), and with the site's addresses written in it as plain
    /// links in the HTML part. The links are the addresses themselves: nothing is rewritten or tracked.
    /// </summary>
    /// <param name="typed">
    /// The index of the one paragraph a person typed (the introduction letter's text, design 8.19.5): its HTML shows the text's
    /// paragraphs and lists (<see cref="TextLayout"/>, design 8.2); its plain part stays as typed.
    /// </param>
    public static EmailMessage BuildMp(string to, string subject, IReadOnlyList<string> paragraphs, string footer, IReadOnlyDictionary<string, string>? headers, int? typed = null)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var text = string.Join("\n\n", paragraphs) + "\n\n" + footer + "\n";
        var html = string.Concat(paragraphs.Select((p, i) => i == typed ? TypedHtml(p) : $"<p>{Linked(Encode(p))}</p>"));
        return new EmailMessage(
            to,
            subject,
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b;max-width:42rem\">{html}{FooterHtml(footer)}</body></html>",
            MpMail.MpMailKinds.Stream,
            headers);
    }

    /// <summary>
    /// A typed text as HTML (design 8.2, "A text's layout"): a <c>&lt;p&gt;</c> per line, a <c>&lt;ul&gt;</c> or an
    /// <c>&lt;ol&gt;</c> (the numbers as typed) for a list, every piece encoded and its addresses plain links.
    /// </summary>
    internal static string TypedHtml(string typed) => string.Concat(TextLayout.Of(typed).Select(block => block.Kind switch
    {
        TextBlockKind.Bullets => "<ul>" + string.Concat(block.Items.Select(i => $"<li>{Linked(Encode(i.Text))}</li>")) + "</ul>",
        TextBlockKind.Numbered => "<ol>" + string.Concat(block.Items.Select(i => $"<li value=\"{i.Number?.ToString(CultureInfo.InvariantCulture)}\">{Linked(Encode(i.Text))}</li>")) + "</ol>",
        _ => $"<p>{Linked(Encode(block.Items[0].Text))}</p>",
    }));

    private static string Linked(string encoded) => Links.Replace(encoded, m => $"<a href=\"{m.Value}\">{m.Value}</a>");

    private static readonly System.Text.RegularExpressions.Regex Links =
        new(@"https://[^\s<>""]+[^\s<>"".,;:)]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The footer in small grey type under a rule, its address a link in the footer's grey (<see cref="FooterLinkStyle"/>, as in
    /// a member's footer; nothing rewritten or tracked).
    /// </summary>
    private static string FooterHtml(string footer) =>
        $"<p style=\"margin-top:24px;padding-top:8px;border-top:1px solid #cbd5e1;font-size:13px;color:#64748b\">{Links.Replace(Encode(footer), m => $"<a href=\"{m.Value}\" style=\"{FooterLinkStyle}\">{m.Value}</a>")}</p>";

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

    /// <summary>"Keisti periodiškumą": the footer's link to the choice of how often, in every email of the member's rhythm (design 8.24.1).</summary>
    public const string ChangeRhythm = "Email.Notices.Rhythm";

    /// <summary>"atsisakyti laiškų": the footer's link to the stop page, in every email of the member's rhythm (design 8.24.1).</summary>
    public const string StopNotices = "Email.Notices.Stop";

    /// <summary>"{0} arba {1}": the footer's line of the two links in the HTML part.</summary>
    public const string NoticeLinks = "Email.Notices.Links";

    /// <summary>"Keisti periodiškumą: {0} · Atsisakyti laiškų: {1}": the same line in the plain part, with the addresses.</summary>
    public const string NoticeLinksText = "Email.Notices.LinksText";

    /// <summary>The way without an email's link (owner, 2026-11-17): the page to open and the code to type there.</summary>
    public const string WithoutLink = "Email.WithoutLink";

    public static string Format(this IStringLocalizer l, string key, params object?[] values) =>
        string.Format(CultureInfo.InvariantCulture, l[key], values);
}
