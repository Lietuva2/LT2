using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The emails of organisations (design 8.14.14): the code that confirms an organisation by its email domain, and what a
/// moderator did with an organisation or its statement, to each representative. Links are full addresses
/// (<see cref="SiteLinks.Absolute"/>); nothing names another representative or a person's side; no tracking.
/// </summary>
internal sealed class OrganisationTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(OrganisationCodeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string[] paragraphs =
        [
            l["SignInCode.Greeting"],
            string.Format(CultureInfo.InvariantCulture, l["Organisation.Code.Intro"], model.OrganisationName, model.Domain),
            model.Code,
            string.Format(CultureInfo.InvariantCulture, l["SignInCode.Validity"], model.ValidMinutes.ToString(CultureInfo.InvariantCulture)),
            string.Format(CultureInfo.InvariantCulture, l["Organisation.Code.What"], model.OrganisationName, model.Domain),
            l["Organisation.Code.Ignore"],
            l["SignInCode.Signature"],
        ];
        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select((p, i) => i == 2
            ? $"<p style=\"font-size:28px;font-weight:700;letter-spacing:4px\">{WebUtility.HtmlEncode(p)}</p>"
            : $"<p>{WebUtility.HtmlEncode(p)}</p>"));
        return new EmailMessage(
            model.To,
            string.Format(CultureInfo.InvariantCulture, l["Organisation.Code.Subject"], model.Code),
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>",
            EmailCategory.Transactional);
    }

    public EmailMessage Render(OrganisationNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!OrganisationNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown organisation notice '{model.Notice}'.");
        }

        var link = SiteLinks.Absolute(site.Value.Address, model.Path);
        var bill = model.BillNumber is { } number ? $"{number} · {model.BillTitle ?? number}" : "";
        var paragraphs = new List<string>
        {
            l["SignInCode.Greeting"],
            string.Format(CultureInfo.InvariantCulture, l[$"Organisation.{model.Notice}.Body"], model.OrganisationName, bill, link),
        };
        if (!string.IsNullOrWhiteSpace(model.Statement))
        {
            paragraphs.Add(string.Format(CultureInfo.InvariantCulture, l["Moderation.Statement"], model.Statement));
        }

        if (!string.IsNullOrWhiteSpace(model.Contact))
        {
            paragraphs.Add(string.Format(CultureInfo.InvariantCulture, l["Restriction.Contact"], model.Contact));
        }

        paragraphs.Add(l["Organisation.Why"]);
        paragraphs.Add(l["SignInCode.Signature"]);
        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));
        return new EmailMessage(
            model.To,
            string.Format(CultureInfo.InvariantCulture, l[$"Organisation.{model.Notice}.Subject"], model.OrganisationName),
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>",
            EmailCategory.Transactional);
    }
}

internal sealed class OrganisationCodeEmailHandler(OrganisationTemplates template, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.OrganisationCodeEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var model = payload.Deserialize<OrganisationCodeEmail>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty organisation code payload.");
        return transport.SendAsync(template.Render(model), cancellationToken);
    }
}

internal sealed class OrganisationNoticeEmailHandler(OrganisationTemplates template, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.OrganisationNoticeEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var model = payload.Deserialize<OrganisationNoticeEmail>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Empty organisation notice payload.");
        return transport.SendAsync(template.Render(model), cancellationToken);
    }
}
