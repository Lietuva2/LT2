using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The email telling a person that something security-relevant changed on their account (design 6.2): a passkey added,
/// a staff role granted or activated, an account reset. It never carries a code or a link that acts.
/// </summary>
internal sealed class AccountNoticeTemplate(IStringLocalizer<EmailTemplates> l)
{
    public EmailMessage Render(AccountNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!AccountNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown account notice '{model.Notice}'.");
        }

        var role = model.Role is null ? "" : l["Role." + model.Role].Value;
        var body = string.Format(CultureInfo.InvariantCulture, l[$"Notice.{model.Notice}.Body"], role);
        var paragraphs = new List<string> { l["SignInCode.Greeting"], body };
        if (model.PasskeysRemoved > 0)
        {
            paragraphs.Add(string.Format(CultureInfo.InvariantCulture, l["Notice.PasskeysRemoved"], model.PasskeysRemoved));
        }

        paragraphs.Add(l["Notice.NotYou"]);
        paragraphs.Add(l["SignInCode.Signature"]);

        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));
        return new EmailMessage(
            model.To,
            l[$"Notice.{model.Notice}.Subject"],
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>",
            EmailCategory.Transactional);
    }
}

internal sealed class AccountNoticeEmailHandler(AccountNoticeTemplate template, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.AccountNoticeEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var model = payload.Deserialize<AccountNoticeEmail>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty account notice payload.");
        return transport.SendAsync(template.Render(model), cancellationToken);
    }
}
