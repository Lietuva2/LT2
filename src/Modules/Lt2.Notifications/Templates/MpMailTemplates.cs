using System.Globalization;
using System.Text;
using Lt2.Notifications.Digests;
using Lt2.Notifications.MpMail;
using Lt2.SharedKernel;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Lt2.Notifications.Templates;

/// <summary>
/// MP mail (design 8.19.3 to 8.19.5, appendix A): the summary as an MP or a copy's recipient reads it, the introduction letter,
/// and the emails that confirm a change of the MP's choices. Both sides always, with the 30-day counts; the MP's own response
/// or the link to answer; at the end what the numbers are and are not, the public copy, the preferences link and the sentence on
/// taking over the profile. Links are full addresses of the site (<see cref="SiteLinks.Absolute"/>); nothing is tracked. The
/// moderators' preview is the same text (<see cref="Text"/>).
/// </summary>
internal sealed class MpMailTemplates(IStringLocalizer<EmailTemplates> l, IOptions<SiteOptions> site)
{
    public const string ListUnsubscribe = "List-Unsubscribe";
    public const string ListUnsubscribePost = "List-Unsubscribe-Post";

    public EmailMessage Digest(MpDigestEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (subject, paragraphs) = Text(model);
        return EmailLayout.BuildMp(model.To, subject, paragraphs, EmailLayout.Footer(l, site.Value.Address), Headers(model.UnsubscribePath, model.CopyStopPath));
    }

    public EmailMessage Introduction(MpIntroductionEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var paragraphs = new List<string>();
        if (model.Test)
        {
            paragraphs.Add(l["Intro.Test"]);
        }

        paragraphs.AddRange(Paragraphs(model.Text));
        paragraphs.Add(model.SignerName.Trim() + "\n" + model.SignerRole.Trim());
        paragraphs.AddRange(Closing(model.PreferencesPath ?? (model.Test ? MpMailPaths.Preferences("…") : null), null));
        return EmailLayout.BuildMp(model.To, (model.Test ? l["Intro.TestSubject"] + " " : "") + model.Subject.Trim(), paragraphs, EmailLayout.Footer(l, site.Value.Address), Headers(model.UnsubscribePath, null));
    }

    public EmailMessage Notice(MpMailNoticeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!MpMailNotices.All.Contains(model.Notice))
        {
            throw new InvalidOperationException($"Unknown MP mail notice '{model.Notice}'.");
        }

        var link = model.Path is { } path ? Link(path) : "";
        var paragraphs = new List<string>
        {
            l[EmailText.Greeting],
            l.Format($"MpMail.{model.Notice}.Body", model.MpName, model.ExtraAddress ?? "", link, model.ValidHours?.ToString(CultureInfo.InvariantCulture) ?? ""),
        };
        if (model.Notice == MpMailNotices.Changed && model.Frequency is { } frequency)
        {
            paragraphs.Add(l.Format("MpMail.Now", l["MpMail.Frequency." + frequency]));
            if (frequency == Frequencies.Stopped)
            {
                paragraphs.Add(l["MpMail.StoppedNote"]);
            }
        }

        if (model.Notice is MpMailNotices.Changed or MpMailNotices.ExtraConfirmed or MpMailNotices.ExtraRemoved)
        {
            paragraphs.Add(l.Format("MpMail.NotYou", link));
        }

