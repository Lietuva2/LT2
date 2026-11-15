using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The email telling a person that something security-relevant changed on their account (design 6.2): a passkey added,
/// a staff role granted or activated, an account reset. It never carries a code or a link that acts.
/// </summary>
internal sealed class AccountNoticeTemplate(IStringLocalizer<EmailTemplates> l)
{
    public EmailMessage Render(AccountNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!AccountNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown account notice '{model.Notice}'.");
        }

        var role = model.Role is null ? "" : l["Role." + model.Role].Value;
        var paragraphs = new List<string> { l[EmailText.Greeting], l.Format($"Notice.{model.Notice}.Body", role) };
        if (model.PasskeysRemoved > 0)
        {
            paragraphs.Add(l.Format("Notice.PasskeysRemoved", model.PasskeysRemoved));
        }

        paragraphs.Add(l["Notice.NotYou"]);
        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.Build(model.To, l[$"Notice.{model.Notice}.Subject"], paragraphs);
    }
}
