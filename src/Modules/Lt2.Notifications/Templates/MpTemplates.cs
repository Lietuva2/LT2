using System.Globalization;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The take-over link of an MP's profile (design 8.5.2), to the official address only: a full address
/// (<see cref="SiteLinks.Absolute"/>) whose token is after the <c>#</c>, so no server log ever holds it; no tracking.
/// </summary>
internal sealed class MpTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(MpTakeoverLinkEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l.Format("Mp.Takeover.Intro", model.MpName),
            SiteLinks.Absolute(site.Value.Address, model.Path),
            l.Format("Mp.Takeover.Validity", model.ValidHours.ToString(CultureInfo.InvariantCulture)),
            l["Mp.Takeover.What"],
            l["Mp.Takeover.Ignore"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l["Mp.Takeover.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address));
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