        paragraphs.Add(l[EmailText.Signature]);
        return EmailLayout.BuildMp(model.To, l[$"MpMail.{model.Notice}.Subject"], paragraphs, EmailLayout.Footer(l, site.Value.Address), null);
    }

    /// <summary>For the moderators' preview: why this MP's own address does not get this summary, or null when it does.</summary>
    public string? SkippedNote(string deliveryStatus) => deliveryStatus switch
    {
        DeliveryStatuses.DryRun or DeliveryStatuses.Waiting or DeliveryStatuses.Queued or DeliveryStatuses.Sent => null,
        DeliveryStatuses.SkippedCeiling => l["Digest.Skipped"].Value,
        _ when l["Digest.Skipped." + deliveryStatus] is { ResourceNotFound: false } note => note.Value,
        _ => null,
    };

    /// <summary>The subject and paragraphs of one MP's summary: the email's text part, and the moderators' preview.</summary>
    public (string Subject, IReadOnlyList<string> Paragraphs) Text(MpDigestEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var copy = model.Recipient == Recipients.Extra;
        var day = model.RunDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var paragraphs = new List<string> { l[EmailText.Greeting] };
        if (copy)
        {
            paragraphs.Add(l.Format("Digest.CopyOf", model.MpName));
        }

        paragraphs.Add(l.Format("Digest.Intro", model.Bills.Count));
        foreach (var bill in model.Bills)
        {
            paragraphs.Add(Bill(bill, copy, model));
        }

        paragraphs.AddRange(Closing(copy ? null : model.PreferencesPath, copy ? model.CopyStopPath : null, model.PublicCopyPath));
        return (l.Format(copy ? "Digest.CopySubject" : "Digest.Subject", day), paragraphs);
    }

    private string Bill(MpDigestBill bill, bool copy, MpDigestEmail model)
    {
        var lt = CultureInfo.GetCultureInfo("lt-LT");
        var item = bill.Item;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{item.BillNumber} · {item.BillTitle}");
        var stage = item.Stage is { } s && l["Stage." + s] is { ResourceNotFound: false } name ? Capital(name.Value) : null;
        if (item.PlannedDate is { } planned)
        {
            text.Append('\n').Append(stage is null
                ? l.Format("Digest.Planned", planned.ToString("MMMM d", lt))
                : l.Format("Digest.StagePlanned", stage, planned.ToString("MMMM d", lt)));
        }
        else if (stage is not null)
        {
            text.Append('\n').Append(stage);
        }

        text.Append('\n').Append(l.Format("Digest.For", N(item.For), N(item.ForOlderThan30Days)));
        text.Append('\n').Append(l.Format("Digest.Against", N(item.Against), N(item.AgainstOlderThan30Days)));
        if (bill.Party is { } mine)
        {
            if (mine.For is { } f)
            {
                text.Append('\n').Append(l.Format("Digest.PartyFor", mine.Party, N(f)));
            }

            if (mine.Against is { } a)
            {
                text.Append('\n').Append(l.Format("Digest.PartyAgainst", mine.Party, N(a)));
            }
        }

        if (item.SurgeSharePercent is { } share)
        {
            text.Append('\n').Append(l.Format("Digest.Surge", item.SurgeWindowHours, share));
        }

        foreach (var side in new[] { "for", "against" })
        {
            var reasons = item.Reasons.Where(r => r.Side == side).ToList();
            if (reasons.Count > 0)
            {
                text.Append('\n').Append(l["Digest.Reasons." + side]);
                for (var i = 0; i < reasons.Count; i++)
                {
                    text.Append('\n').Append(Reason(reasons[i], ReasonLink(model, item.BillNumber, side, i + 1)));
                }
            }
            else
            {
                // A side may have no argument a moderator has seen yet.
                text.Append('\n').Append(l["Digest.NoReasons." + side]);
            }
        }

        var billLink = Link("/seimas/" + Uri.EscapeDataString(item.BillNumber));
        if (bill.Response is { } response)
        {
            var stance = l["Digest.Stance." + response.Stance];
            var when = VilniusTime.DateOf(response.WrittenAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            text.Append('\n').Append(copy ? l.Format("Digest.ResponseCopy", stance, when) : l.Format("Digest.Response", stance, when, billLink));
        }
        else
        {
            text.Append('\n').Append(l.Format("Digest.Answer", billLink));
        }

        text.Append('\n').Append(l.Format("Digest.AllNumbers", billLink));
        return text.ToString();
    }

    /// <summary>
    /// One argument in the email (design 8.19.3, owner 2026-11-16): its beginning by the same rule as the page
    /// (<see cref="TextExcerpt"/>, about 300 characters) with "…" and the "Man svarbu" count, and when it was cut a plain
    /// line "Visas argumentas: {link}" to it on the summary's public copy, which shows the snapshot's whole text. The
    /// snapshot itself keeps the whole text; the moderators' preview is this same text.
    /// </summary>
    private string Reason(DigestReason reason, string link)
    {
        var excerpt = TextExcerpt.Of(reason.Text, TextExcerpt.ArgumentShown, TextExcerpt.ArgumentMargin);
        return excerpt.Rest is null
            ? string.Create(CultureInfo.InvariantCulture, $"  · {reason.Text} ({N(reason.Endorsements)})")
            : string.Create(CultureInfo.InvariantCulture, $"  · {excerpt.Shown}… ({N(reason.Endorsements)})") + "\n    " + l.Format("Digest.WholeReason", link);
    }

    /// <summary>The argument on the public copy: at its own place when the run is known, else the copy's page.</summary>
    private string ReasonLink(MpDigestEmail model, string number, string side, int place) => Link(model.RunId is { } run
        ? MpMailPaths.ReasonOnPublicCopy(model.RunDate, run, number, side, place)
        : model.PublicCopyPath);

    /// <summary>
    /// What ends every MP email (design 8.19.3, 8.19.5): what the numbers are and are not and the public copy (summaries only),
    /// then the preferences link and the sentence on taking over the profile (the MP's own address), or a copy's own stop link.
    /// </summary>
    private IEnumerable<string> Closing(string? preferencesPath, string? copyStopPath, string? publicCopyPath = null)
    {
        if (publicCopyPath is not null)
        {
            yield return l["Digest.NotAPoll"] + "\n" + l.Format("Digest.PublicCopy", Link(publicCopyPath));
        }

        if (preferencesPath is not null)
        {
            yield return l.Format("Digest.Preferences", Link(preferencesPath)) + "\n" + l.Format("Digest.Takeover", Link(MpMailPaths.SignIn));
        }

        if (copyStopPath is not null)
        {
            yield return l.Format("Digest.CopyStop", Link(copyStopPath));
        }

        yield return l[EmailText.Signature];
    }

    /// <summary>
    /// <c>List-Unsubscribe</c> (design 8.19.3): the MP's one-click stop with <c>List-Unsubscribe-Post</c> (RFC 8058); a copy's
    /// own page without it, since the one-click form is the MP's one stop that needs no same-origin check.
    /// </summary>
    private Dictionary<string, string>? Headers(string? unsubscribePath, string? copyStopPath)
    {
        if (unsubscribePath is not null)
        {
            return new Dictionary<string, string>
            {
                [ListUnsubscribe] = "<" + Link(unsubscribePath) + ">",
                [ListUnsubscribePost] = "List-Unsubscribe=One-Click",
            };
        }

        return copyStopPath is null ? null : new Dictionary<string, string> { [ListUnsubscribe] = "<" + Link(copyStopPath) + ">" };
    }

    /// <summary>The letter's text as plain paragraphs: separated by an empty line, single line breaks kept.</summary>
    private static string[] Paragraphs(string text) =>
        text.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string Link(string path) => SiteLinks.Absolute(site.Value.Address, path);

    private static string Capital(string value) => value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.GetCultureInfo("lt-LT")) + value[1..];

    private static string N(int value) => value.ToString("#,0", new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = [3] });
}
