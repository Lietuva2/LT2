using System.Globalization;
using System.Text;
using Lt2.Notifications.Digests;
using Lt2.Notifications.MpMail;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// MP mail (design 8.19.3 to 8.19.5, appendix A): the summary as an MP or a copy's recipient reads it, the introduction letter,
/// and the emails that confirm a change of the MP's choices. Both sides always, with the 30-day counts; the MP's own response
/// or the link to answer; at the end what the numbers are and are not, the public copy, the preferences link and the sentence on
/// taking over the profile. Links are full addresses of the site (<see cref="SiteLinks.Absolute"/>); nothing is tracked. The
/// moderators' summaries screen shows the part common to every MP (design 8.19.2, owner 2026-11-17).
/// </summary>
internal sealed class MpMailTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public const string ListUnsubscribe = "List-Unsubscribe";
    public const string ListUnsubscribePost = "List-Unsubscribe-Post";

    public EmailMessage Digest(MpDigestEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (subject, blocks) = Blocks(model);
        return EmailLayout.BuildMp(model.To, subject, blocks, EmailLayout.Footer(l, site.Value.Address), Headers(model.UnsubscribePath, model.CopyStopPath));
    }

    public EmailMessage Introduction(MpIntroductionEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var blocks = new List<EmailBlock>();
        if (model.Test)
        {
            blocks.Add(l["Intro.Test"].Value);
        }

        // The letter as the administrator typed it (design 8.2, "A text's layout"): the plain part keeps it so, the HTML part
        // shows its paragraphs and lists. The signer is the signature: the name in semibold, the role muted below it.
        var letter = model.Text.ReplaceLineEndings("\n").Trim();
        blocks.Add(new EmailBlock(letter, EmailLayout.TypedHtml(letter)));
        var name = model.SignerName.Trim();
        var role = model.SignerRole.Trim();
        blocks.Add(new EmailBlock(name + "\n" + role, EmailLayout.SignatureHtml(name, role)));
        blocks.AddRange(Closing(model.PreferencesPath ?? (model.Test ? MpMailPaths.Preferences("…") : null), null, signOff: false));
        return EmailLayout.BuildMp(model.To, (model.Test ? l["Intro.TestSubject"] + " " : "") + model.Subject.Trim(), blocks, EmailLayout.Footer(l, site.Value.Address), Headers(model.UnsubscribePath, null));
    }

    public EmailMessage Notice(MpMailNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!MpMailNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown MP mail notice '{model.Notice}'.");
        }

        var link = model.Path is { } path ? Link(path) : "";
        var blocks = new List<EmailBlock>
        {
            l[EmailText.Greeting].Value,
            l.Format($"MpMail.{model.Notice}.Body", model.MpName, model.ExtraAddress ?? "", link, model.ValidHours?.ToString(CultureInfo.InvariantCulture) ?? ""),
        };
        if (model.Notice == MpMailNotices.Changed && model.Frequency is { } frequency)
        {
            blocks.Add(l.Format("MpMail.Now", l["MpMail.Frequency." + frequency]));
            if (frequency == Frequencies.Stopped)
            {
                blocks.Add(l["MpMail.StoppedNote"].Value);
            }
        }

        if (model.Notice is MpMailNotices.Changed or MpMailNotices.ExtraConfirmed or MpMailNotices.ExtraRemoved)
        {
            blocks.Add(l.Format("MpMail.NotYou", link));
        }

        blocks.Add(Signature());
        return EmailLayout.BuildMp(model.To, l[$"MpMail.{model.Notice}.Subject"], blocks, EmailLayout.Footer(l, site.Value.Address), null);
    }

    /// <summary>The subject and paragraphs of one MP's summary: the email's text part.</summary>
    public (string Subject, IReadOnlyList<string> Paragraphs) Text(MpDigestEmail model)
    {
        var (subject, blocks) = Blocks(model);
        return (subject, blocks.Select(b => b.Text).ToList());
    }

    /// <summary>One MP's summary as blocks: each with its text and, where its HTML has a shape of its own, that HTML.</summary>
    private (string Subject, List<EmailBlock> Blocks) Blocks(MpDigestEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        // A copy: to the MP's confirmed extra recipient, or to an office member (design 8.19.3, owner 2026-11-17).
        var office = model.Recipient == Recipients.Office;
        var copy = office || model.Recipient == Recipients.Extra;
        var day = model.RunDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var blocks = new List<EmailBlock> { l[EmailText.Greeting].Value };
        if (copy)
        {
            blocks.Add(office
                ? l.Format("Digest.CopyOfOffice", model.MpName, Link(AccountScreens.Taking))
                : l.Format("Digest.CopyOf", model.MpName));
        }

        blocks.Add(l.Format("Digest.Intro", model.Bills.Count));
        foreach (var bill in model.Bills)
        {
            blocks.Add(Bill(bill, copy, model));
        }

        blocks.AddRange(Closing(copy ? null : model.PreferencesPath, copy ? model.CopyStopPath : null, model.PublicCopyPath));
        return (l.Format(copy ? "Digest.CopySubject" : "Digest.Subject", day), blocks);
    }

    private const string ForColour = "#1b6034";
    private const string AgainstColour = "#85322f";

    /// <summary>
    /// One bill (appendix A) as text and as a card modelled on the public copy (<c>DigestPage.razor</c>): the number muted
    /// before the title, which links to the bill's page; the stage and planned day as one muted line; the two counts large in the
    /// side's colour with a proportion bar and the "older than 30 days" figure as a small note; the party lines; each side's
    /// arguments under its heading; then one action: the MP's own response with its link, or the button to answer. The text part
    /// has the same lines, each address once.
    /// </summary>
    private EmailBlock Bill(MpDigestBill bill, bool copy, MpDigestEmail model)
    {
        var lt = CultureInfo.GetCultureInfo("lt-LT");
        var item = bill.Item;
        var billLink = Link("/seimas/" + Uri.EscapeDataString(item.BillNumber));
        var text = new StringBuilder();
        var html = new StringBuilder("<div style=\"margin:0 0 20px;padding:16px;border:1px solid #e2e8f0;border-radius:8px;background-color:#f8fafc;color:#1e293b\">");
        void Line(string line) => text.Append('\n').Append(line);

        text.Append(CultureInfo.InvariantCulture, $"{item.BillNumber} · {item.BillTitle}");
        html.Append("<h3 style=\"margin:0 0 4px;font-size:18px;line-height:1.35;font-weight:700\"><span style=\"color:#64748b;font-weight:600\">")
            .Append(EmailLayout.Encode(item.BillNumber)).Append("</span> <a href=\"").Append(System.Net.WebUtility.HtmlEncode(billLink))
            .Append("\" style=\"color:#1e293b\">").Append(EmailLayout.Encode(item.BillTitle)).Append("</a></h3>");

        var stage = item.Stage is { } s && l["Stage." + s] is { ResourceNotFound: false } name ? Capital(name.Value) : null;
        string? when = null;
        if (item.PlannedDate is { } planned)
        {
            when = stage is null
                ? l.Format("Digest.Planned", planned.ToString("MMMM d", lt))
                : l.Format("Digest.StagePlanned", stage, planned.ToString("MMMM d", lt));
        }
        else if (stage is not null)
        {
            when = stage;
        }

        if (when is not null)
        {
            Line(when);
            html.Append("<p style=\"margin:0 0 14px;font-size:14px;color:#64748b\">").Append(EmailLayout.Encode(when)).Append("</p>");
        }
        else
        {
            html.Append("<div style=\"margin:0 0 10px\"></div>");
        }

        Line(l.Format("Digest.For", N(item.For), N(item.ForOlderThan30Days)));
        Line(l.Format("Digest.Against", N(item.Against), N(item.AgainstOlderThan30Days)));
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;margin:0 0 10px\"><tr>")
            .Append(Figure(l["Digest.ForLabel"], item.For, item.ForOlderThan30Days, ForColour, "0 8px 0 0"))
            .Append(Figure(l["Digest.AgainstLabel"], item.Against, item.AgainstOlderThan30Days, AgainstColour, "0 0 0 8px"))
            .Append("</tr></table>").Append(Bar(item.For, item.Against));

        var party = new List<string>();
        if (bill.Party is { } mine)
        {
            if (mine.For is { } f)
            {
                party.Add(l.Format("Digest.PartyFor", mine.Party, N(f)));
            }

            if (mine.Against is { } a)
            {
                party.Add(l.Format("Digest.PartyAgainst", mine.Party, N(a)));
            }
        }

        foreach (var line in party)
        {
            Line(line);
            html.Append("<p style=\"margin:0 0 2px;font-size:13px;color:#64748b\">").Append(EmailLayout.Encode(line.Trim())).Append("</p>");
        }

        if (item.SurgeSharePercent is { } share)
        {
            var surge = l.Format("Digest.Surge", item.SurgeWindowHours, share);
            Line(surge);
            html.Append("<p style=\"margin:10px 0 0;padding:8px 10px;border-left:3px solid #94a3b8;font-size:13px;line-height:1.5;color:#475569\">").Append(EmailLayout.Encode(surge.Trim())).Append("</p>");
        }

        foreach (var side in new[] { "for", "against" })
        {
            var colour = side == "for" ? ForColour : AgainstColour;
            var reasons = item.Reasons.Where(r => r.Side == side).ToList();
            if (reasons.Count == 0)
            {
                // A side may have no published argument (design 8.1: records open without arguments).
                var none = l["Digest.NoReasons." + side].Value;
                Line(none);
                html.Append(SideHeading(none.Trim(), colour, muted: true));
                continue;
            }

            var of = DigestReasonCount.Shown(side == "for" ? item.ForReasonCount : item.AgainstReasonCount, reasons.Count);
            var heading = of is { } total ? l.Format("Digest.ReasonsOf." + side, N(total)) : l["Digest.Reasons." + side].Value;
            Line(heading);
            html.Append(SideHeading(heading.Trim(), colour, muted: false));
            for (var i = 0; i < reasons.Count; i++)
            {
                var url = ReasonLink(model, item.BillNumber, side, i + 1);
                var (body, count, cut) = Reason(reasons[i]);
                Line($"  · {body} ({count})");
                html.Append("<div style=\"margin:0 0 8px;padding:8px 10px;border:1px solid #e2e8f0;border-radius:6px;background-color:#ffffff;font-size:15px;line-height:1.5\">")
                    .Append(EmailLayout.Encode(body)).Append(" <span style=\"color:#64748b;white-space:nowrap\">(").Append(EmailLayout.Encode(count)).Append(")</span>");
                if (cut)
                {
                    Line("    " + l.Format("Digest.WholeReason", url));
                    html.Append("<br><span style=\"font-size:14px\">").Append(EmailLayout.ActionAnchor(url, l["Digest.WholeReasonLabel"])).Append("</span>");
                }

                html.Append("</div>");
            }
        }

        // One action: the response with the way to change it, or the button to answer. The bill's page is linked once in the
        // text part, as the line's address (the heading carries it in the HTML part too).
        if (bill.Response is { } response)
        {
            var stance = l["Digest.Stance." + response.Stance];
            var day = VilniusTime.DateOf(response.WrittenAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (copy)
            {
                var line = l.Format("Digest.ResponseCopy", stance, day);
                Line(line);
                html.Append("<p style=\"margin:14px 0 0;font-size:15px\">").Append(EmailLayout.Encode(line.Trim())).Append("</p>");
                Line(l.Format("Digest.AllNumbers", billLink));
            }
            else
            {
                var line = l.Format("Digest.Response", stance, day, billLink);
                Line(line);
                html.Append("<p style=\"margin:14px 0 0;font-size:15px\">").Append(EmailLayout.LinkLineHtml(line.Trim())).Append("</p>");
            }
        }
        else
        {
            Line(l.Format("Digest.Answer", billLink));
            html.Append("<div style=\"margin:14px 0 0\">").Append(EmailLayout.ButtonHtml(billLink, l["Digest.AnswerButton"], "0")).Append("</div>");
        }

        return new EmailBlock(text.ToString(), html.Append("</div>").ToString());
    }

    /// <summary>One side's figure in the pair (as the page's <c>figure</c>): the label, the number large in the side's colour, the older-than-30-days figure as a small note.</summary>
    private string Figure(string label, int count, int older, string colour, string padding) =>
        $"<td width=\"50%\" valign=\"top\" style=\"width:50%;padding:{padding}\">"
        + $"<div style=\"font-size:13px;line-height:1.3;color:#475569\">{EmailLayout.Encode(label)}</div>"
        + $"<div style=\"font-size:30px;line-height:1.2;font-weight:700;color:{colour}\">{EmailLayout.Encode(N(count))}</div>"
        + $"<div style=\"font-size:12px;line-height:1.4;color:#64748b\">{EmailLayout.Encode(l.Format("Digest.Older", N(older)))}</div></td>";

    /// <summary>The proportion bar (the page's <c>SplitBar</c>): a table whose two cells have the shares of the two sides; none when nobody has taken a side.</summary>
    private static string Bar(int forCount, int againstCount)
    {
        if (forCount + againstCount == 0)
        {
            return "";
        }

        var forShare = forCount == 0 ? 0 : againstCount == 0 ? 100 : Math.Clamp((int)Math.Round(forCount * 100.0 / (forCount + againstCount)), 1, 99);
        static string Cell(int share, string colour) => share == 0
            ? ""
            : $"<td width=\"{share}%\" height=\"8\" style=\"width:{share}%;height:8px;background-color:{colour};font-size:0;line-height:0\">&nbsp;</td>";
        return "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;margin:0 0 10px\"><tr>"
            + Cell(forShare, ForColour) + Cell(100 - forShare, AgainstColour) + "</tr></table>";
    }

    private static string SideHeading(string text, string colour, bool muted) =>
        $"<div style=\"margin:16px 0 8px;padding-left:10px;border-left:4px solid {colour};font-size:15px;line-height:1.4;font-weight:{(muted ? "400" : "600")};color:{(muted ? "#64748b" : "#1e293b")}\">{EmailLayout.Encode(text)}</div>";

    /// <summary>
    /// One argument in the email (design 8.19.3, owner 2026-11-16): its beginning by the same rule as the page
    /// (<see cref="TextExcerpt"/>, about 300 characters) with "…", the "Man svarbu" count, and whether it was cut: then the text
    /// part adds a plain line "Visas argumentas: {link}" and the HTML part the link "Skaityti visą", to it on the summary's public
    /// copy, which shows the snapshot's whole text. The snapshot itself keeps the whole text.
    /// </summary>
    private static (string Body, string Count, bool Cut) Reason(DigestReason reason)
    {
        var excerpt = TextExcerpt.Of(reason.Text, TextExcerpt.ArgumentShown, TextExcerpt.ArgumentMargin);
        return excerpt.Rest is null
            ? (reason.Text, N(reason.Endorsements), false)
            : (excerpt.Shown + "…", N(reason.Endorsements), true);
    }

    /// <summary>The argument on the public copy: at its own place when the run is known, else the copy's page.</summary>
    private string ReasonLink(MpDigestEmail model, string number, string side, int place) => Link(model.RunId is { } run
        ? MpMailPaths.ReasonOnPublicCopy(model.RunDate, run, number, side, place)
        : model.PublicCopyPath);

    /// <summary>The sign-off of a summary and a notice: "Lietuva 2.0", in semibold with room above.</summary>
    private EmailBlock Signature() => new(l[EmailText.Signature], EmailLayout.SignatureHtml(l[EmailText.Signature]));

    /// <summary>
    /// What ends every MP email (design 8.19.3, 8.19.5): what the numbers are and are not and the public copy (summaries only),
    /// then the sign-off "Lietuva 2.0" (not in the introduction letter, whose signer signs it), then below it what the system adds, set apart by a rule: the sentence on taking over the profile (the MP's own address) as a short
    /// lead line and a button, and the preferences link as a quiet link, or a copy's own stop link.
    /// </summary>
    private IEnumerable<EmailBlock> Closing(string? preferencesPath, string? copyStopPath, string? publicCopyPath = null, bool signOff = true)
    {
        if (publicCopyPath is not null)
        {
            var publicCopy = l.Format("Digest.PublicCopy", Link(publicCopyPath));
            yield return new EmailBlock(
                l["Digest.NotAPoll"] + "\n" + publicCopy,
                $"<div style=\"margin:0 0 12px;padding:10px 12px;border:1px solid #cbd5e1;border-radius:6px;background-color:#f8fafc;font-size:14px;line-height:1.5;color:#475569\">{EmailLayout.Encode(l["Digest.NotAPoll"])}</div>"
                + $"<p style=\"{EmailLayout.ParagraphStyle}\">{EmailLayout.ActionAnchor(Link(publicCopyPath), l["Digest.PublicCopyLabel"])}</p>");
        }

        if (signOff)
        {
            yield return Signature();
        }

        if (preferencesPath is not null)
        {
            var preferences = l.Format("Digest.Preferences", Link(preferencesPath));
            yield return new EmailBlock(
                preferences + "\n" + l.Format("Digest.Takeover", Link(MpMailPaths.SignIn)),
                EmailLayout.SystemHtml(l["Digest.TakeoverLead"], l["Digest.TakeoverButton"], Link(MpMailPaths.SignIn), preferences));
        }

        if (copyStopPath is not null)
        {
            var stop = l.Format("Digest.CopyStop", Link(copyStopPath));
            yield return new EmailBlock(stop, EmailLayout.SystemHtml(null, null, null, stop));
        }
    }

    /// <summary>
    /// <c>List-Unsubscribe</c> (design 8.19.3): the MP's one-click stop with <c>List-Unsubscribe-Post</c> (RFC 8058); a copy's
    /// own page without it, since the one-click form is the MP's one stop that needs no same-origin check.
    /// </summary>
    private Dictionary<string, string>? Headers(string? unsubscribePath, string? copyStopPath)
    {
        if (unsubscribePath is not null)
        {
            return new Dictionary<string, string>
            {
                [ListUnsubscribe] = "<" + Link(unsubscribePath) + ">",
                [ListUnsubscribePost] = "List-Unsubscribe=One-Click",
            };
        }

        return copyStopPath is null ? null : new Dictionary<string, string> { [ListUnsubscribe] = "<" + Link(copyStopPath) + ">" };
    }

    /// <summary>The letter's text as plain paragraphs: separated by an empty line, single line breaks kept.</summary>
    private string Link(string path) => SiteLinks.Absolute(site.Value.Address, path);

    private static string Capital(string value) => value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.GetCultureInfo("lt-LT")) + value[1..];

    private static string N(int value) => value.ToString("#,0", new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = [3] });
}
