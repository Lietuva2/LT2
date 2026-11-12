using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;

namespace Lt2.Notifications.Templates;

/// <summary>Marker type for Resources/EmailTemplates.resx.</summary>
internal sealed class EmailTemplates;

/// <summary>The sign-in code email, in Lithuanian. The same text serves registration and sign-in, so it reveals nothing.</summary>
internal sealed class SignInCodeTemplate(IStringLocalizer<EmailTemplates> l)
{
    public EmailMessage Render(SignInCodeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var minutes = model.ValidMinutes.ToString(CultureInfo.InvariantCulture);
        string[] paragraphs =
        [
            l["SignInCode.Greeting"],
            l["SignInCode.Intro"],
            model.Code,
            string.Format(CultureInfo.InvariantCulture, l["SignInCode.Validity"], minutes),
            l["SignInCode.NewestOnly"],
            l["SignInCode.Ignore"],
            l["SignInCode.Signature"],
        ];

        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select((p, i) => i == 2
            ? $"<p style=\"font-size:28px;font-weight:700;letter-spacing:4px\">{WebUtility.HtmlEncode(p)}</p>"
            : $"<p>{WebUtility.HtmlEncode(p)}</p>"));

        return new EmailMessage(
            model.To,
            string.Format(CultureInfo.InvariantCulture, l["SignInCode.Subject"], model.Code),
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>",
            EmailCategory.Transactional);
    }
}

internal sealed class SignInCodeEmailHandler(SignInCodeTemplate template, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.SignInCodeEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var model = payload.Deserialize<SignInCodeEmail>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty sign-in code payload.");
        return transport.SendAsync(template.Render(model), cancellationToken);
    }
}
