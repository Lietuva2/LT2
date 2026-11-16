using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The email telling a person that something security-relevant changed on their account (design 6.2): a passkey added,
/// a staff role granted or activated, an account reset; and to an MP's official address what happened to the profile
/// (design 8.5.2, 8.5.3: taken over, given up, ended by an administrator with the statement, an office member added,
/// removed or gone), never naming an office member. It never carries a code or a link that acts.
/// </summary>
internal sealed class AccountNoticeTemplate(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(AccountNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!AccountNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown account notice '{model.Notice}'.");
        }

        var role = model.Role is null ? "" : l["Role." + model.Role].Value;
        var paragraphs = new List<string> { l[EmailText.Greeting], l.Format($"Notice.{model.Notice}.Body", role, model.MpName ?? "") };
        if (!string.IsNullOrWhiteSpace(model.Statement))
        {
            paragraphs.Add(l.Format("Notice.AdministratorStatement", model.Statement));
        }

        if (model.PasskeysRemoved > 0)
        {
            paragraphs.Add(l.Format("Notice.PasskeysRemoved", model.PasskeysRemoved));
        }

        paragraphs.Add(l["Notice.NotYou"]);
        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.Build(model.To, l[$"Notice.{model.Notice}.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address));
    }
}
