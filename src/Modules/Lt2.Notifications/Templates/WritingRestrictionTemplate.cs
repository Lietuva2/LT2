using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;

namespace Lt2.Notifications.Templates;

/// <summary>
/// A moderator restricted the person from writing, or lifted that (design 8.3): the statement, the end ("until" a day or
/// until lifted), what still works, and where to object. Never a side; no tracking.
/// </summary>
internal sealed class WritingRestrictionTemplate(IStringLocalizer<EmailTemplates> l)
{
    public EmailMessage Render(WritingRestrictionEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!WritingRestrictionNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown writing restriction notice '{model.Notice}'.");
        }

        var paragraphs = new List<string> { l["SignInCode.Greeting"], l[$"Restriction.{model.Notice}.Body"] };
        if (model.Notice == WritingRestrictionNotices.Restricted)
        {
            paragraphs.Add(model.Until is { } until
                ? string.Format(CultureInfo.InvariantCulture, l["Restriction.Until"], until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                : l["Restriction.UntilLifted"]);
            paragraphs.Add(l["Restriction.Untouched"]);
        }

        paragraphs.Add(string.Format(CultureInfo.InvariantCulture, l["Moderation.Statement"], model.Statement));
        if (model.Notice == WritingRestrictionNotices.Restricted && !string.IsNullOrWhiteSpace(model.Contact))
        {
            paragraphs.Add(string.Format(CultureInfo.InvariantCulture, l["Restriction.Contact"], model.Contact));
        }

        paragraphs.Add(l["SignInCode.Signature"]);
        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));
        return new EmailMessage(
            model.To,
            l[$"Restriction.{model.Notice}.Subject"],
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>",
            EmailCategory.Transactional);
    }
}

internal sealed class WritingRestrictionEmailHandler(WritingRestrictionTemplate template, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.WritingRestrictionEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var model = payload.Deserialize<WritingRestrictionEmail>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty writing restriction payload.");
        return transport.SendAsync(template.Render(model), cancellationToken);
    }
}
