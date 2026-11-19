using System.Globalization;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The sign-in code email, in Lithuanian. The same text serves registration and sign-in, so it reveals nothing. A code a member
/// asked for on the account page to prove the address in use before changing it (design 6.2,
/// <see cref="SignInCodePurposes.EmailChange"/>) is the same code in other words.
/// </summary>
internal sealed class SignInCodeTemplate(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(SignInCodeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var keys = model.Purpose switch
        {
            null => "SignInCode",
            SignInCodePurposes.EmailChange => "EmailProofCode",
            _ => throw new InvalidOperationException($"Unknown code purpose '{model.Purpose}'."),
        };
        string[] paragraphs =
        [
            l[EmailText.Greeting],
            l[keys + ".Intro"],
            model.Code,
            l.Format(EmailText.CodeValidity, model.ValidMinutes.ToString(CultureInfo.InvariantCulture)),
            l[keys + ".NewestOnly"],
            l[keys + ".Ignore"],
            l[EmailText.Signature],
        ];
        return EmailLayout.Build(model.To, l.Format(keys + ".Subject", model.Code), paragraphs, EmailLayout.Footer(l, site.Value.Address), highlighted: 2);
    }
}
