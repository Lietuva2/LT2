using System.Globalization;
using System.Net;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;

namespace Lt2.Notifications.Templates;

/// <summary>Marker type for Resources/EmailTemplates.resx.</summary>
internal sealed class EmailTemplates;

/// <summary>
/// One paragraph of an email: its plain text and, when the HTML part shows it in a shape of its own (a signature, a bill's
/// block, the part the system adds), that HTML. Without <paramref name="Html"/> the HTML part shows the text as one paragraph
/// (<see cref="EmailLayout.LinkedHtml"/>). The plain part is always the text.
/// </summary>
internal readonly record struct EmailBlock(string Text, string? Html = null)
{
    public static implicit operator EmailBlock(string text) => new(text);

    public static EmailBlock From(string text) => new(text);
}

/// <summary>
/// The one shape of every email: paragraphs as a plain-text part and as light HTML, no tracking of any kind (AGENTS.md; no
/// images, no tracking parameters, nothing to load). The HTML part is one small shell for members' and MPs' emails alike
/// (<see cref="Shell"/>): a centred column of at most 36rem with the wordmark "Lietuva 2.0" as text over a thin green rule, the
/// letter, and the footer in small muted type under a rule. Every style is inline and every colour explicit, so a client that
/// strips <c>&lt;style&gt;</c> or forces a dark mode still shows it sensibly. Optionally one paragraph, a code, shown large.
/// Every email ends with the footer (<see cref="Footer"/>): where the privacy notice is (design 8.22.3).
/// </summary>
internal static class EmailLayout
{
    /// <summary>The privacy notice's page, linked from every email's footer.</summary>
    public const string PrivacyPath = "/privatumas";

    /// <summary>The wordmark of the header: text, never an image.</summary>
    private const string Wordmark = "Lietuva 2.0";

    /// <summary>The type of the whole email (16px, line height 1.55, ink on white).</summary>
    private const string BaseStyle = "font-family:system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:16px;line-height:1.55;color:#1e293b;background-color:#ffffff";

    /// <summary>One paragraph of the letter.</summary>
    internal const string ParagraphStyle = "margin:0 0 16px";

    /// <summary>The footer's grey (contrast 4.8:1 on white), small.</summary>
    private const string FooterStyle = "margin:32px 0 0;padding:12px 0 0;border-top:1px solid #cbd5e1;font-size:13px;line-height:1.5;color:#64748b";

    /// <summary>The look of a "text: url" line's link in a letter: bold, in the site's green.</summary>
    private const string ActionLinkStyle = "font-weight:600;color:#1f6b3a";

    /// <summary>The look of the quiet link below the button: muted, underlined.</summary>
    private const string QuietLinkStyle = "color:#475569;text-decoration:underline";

    /// <summary>
    /// The look of every link in a footer ("Keisti periodiškumą", "atsisakyti laiškų", the privacy notice): the footer's grey,
    /// underlined, so it is grey in every mail program rather than the program's own link colour.
    /// </summary>
    private const string FooterLinkStyle = "color:#64748b;text-decoration:underline";

    /// <summary>
    /// What an address that stays visible in running text gets so that nothing scrolls sideways on a phone: it may break
    /// anywhere.
    /// </summary>
    private const string Breaking = "overflow-wrap:anywhere;word-break:break-word";

    /// <summary>The footer every email ends with: the privacy notice's full address (<see cref="SiteLinks.Absolute"/>).</summary>
    public static string Footer(IStringLocalizer l, string? siteAddress)
    {
        ArgumentNullException.ThrowIfNull(l);
        return l.Format(EmailText.Privacy, SiteLinks.Absolute(siteAddress, PrivacyPath));
    }

    /// <summary>
    /// A line that leads somewhere on the site (owner, 2026-11-17): "<paramref name="label"/>: <paramref name="url"/>". The plain
    /// part shows it so; in the HTML part every line of this shape is a link (see <see cref="LinkLineHtml(string, bool)"/>).
    /// </summary>
    public static string LinkLine(string label, string url) => $"{label}: {url}";

