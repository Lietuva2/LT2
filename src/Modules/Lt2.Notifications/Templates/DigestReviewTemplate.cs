using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// The moderators' notice of a prepared summary (design 8.19.2, owner 2026-11-17): when it goes, how many bills and arguments,
/// what they can do before then, and the summaries screen. Never an argument's text or anything about an MP.
/// </summary>
internal sealed class DigestReviewTemplate(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public EmailMessage Render(DigestReviewEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var due = Due(model.DueAt);
        return EmailLayout.Build(
            model.To,
            l.Format("DigestReview.Subject", due),
            [
                l[EmailText.Greeting],
                l.Format("DigestReview.Body", l["DigestReview.Kind." + model.Kind], due, Count(model.Bills, "Bill"), Count(model.Reasons, "Reason")),
                l["DigestReview.What"],
                l.Format("DigestReview.Link", SiteLinks.Absolute(site.Value.Address, model.Path)),
                l[EmailText.Signature],
            ],
            EmailLayout.Footer(l, site.Value.Address));
    }

    /// <summary>"yyyy-MM-dd HH:mm", Vilnius time.</summary>
    public static string Due(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, VilniusTime.Zone).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private string Count(int n, string noun) =>
        l.Format(LithuanianCounts.Pick(n, $"DigestReview.{noun}.One", $"DigestReview.{noun}.Few", $"DigestReview.{noun}.Many"), n);
}
