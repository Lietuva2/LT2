using System.Globalization;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The take-over link of an MP's profile (design 8.5.2), to the official address only: a full address
/// (<see cref="SiteLinks.Absolute"/>) whose code is after the <c>#</c>, so no server log ever holds it, and the way without the
/// link (owner, 2026-11-17): the page to open and the code to type there; no tracking. Sent with a registration (design 8.5.2),
/// the same link in the words of a registration that it also completes.
/// </summary>
internal sealed class MpTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(MpTakeoverLinkEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Design 8.5.2: sent with a registration, the same link also completes the registration, in its own words.
        var keys = model.AtRegistration ? "Mp.Registration" : "Mp.Takeover";
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l.Format(keys + ".Intro", model.MpName),
            EmailLayout.LinkLine(l[keys + ".Link"], SiteLinks.Absolute(site.Value.Address, model.Path)),
            l.Format(EmailText.WithoutLink, SiteLinks.Absolute(site.Value.Address, model.PagePath)),
            model.Code,
            l.Format(keys + ".Validity", model.ValidHours.ToString(CultureInfo.InvariantCulture)),
            l[keys + ".What"],
            l[keys + ".Ignore"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l[keys + ".Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address), highlighted: 4);
    }

    /// <summary>
    /// An invitation to an MP's office (design 8.5.3, owner 2026-11-17), to the address the MP's account typed: who invites and
    /// what an office member does, "Priimti kvietimą" as a link, and the way without the link: the page's address to open
    /// and the code to type there. Nothing tracked; the address and the code exist only in the outbox row until it is sent.
    /// </summary>
    public EmailMessage OfficeInvitation(MpOfficeInvitationEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var until = TimeZoneInfo.ConvertTime(model.ExpiresAt, VilniusTime.Zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l.Format("Mp.Office.Intro", model.MpName),
            l["Mp.Office.What"],
            EmailLayout.LinkLine(l["Mp.Office.Accept"], SiteLinks.Absolute(site.Value.Address, model.Path)),
            l.Format(EmailText.WithoutLink, SiteLinks.Absolute(site.Value.Address, model.PagePath)),
            model.Code,
            l.Format("Mp.Office.Validity", until),
            l["Mp.Office.Ignore"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l["Mp.Office.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address), highlighted: 5);
    }

    /// <summary>
    /// A moderator hid or restored the MP's response on a bill (design 8.5.5): the bill, the statement of reasons (shown in the
    /// response's place too) and the moderators' contact. Never who wrote the response.
    /// </summary>
    public EmailMessage Response(MpResponseNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!MpResponseNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown response notice '{model.Notice}'.");
        }

        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
            l.Format($"Mp.{model.Notice}.Body", model.MpName, $"{model.BillNumber} · {model.BillTitle}", SiteLinks.Absolute(site.Value.Address, model.Path)),
        };
        if (!string.IsNullOrWhiteSpace(model.Statement))
        {
            paragraphs.Add(l.Format(EmailText.Statement, model.Statement));
        }

        if (!string.IsNullOrWhiteSpace(model.Contact))
        {
            paragraphs.Add(l.Format(EmailText.Contact, model.Contact));
        }

        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.Build(model.To, l[$"Mp.{model.Notice}.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address));
    }
}
