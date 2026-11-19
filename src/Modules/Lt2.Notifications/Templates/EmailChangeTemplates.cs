using System.Globalization;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The emails of changing the account's address (design 6.2; the Lithuanian texts are drafts for the native reader): the code to
/// the new address; to an address that already belongs to an account, that nothing was changed (no code, no link); to the old
/// address, that the address changed, when and to which address in part, with "Ne jūs? Grąžinti ankstesnį adresą" (a full
/// address whose code is after the <c>#</c>, and the page to open and the code to type there); to the address the account had
/// moved to, that the earlier one was put back. Nothing tracked.
/// </summary>
internal sealed class EmailChangeTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Code(EmailChangeCodeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l["EmailChange.Code.Intro"],
            model.Code,
            l.Format(EmailText.CodeValidity, model.ValidMinutes.ToString(CultureInfo.InvariantCulture)),
            l["EmailChange.Code.NewestOnly"],
            l["EmailChange.Code.Ignore"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l.Format("EmailChange.Code.Subject", model.Code), paragraphs, EmailLayout.Footer(l, site.Value.Address), highlighted: 2);
    }

    public EmailMessage Taken(EmailChangeTakenEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l["EmailChange.Taken.Body"],
            l["EmailChange.Taken.Nothing"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l["EmailChange.Taken.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address));
    }

    public EmailMessage Changed(EmailChangedEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l.Format("EmailChange.Changed.Body", When(model.ChangedAt), model.NewAddress),
            l["EmailChange.Changed.After"],
            EmailLayout.LinkLine(l["EmailChange.Changed.Link"], SiteLinks.Absolute(site.Value.Address, model.Path)),
            l.Format(EmailText.WithoutLink, SiteLinks.Absolute(site.Value.Address, model.PagePath)),
            model.Code,
            l.Format("EmailChange.Changed.Validity", When(model.ExpiresAt)),
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l["EmailChange.Changed.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address), highlighted: 5);
    }

    public EmailMessage Restored(EmailRestoredEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l.Format("EmailChange.Restored.Body", When(model.RestoredAt)),
            l["EmailChange.Restored.NotYou"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l["EmailChange.Restored.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address));
    }

    /// <summary>A moment as the emails write it: <c>yyyy-MM-dd HH:mm</c>, Vilnius time.</summary>
    private static string When(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, VilniusTime.Zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
