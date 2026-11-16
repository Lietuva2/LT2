using System.Globalization;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>The sign-in code email, in Lithuanian. The same text serves registration and sign-in, so it reveals nothing.</summary>
internal sealed class SignInCodeTemplate(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(SignInCodeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l["SignInCode.Intro"],
            model.Code,
            l.Format(EmailText.CodeValidity, model.ValidMinutes.ToString(CultureInfo.InvariantCulture)),
            l["SignInCode.NewestOnly"],
            l["SignInCode.Ignore"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l.Format("SignInCode.Subject", model.Code), paragraphs, EmailLayout.Footer(l, site.Value.Address), highlighted: 2);
    }
}
