using System.Globalization;
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
            l[EmailText.Greeting],
            l.Format("Organisation.Code.Intro", model.OrganisationName, model.Domain),
            model.Code,
            l.Format(EmailText.CodeValidity, model.ValidMinutes.ToString(CultureInfo.InvariantCulture)),
            l.Format("Organisation.Code.What", model.OrganisationName, model.Domain),
            l["Organisation.Code.Ignore"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l.Format("Organisation.Code.Subject", model.Code), paragraphs, EmailLayout.Footer(l, site.Value.Address), highlighted: 2);
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
            l[EmailText.Greeting],
            l.Format($"Organisation.{model.Notice}.Body", model.OrganisationName, bill, link),
        };
        if (!string.IsNullOrWhiteSpace(model.Statement))
        {
            paragraphs.Add(l.Format(EmailText.Statement, model.Statement));
        }

        if (!string.IsNullOrWhiteSpace(model.Contact))
        {
            paragraphs.Add(l.Format(EmailText.Contact, model.Contact));
        }

        paragraphs.Add(l["Organisation.Why"]);
        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.Build(model.To, l.Format($"Organisation.{model.Notice}.Subject", model.OrganisationName), paragraphs, EmailLayout.Footer(l, site.Value.Address));
    }
}
