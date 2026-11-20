using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The decision on a member's report, to the member who reported (owner, 2026-11-17; Digital Services Act art. 16(5)): which
/// text (its kind and bill, or an organisation's or a profile's public name), what became of it, the moderator's explanation
/// when there is one, the link while it still stands, and the moderation rules. Never the moderator, never other reporters.
/// </summary>
internal sealed class ReportDecidedTemplate(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    /// <summary>The moderation rules' page, linked from every decision.</summary>
    public const string RulesPath = "/moderavimo-taisykles";

    private static readonly string[] Targets = [ReportTargets.Reason, ReportTargets.Comment, ReportTargets.Statement, ReportTargets.Response, ReportTargets.Record, ReportTargets.Organisation, ReportTargets.Profile];

    public EmailMessage Render(ReportDecidedEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!ReportOutcomes.All.Contains(model.Outcome) || !Targets.Contains(model.Target))
        {
            throw new InvalidOperationException($"Unknown report decision '{model.Target}' / '{model.Outcome}'.");
        }

        var about = model.Target switch
        {
            ReportTargets.Organisation or ReportTargets.Profile => l.Format("Report.About." + model.Target, model.Name ?? ""),
            _ => l.Format("Report.About." + model.Target, model.BillNumber ?? "", model.BillTitle ?? ""),
        };

        // Lithuanian agrees with the noun: a statement ("pozicija") and an organisation are feminine, the rest masculine.
        var gender = model.Target is ReportTargets.Statement or ReportTargets.Organisation ? "f" : "m";
        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
            l.Format("Report.Body", about) + " " + l[$"Report.Outcome.{model.Outcome}.{gender}"],
        };
        if (!string.IsNullOrWhiteSpace(model.Statement))
        {
            paragraphs.Add(l.Format(EmailText.Statement, model.Statement));
        }

        if (model.Path is { } path)
        {
            paragraphs.Add(EmailLayout.LinkLine(l["Report.Where"], SiteLinks.Absolute(site.Value.Address, path)));
        }

        paragraphs.Add(EmailLayout.LinkLine(l["Report.Rules"], SiteLinks.Absolute(site.Value.Address, RulesPath)));
        paragraphs.Add(l["Report.Why"]);
        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.Build(model.To, l["Report.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address));
    }
}