    /// <summary>
    /// A member's email (design 5.6): paragraphs as plain text and as light HTML in the shell. In the HTML part (owner, 2026-11-17: "make it a
    /// normal link"; the address printed under it dropped the same day as noise, the bold green kept) a line that ends
    /// "text: https://…" shows that text, from its last sentence, as a bold green link, and nothing more; any other address of
    /// the site in a line is a plain link that may break anywhere.
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
            ? $"<p style=\"{ParagraphStyle};font-size:28px;line-height:1.3;font-weight:700;letter-spacing:4px\">{Encode(p)}</p>"
            : ParagraphHtml(p)));
        return new EmailMessage(to, subject, text, Shell(subject, html, footerHtml), Headers: headers);
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
    /// The HTML part's shell: a table (for the programs that ignore <c>max-width</c> and <c>margin:auto</c>, Outlook) around a
    /// column of at most 36rem, 24px of room above and below and 16px at the sides, which is what a phone gets because the
    /// column is only as wide as the screen. The header is the wordmark as text over a thin rule; nothing is loaded from
    /// anywhere. <paramref name="footerHtml"/> is already HTML.
    /// </summary>
    private static string Shell(string subject, string bodyHtml, string footerHtml) =>
        "<!doctype html><html lang=\"lt\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
        + "<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">"
        + $"<title>{Encode(subject)}</title></head>"
        + $"<body style=\"margin:0;padding:0;{BaseStyle}\">"
        + $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;background-color:#ffffff\"><tr><td align=\"center\" style=\"padding:0\">"
        + "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:36rem\"><tr>"
        + $"<td style=\"padding:24px 16px;text-align:left;{BaseStyle}\">"
        + $"<div style=\"margin:0 0 24px;padding:0 0 10px;border-bottom:2px solid #1f6b3a;font-size:20px;line-height:1.3;font-weight:700;color:#154d29\">{Wordmark}</div>"
        + bodyHtml
        + $"<div style=\"{FooterStyle}\">{footerHtml}</div>"
        + "</td></tr></table></td></tr></table></body></html>";

    /// <summary>A paragraph of plain text as HTML: one <c>&lt;p&gt;</c>, each line through <see cref="LinkLineHtml(string, bool)"/>.</summary>
    internal static string ParagraphHtml(string paragraph) => $"<p style=\"{ParagraphStyle}\">{LinkedHtml(paragraph)}</p>";

    /// <summary>
    /// A paragraph's HTML, each of its lines through <see cref="LinkLineHtml(string, bool)"/>. In the footer (<paramref name="footer"/>) its
    /// links take the footer's small grey type, as every link there does; only the body's are bold green.
    /// </summary>
    internal static string LinkedHtml(string paragraph, bool footer = false) =>
        string.Join("<br>", paragraph.ReplaceLineEndings("\n").Split('\n').Select(line => LinkLineHtml(line, footer)));

    /// <summary>
    /// One line of an email as HTML: "…sentence. Link text: https://…" becomes the earlier sentences as text, then the
    /// link text as a bold link in the site's green (owner, 2026-11-17: "I had no problems with those green links"), without the
    /// address printed under it (owner, 2026-11-17: it was noise); any other line keeps its text with its addresses as plain
    /// links that may break anywhere. In the footer (<paramref name="footer"/>) every link is the footer's grey, underlined.
    /// </summary>
    internal static string LinkLineHtml(string line, bool footer = false) =>
        LinkLineHtml(line, footer ? ActionKind.Footer : ActionKind.Action);

    /// <summary>The same with the quiet look (muted, underlined) of the link below the takeover button.</summary>
    internal static string QuietLineHtml(string line) => LinkLineHtml(line, ActionKind.Quiet);

    private enum ActionKind
    {
        Action,
        Footer,
        Quiet,
    }

    private static string LinkLineHtml(string line, ActionKind kind)
    {
        var match = LinkAtEnd.Match(line);
        if (!match.Success)
        {
            var style = kind == ActionKind.Footer
                ? FooterLinkStyle + ";" + Breaking
                : (kind == ActionKind.Quiet ? QuietLinkStyle : "color:#1f6b3a") + ";" + Breaking;
            return Links.Replace(Encode(line), m => $"<a href=\"{m.Value}\" style=\"{style}\">{m.Value}</a>");
        }

        return Encode(match.Groups["lead"].Value) + Anchor(
            match.Groups["url"].Value,
            match.Groups["label"].Value.Trim(),
            kind switch { ActionKind.Footer => FooterLinkStyle, ActionKind.Quiet => QuietLinkStyle, _ => ActionLinkStyle });
    }

    /// <summary>A link: the address itself (nothing rewritten or tracked) around the encoded text, with its look when given.</summary>
    private static string Anchor(string url, string text, string? style = null) =>
        $"<a href=\"{WebUtility.HtmlEncode(url)}\"{(style is null ? "" : $" style=\"{style}\"")}>{Encode(text)}</a>";

    /// <summary>
    /// A line ending "text: https://…": <c>lead</c> is the line's earlier sentences (and a list's dash), <c>label</c> the last
    /// sentence before the colon, <c>url</c> the address.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex LinkAtEnd =
        new(@"^(?<lead>(?:.*[.!?]\s+)?(?:[–-]\s+)?)(?<label>\S[^\n]*?):\s+(?<url>https://[^\s<>""]+[^\s<>"".,;:)])\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// An email to an MP or for an MP (design 8.19.3): the same shell, on the MP stream (its own sender and, when configured,
    /// its own SMTP account), with its headers (<c>List-Unsubscribe</c>). The HTML part writes the site's addresses as the
    /// members' emails do: a line "Label: https://…" shows the label as the link (<see cref="LinkLineHtml(string, bool)"/>), so no
    /// long address is printed; the plain part keeps every address in full. The links are the addresses themselves: nothing is
    /// rewritten or tracked. A block with its own HTML (the letter's text, the signature, a bill, the part the system adds) shows
    /// that HTML; the plain part is always the blocks' text.
    /// </summary>
    public static EmailMessage BuildMp(string to, string subject, IReadOnlyList<EmailBlock> blocks, string footer, IReadOnlyDictionary<string, string>? headers)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var text = string.Join("\n\n", blocks.Select(b => b.Text)) + "\n\n" + footer + "\n";
        var html = string.Concat(blocks.Select(b => b.Html ?? ParagraphHtml(b.Text)));
        return new EmailMessage(to, subject, text, Shell(subject, html, LinkedHtml(footer, footer: true)), MpMail.MpMailKinds.Stream, headers);
    }

    /// <summary>
    /// A signature (the introduction letter's signer and role, a summary's "Lietuva 2.0"): the name in semibold with a little
    /// room above, the role muted on the next line.
    /// </summary>
    internal static string SignatureHtml(string name, string? role = null) =>
        $"<p style=\"margin:24px 0 {(role is null ? "16px" : "0")};font-weight:600\">{Encode(name)}</p>"
        + (role is null ? "" : $"<p style=\"margin:2px 0 16px;color:#475569\">{Encode(role)}</p>");

    /// <summary>
    /// What the system adds to an MP email, set apart from the letter by room and a light rule (design 8.19.5): optionally a
    /// call to action (a short <paramref name="lead"/> line and a button to <paramref name="buttonUrl"/>, a padded link on a
    /// one-cell table so it is a button in Outlook too, white on the site's green, 6.7:1), then a quiet link line
    /// (<paramref name="quietLine"/>, "Label: https://…").
    /// </summary>
    internal static string SystemHtml(string? lead, string? buttonLabel, string? buttonUrl, string quietLine)
    {
        var html = "<div style=\"margin:32px 0 0;padding:20px 0 0;border-top:1px solid #cbd5e1\">";
        if (lead is not null && buttonLabel is not null && buttonUrl is not null)
        {
            html += $"<p style=\"margin:0 0 12px;font-size:15px;line-height:1.5;color:#334155\">{Encode(lead)}</p>" + ButtonHtml(buttonUrl, buttonLabel, "0 0 20px");
        }

        return html + $"<p style=\"margin:0 0 16px;font-size:14px;line-height:1.5\">{QuietLineHtml(quietLine)}</p></div>";
    }

    /// <summary>A link in a letter by its label, in the bold green of "Label: https://…" lines.</summary>
    internal static string ActionAnchor(string url, string label) => Anchor(url, label, ActionLinkStyle);

    /// <summary>
    /// A button (a padded link on a one-cell table, so it is a button in Outlook too): white on the site's green, 6.7:1.
    /// <paramref name="margin"/> is the table's margin.
    /// </summary>
    internal static string ButtonHtml(string url, string label, string margin) =>
        $"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:{margin}\"><tr>"
        + "<td bgcolor=\"#1f6b3a\" style=\"background-color:#1f6b3a;border-radius:6px\">"
        + $"<a href=\"{WebUtility.HtmlEncode(url)}\" style=\"display:block;padding:12px 20px;font-size:16px;line-height:1.3;font-weight:600;color:#ffffff;text-decoration:none\">{Encode(label)}</a>"
        + "</td></tr></table>";

    /// <summary>
    /// A typed text as HTML (design 8.2, "A text's layout"): a <c>&lt;p&gt;</c> per line, a <c>&lt;ul&gt;</c> or an
    /// <c>&lt;ol&gt;</c> (the numbers as typed) for a list, every piece encoded and its addresses plain links that may break
    /// anywhere.
    /// </summary>
    internal static string TypedHtml(string typed) => string.Concat(TextLayout.Of(typed).Select(block => block.Kind switch
    {
        TextBlockKind.Bullets => $"<ul style=\"margin:0 0 16px;padding-left:24px\">" + string.Concat(block.Items.Select(i => $"<li style=\"margin:0 0 4px\">{Linked(Encode(i.Text))}</li>")) + "</ul>",
        TextBlockKind.Numbered => $"<ol style=\"margin:0 0 16px;padding-left:24px\">" + string.Concat(block.Items.Select(i => $"<li value=\"{i.Number?.ToString(CultureInfo.InvariantCulture)}\" style=\"margin:0 0 4px\">{Linked(Encode(i.Text))}</li>")) + "</ol>",
        _ => $"<p style=\"{ParagraphStyle}\">{Linked(Encode(block.Items[0].Text))}</p>",
    }));

    private static string Linked(string encoded) => Links.Replace(encoded, m => $"<a href=\"{m.Value}\" style=\"color:#1f6b3a;{Breaking}\">{m.Value}</a>");

    private static readonly System.Text.RegularExpressions.Regex Links =
        new(@"https://[^\s<>""]+[^\s<>"".,;:)]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static string Encode(string paragraph) =>
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
