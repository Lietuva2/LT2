using System.Globalization;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// A moderator restricted the person from writing, or lifted that (design 8.3): the statement, the end ("until" a day or
/// until lifted), what still works, and where to object. Never a side; no tracking.
/// </summary>
internal sealed class WritingRestrictionTemplate(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(WritingRestrictionEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!WritingRestrictionNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown writing restriction notice '{model.Notice}'.");
        }

        var paragraphs = new List<string> { l[EmailText.Greeting], l[$"Restriction.{model.Notice}.Body"] };
        if (model.Notice == WritingRestrictionNotices.Restricted)
        {
            paragraphs.Add(model.Until is { } until
                ? l.Format("Restriction.Until", until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                : l["Restriction.UntilLifted"]);
            paragraphs.Add(l["Restriction.Untouched"]);
        }

        paragraphs.Add(l.Format(EmailText.Statement, model.Statement));
        if (model.Notice == WritingRestrictionNotices.Restricted && !string.IsNullOrWhiteSpace(model.Contact))
        {
            paragraphs.Add(l.Format(EmailText.Contact, model.Contact));
        }

        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.Build(model.To, l[$"Restriction.{model.Notice}.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address));
    }
}
