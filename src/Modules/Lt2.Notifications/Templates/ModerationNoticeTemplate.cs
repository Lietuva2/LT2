using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The statement of reasons to an author (design 8.3, 8.6): what a moderator did with their argument, and why.
/// It names the bill, never a side, and carries no tracking.
/// </summary>
internal sealed class ModerationNoticeTemplate(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(ModerationNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!ModerationNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown moderation notice '{model.Notice}'.");
        }

        var bill = $"{model.BillNumber} · {model.BillTitle}";
        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
            l.Format($"Moderation.{model.Notice}.Body", bill, SiteLinks.Absolute(site.Value.Address, model.Path ?? $"/seimas/{Uri.EscapeDataString(model.BillNumber)}")),
        };
        if (!string.IsNullOrWhiteSpace(model.Excerpt))
        {
            paragraphs.Add(l.Format(model.Notice.StartsWith("comment-", StringComparison.Ordinal) ? "Moderation.CommentExcerpt" : "Moderation.Excerpt", model.Excerpt));
        }

        if (!string.IsNullOrWhiteSpace(model.Statement))
        {
            paragraphs.Add(l.Format(EmailText.Statement, model.Statement));
        }

        if (!string.IsNullOrWhiteSpace(model.Contact))
        {
            paragraphs.Add(l.Format(EmailText.Contact, model.Contact));
        }

        paragraphs.Add(l["Moderation.Rules"]);
        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.Build(model.To, l.Format($"Moderation.{model.Notice}.Subject", model.BillNumber), paragraphs, EmailLayout.Footer(l, site.Value.Address));
    }
}
