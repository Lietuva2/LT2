using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>Feedback emails (design 8.10): the team's reply to a member, and the daily notice to the team (a count, never text).</summary>
internal sealed class FeedbackTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Reply(FeedbackReplyEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return EmailLayout.Build(
            model.To,
            l["Feedback.Reply.Subject"],
            [
                l[EmailText.Greeting],
                l["Feedback.Reply.Body"],
                l.Format("Feedback.Reply.Yours", model.Excerpt),
                l.Format("Feedback.Reply.Answer", model.Reply),
                l[EmailText.Signature],
            ],
            EmailLayout.Footer(l, site.Value.Address));
    }

    public EmailMessage Notice(FeedbackNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return EmailLayout.Build(
            model.To,
            l.Format("Feedback.Notice.Subject", model.Count),
            [l.Format("Feedback.Notice.Body", model.Count, SiteLinks.Absolute(site.Value.Address, model.StaffPath))],
            EmailLayout.Footer(l, site.Value.Address));
    }
}
