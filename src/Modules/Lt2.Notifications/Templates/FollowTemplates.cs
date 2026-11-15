using System.Globalization;
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
            l[EmailText.Greeting],
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
        paragraphs.Add(l[EmailText.Signature]);
        return Message(model.To, Format("Follow.Planned.Subject", model.BillNumber, day), paragraphs);
    }

    public EmailMessage Voted(FollowVotedEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
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
        paragraphs.Add(l[EmailText.Signature]);
        return Message(model.To, Format("Follow.Voted.Subject", model.BillNumber), paragraphs);
    }

    /// <summary>
    /// An organisation the person follows made its statement on a bill public (design 8.14.14, stage B): the organisation
    /// with its label, the bill, the stance and a link to the bill's page. Never the statement's text, a side or a count.
    /// </summary>
    public EmailMessage Statement(FollowStatementEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var label = model.ConfirmedBy switch
        {
            "domain" => Format("Follow.OrgLabel.domain", model.ConfirmedDomain ?? ""),
            "moderator" => (string)l["Follow.OrgLabel.moderator"],
            _ => (string)l["Follow.OrgLabel.none"],
        };
        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
            Format("Follow.Statement.Body", model.OrganisationName, $"{model.BillNumber} · {model.BillTitle}", label),
            Format("Follow.Statement.Stance", l["Follow.Stance." + model.Stance]),
            Format("Follow.Statement.Read", Link(model.BillPath)),
            Format("Follow.Statement.Footer", Link(model.AccountPath)),
            l[EmailText.Signature],
        };
        return Message(model.To, Format("Follow.Statement.Subject", model.OrganisationName, model.BillNumber), paragraphs);
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "–";

    private string Link(string path) => SiteLinks.Absolute(site.Value.Address, path);

    private string Format(string key, params object?[] values) => l.Format(key, values);

    private static EmailMessage Message(string to, string subject, IReadOnlyList<string> paragraphs) => EmailLayout.Build(to, subject, paragraphs);
}