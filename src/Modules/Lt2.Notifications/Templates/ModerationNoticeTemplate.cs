using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Notifications.Outbox;
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
            l["SignInCode.Greeting"],
            string.Format(CultureInfo.InvariantCulture, l[$"Moderation.{model.Notice}.Body"], bill, SiteLinks.Absolute(site.Value.Address, model.Path ?? $"/seimas/{Uri.EscapeDataString(model.BillNumber)}")),
        };
        if (!string.IsNullOrWhiteSpace(model.Excerpt))
        {
            paragraphs.Add(string.Format(CultureInfo.InvariantCulture, l["Moderation.Excerpt"], model.Excerpt));
        }

        if (!string.IsNullOrWhiteSpace(model.Statement))
        {
            paragraphs.Add(string.Format(CultureInfo.InvariantCulture, l["Moderation.Statement"], model.Statement));
        }

        paragraphs.Add(l["Moderation.Rules"]);
        paragraphs.Add(l["SignInCode.Signature"]);

        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));
        return new EmailMessage(
            model.To,
            string.Format(CultureInfo.InvariantCulture, l[$"Moderation.{model.Notice}.Subject"], model.BillNumber),
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>",
            EmailCategory.Transactional);
    }
}

internal sealed class ModerationNoticeEmailHandler(ModerationNoticeTemplate template, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.ModerationNoticeEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var model = payload.Deserialize<ModerationNoticeEmail>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty moderation notice payload.");
        return transport.SendAsync(template.Render(model), cancellationToken);
    }
}
