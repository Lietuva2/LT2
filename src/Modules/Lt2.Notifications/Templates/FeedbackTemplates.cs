using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Notifications.Outbox;
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
        return Message(
            model.To,
            l["Feedback.Reply.Subject"],
            [
                l["SignInCode.Greeting"],
                l["Feedback.Reply.Body"],
                string.Format(CultureInfo.InvariantCulture, l["Feedback.Reply.Yours"], model.Excerpt),
                string.Format(CultureInfo.InvariantCulture, l["Feedback.Reply.Answer"], model.Reply),
                l["SignInCode.Signature"],
            ]);
    }

    public EmailMessage Notice(FeedbackNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Message(
            model.To,
            string.Format(CultureInfo.InvariantCulture, l["Feedback.Notice.Subject"], model.Count),
            [string.Format(CultureInfo.InvariantCulture, l["Feedback.Notice.Body"], model.Count, SiteLinks.Absolute(site.Value.Address, model.StaffPath))]);
    }

    private static EmailMessage Message(string to, string subject, IReadOnlyList<string> paragraphs)
    {
        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));
        return new EmailMessage(
            to,
            subject,
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>",
            EmailCategory.Transactional);
    }
}

internal sealed class FeedbackReplyEmailHandler(FeedbackTemplates templates, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.FeedbackReplyEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken) =>
        transport.SendAsync(templates.Reply(payload.Deserialize<FeedbackReplyEmail>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Empty payload.")), cancellationToken);
}

internal sealed class FeedbackNoticeEmailHandler(FeedbackTemplates templates, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.FeedbackNoticeEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken) =>
        transport.SendAsync(templates.Notice(payload.Deserialize<FeedbackNoticeEmail>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Empty payload.")), cancellationToken);
}
