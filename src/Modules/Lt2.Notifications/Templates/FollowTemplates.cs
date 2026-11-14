using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The follow emails of design 8.11.2 (planned, voted). Each is about the bill and the Seimas only: never the person's
/// side, a bill's counts or anyone else. No tracking; links are the site's own pages, with the full address when
/// <c>Site:Address</c> is configured.
/// </summary>
internal sealed class FollowTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Planned(FollowPlannedEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var day = Day(model.Date);
        var paragraphs = new List<string>
        {
            l["SignInCode.Greeting"],
            Format("Follow.Planned.Body", $"{model.BillNumber} · {model.BillTitle}"),
        };
        if (model.Stage is { } stage)
        {
            paragraphs.Add(Format("Follow.Stage", l["Stage." + stage]));
        }

        paragraphs.Add(Format("Follow.Planned.Date", day));
        paragraphs.Add(l["Follow.Planned.Moves"]);
        paragraphs.Add(Format("Follow.BillLink", Link(model.BillPath)));
        paragraphs.Add(Format("Follow.Footer", Link(model.AccountPath)));
        paragraphs.Add(l["SignInCode.Signature"]);
        return Message(model.To, Format("Follow.Planned.Subject", model.BillNumber, day), paragraphs);
    }

    public EmailMessage Voted(FollowVotedEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var paragraphs = new List<string>
        {
            l["SignInCode.Greeting"],
            Format("Follow.Voted.Body", $"{model.BillNumber} · {model.BillTitle}", Day(model.Day)),
        };
        if (model.Stage is { } stage)
        {
            paragraphs.Add(Format("Follow.Stage", l["Stage." + stage]));
        }

        paragraphs.Add(model.IsConsensus
            ? l["Follow.Voted.Consensus"]
            : Format("Follow.Voted.Result", Number(model.For), Number(model.Against), Number(model.Abstain)));
        if (!string.IsNullOrWhiteSpace(model.OfficialNote))
        {
            paragraphs.Add(Format("Follow.Voted.Note", model.OfficialNote));
        }

        if (model.Mps.Count > 0)
        {
            paragraphs.Add(l["Follow.Voted.Mps"] + "\n" + string.Join("\n", model.Mps.Select(m => Format("Follow.Voted.Mp", m.Name, l["Choice." + (m.Choice ?? "none")]))));
        }

        paragraphs.Add(Format("Follow.VoteLink", Link(model.VotePath)));
        paragraphs.Add(Format("Follow.Footer", Link(model.AccountPath)));
        paragraphs.Add(l["SignInCode.Signature"]);
        return Message(model.To, Format("Follow.Voted.Subject", model.BillNumber), paragraphs);
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "–";

    private string Link(string path) => SiteLinks.Absolute(site.Value.Address, path);

    private string Format(string key, params object?[] values) => string.Format(CultureInfo.InvariantCulture, l[key], values);

    private static EmailMessage Message(string to, string subject, IReadOnlyList<string> paragraphs)
    {
        var text = string.Join("\n\n", paragraphs) + "\n";
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p).Replace("\n", "<br>", StringComparison.Ordinal)}</p>"));
        return new EmailMessage(
            to,
            subject,
            text,
            $"<!doctype html><html lang=\"lt\"><body style=\"font-family:system-ui,sans-serif;color:#1e293b\">{html}</body></html>",
            EmailCategory.Transactional);
    }
}

internal sealed class FollowPlannedEmailHandler(FollowTemplates templates, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.FollowPlannedEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken) =>
        transport.SendAsync(templates.Planned(payload.Deserialize<FollowPlannedEmail>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Empty payload.")), cancellationToken);
}

internal sealed class FollowVotedEmailHandler(FollowTemplates templates, IEmailTransport transport) : IOutboxHandler
{
    public string Kind => MessageKinds.FollowVotedEmail;

    public Task HandleAsync(JsonElement payload, CancellationToken cancellationToken) =>
        transport.SendAsync(templates.Voted(payload.Deserialize<FollowVotedEmail>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Empty payload.")), cancellationToken);
}
