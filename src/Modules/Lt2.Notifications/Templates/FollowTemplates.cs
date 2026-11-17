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
        paragraphs.Add(Stop(Format("Follow.Footer", Link(model.AccountPath)), model.StopToken));
        paragraphs.Add(l[EmailText.Signature]);
        return Message(model.To, Format("Follow.Planned.Subject", model.BillNumber, day), paragraphs, model.StopToken);
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
        paragraphs.Add(Stop(Format("Follow.Footer", Link(model.AccountPath)), model.StopToken));
        paragraphs.Add(l[EmailText.Signature]);
        return Message(model.To, Format("Follow.Voted.Subject", model.BillNumber), paragraphs, model.StopToken);
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
        };
        if (model.Day is { } published)
        {
            paragraphs.Add(Format("Follow.PublishedOn", Day(published)));
        }

        paragraphs.AddRange(
        [
            Format("Follow.Statement.Read", Link(model.BillPath)),
            Stop(Format("Follow.Statement.Footer", Link(model.AccountPath)), model.StopToken),
            l[EmailText.Signature],
        ]);
        return Message(model.To, Format("Follow.Statement.Subject", model.OrganisationName, model.BillNumber), paragraphs, model.StopToken);
    }

    /// <summary>
    /// MPs answered on a bill the person follows (design 8.20.1): in the person's rhythm (design 8.24), one part per bill
    /// with every answer not told yet (at once, the morning from Follows:ResponsesAt, or Monday). Each MP's
    /// name (nominative), faction and stance, and the link to the bill's part "Seimo narių atsakymai". Never an explanation's
    /// text, the person's side, a count or anyone else.
    /// </summary>
    public EmailMessage Responses(FollowResponsesEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var lines = model.Responses.Select(r => r.Faction is { Length: > 0 } faction
            ? Format("Follow.Responses.MpWithFaction", r.Name, faction, l["Follow.Responses.Stance." + r.Stance])
            : Format("Follow.Responses.Mp", r.Name, l["Follow.Responses.Stance." + r.Stance]));
        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
            Format("Follow.Responses.Body", $"{model.BillNumber} · {model.BillTitle}", Day(model.Day)) + "\n" + string.Join("\n", lines),
            Format("Follow.Responses.Read", Link(model.BillPath)),
            Stop(Format("Follow.Footer", Link(model.AccountPath)), model.StopToken),
            l[EmailText.Signature],
        };
        return Message(model.To, Format("Follow.Responses.Subject", model.BillNumber), paragraphs, model.StopToken);
    }

    /// <summary>
    /// A member the person follows published an argument (design 8.25.5): the member's display name, the bill and the link to
    /// the argument and to the member's page. Never the argument's text, its side, the person's side or any count.
    /// </summary>
    public EmailMessage Member(FollowMemberEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
            Format("Follow.Member.Body." + What(model.What), model.MemberName, $"{model.BillNumber} · {model.BillTitle}"),
        };
        if (model.Day is { } published)
        {
            paragraphs.Add(Format("Follow.PublishedOn", Day(published)));
        }

        paragraphs.AddRange(
        [
            Format("Follow.Member.Read." + What(model.What), Link(model.TextPath)),
            Format("Follow.Member.Profile", Link(model.ProfilePath)),
            Stop(Format("Follow.Member.Footer", Link(model.AccountPath)), model.StopToken),
            l[EmailText.Signature],
        ]);
        return Message(model.To, Format("Follow.Member.Subject." + What(model.What), model.MemberName, model.BillNumber), paragraphs, model.StopToken);
    }

    /// <summary>The "planned" part of a gathered email (design 8.24): a line per bill with the date current when the email was made.</summary>
    public string PlannedPart(IReadOnlyList<FollowPlannedEmail> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = items.Select(m => Format(
            "Gathered.Planned.Line",
            $"{m.BillNumber} · {m.BillTitle}",
            m.Stage is { } stage ? Format("Gathered.Planned.DateStage", Day(m.Date), l["Stage." + stage]) : Day(m.Date),
            Link(m.BillPath)));
        return l["Gathered.Planned.Title"] + "\n" + string.Join("\n", lines) + "\n" + l["Follow.Planned.Moves"];
    }

    /// <summary>The "voted" part: a line per vote with its day, the result, the official note and how the followed MPs voted.</summary>
    public string VotedPart(IReadOnlyList<FollowVotedEmail> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = items.Select(m =>
        {
            var result = m.IsConsensus ? (string)l["Follow.Voted.Consensus"] : Format("Follow.Voted.Result", Number(m.For), Number(m.Against), Number(m.Abstain));
            var line = Format(
                "Gathered.Voted.Line",
                Day(m.Day),
                $"{m.BillNumber} · {m.BillTitle}" + (m.Stage is { } stage ? " (" + l["Stage." + stage] + ")" : ""),
                result,
                Link(m.VotePath));
            if (!string.IsNullOrWhiteSpace(m.OfficialNote))
            {
                line += "\n  " + Format("Follow.Voted.Note", m.OfficialNote);
            }

            return m.Mps.Count == 0
                ? line
                : line + "\n  " + l["Follow.Voted.Mps"] + "\n" + string.Join("\n", m.Mps.Select(c => "  " + Format("Follow.Voted.Mp", c.Name, l["Choice." + (c.Choice ?? "none")])));
        });
        return l["Gathered.Voted.Title"] + "\n" + string.Join("\n", lines);
    }

    /// <summary>The statements' part: a line per statement with the day it became public, the organisation and its label, and the stance.</summary>
    public string StatementPart(IReadOnlyList<FollowStatementEmail> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = items.Select(m => Format(
            "Gathered.Statement.Line",
            m.Day is { } day ? Day(day) : "",
            m.OrganisationName,
            Label(m.ConfirmedBy, m.ConfirmedDomain),
            $"{m.BillNumber} · {m.BillTitle}",
            l["Follow.Stance." + m.Stance],
            Link(m.BillPath)).TrimStart());
        return l["Gathered.Statement.Title"] + "\n" + string.Join("\n", lines);
    }

    /// <summary>The answers' part: per bill, the day and the link, then each MP with faction and stance; never an explanation.</summary>
    public string ResponsesPart(IReadOnlyList<FollowResponsesEmail> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = items.Select(m => Format("Gathered.Responses.Line", Day(m.Day), $"{m.BillNumber} · {m.BillTitle}", Link(m.BillPath)) + "\n"
            + string.Join("\n", m.Responses.Select(r => "  " + (r.Faction is { Length: > 0 } faction
                ? Format("Follow.Responses.MpWithFaction", r.Name, faction, l["Follow.Responses.Stance." + r.Stance])
                : Format("Follow.Responses.Mp", r.Name, l["Follow.Responses.Stance." + r.Stance])))));
        return l["Gathered.Responses.Title"] + "\n" + string.Join("\n", lines);
    }

    /// <summary>The followed members' part (design 8.25.5): per argument the member's name, the bill and the link; never the text or its side.</summary>
    public string MemberPart(IReadOnlyList<FollowMemberEmail> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = items.Select(m => Format("Gathered.Member.Line." + What(m.What), m.MemberName, $"{m.BillNumber} · {m.BillTitle}", Link(m.TextPath), m.Day is { } day ? Day(day) : "").Replace("–  ", "– ", StringComparison.Ordinal));
        return l["Gathered.Member.Title"] + "\n" + string.Join("\n", lines);
    }

    /// <summary>
    /// The comments' part (design 8.23.9, 8.24): a line per argument of the person's that got comments, the bill and the link.
    /// Never a comment's text, its writer, how many, or anything about "Sutinku".
    /// </summary>
    public string CommentsPart(IReadOnlyList<CommentNoticeEmail> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        // One line per argument (design 8.23.9), with the latest day if two things in one email name it.
        var lines = items.SelectMany(m => m.Arguments)
            .GroupBy(a => a.Path, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(a => a.Day).First())
            .Select(a => "– " + CommentLine(a));
        return l["Gathered.Comments.Title"] + "\n" + string.Join("\n", lines);
    }

    /// <summary>A followed member's text: a comment, or an argument (also for anything unknown).</summary>
    private static string What(string? what) => what == MemberTextKinds.Comment ? MemberTextKinds.Comment : MemberTextKinds.Argument;

    private string Label(string? confirmedBy, string? domain) => confirmedBy switch
    {
        "domain" => Format("Follow.OrgLabel.domain", domain ?? ""),
        "moderator" => l["Follow.OrgLabel.moderator"],
        _ => l["Follow.OrgLabel.none"],
    };

    /// New comments under the person's own arguments (design 8.23.9): one line per argument (the bill and the link to the
    /// argument). Never a comment's text, its writer, how many, or anything about "Sutinku".
    /// </summary>
    public EmailMessage Comments(CommentNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var lines = model.Arguments.Select(CommentLine);
        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
            l[model.Arguments.Count == 1 ? "Comments.Notice.Body.One" : "Comments.Notice.Body.Many"] + "\n" + string.Join("\n", lines),
            Stop(Format(model.Arguments.Count == 1 ? "Comments.Notice.Footer.One" : "Comments.Notice.Footer.Many", Link(model.AccountPath)), model.StopToken),
            l[EmailText.Signature],
        };
        return Message(model.To, l["Comments.Notice.Subject"], paragraphs, model.StopToken);
    }

    private string CommentLine(CommentedArgumentLine a) =>
        Format("Comments.Notice.Line", $"{a.BillNumber} · {a.BillTitle}", Link(a.Path), a.Day is { } day ? Day(day) : "").TrimStart();

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "–";

    private string Link(string path) => SiteLinks.Absolute(site.Value.Address, path);

    private string Format(string key, params object?[] values) => l.Format(key, values);

    /// <summary>The account footer line with the stop link under it (design 8.24.1).</summary>
    private string Stop(string line, string? stopToken) => EmailLayout.WithStopLine(l, site.Value.Address, line, stopToken);

    /// <summary>A member's email with the stop link's headers (design 8.24.1).</summary>
    private EmailMessage Message(string to, string subject, IReadOnlyList<string> paragraphs, string? stopToken) =>
        EmailLayout.Build(to, subject, paragraphs, EmailLayout.Footer(l, site.Value.Address), headers: EmailLayout.StopHeaders(site.Value.Address, stopToken));
}