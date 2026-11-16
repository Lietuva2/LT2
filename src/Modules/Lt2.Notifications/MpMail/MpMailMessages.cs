using Lt2.Notifications.Digests;
using Lt2.SharedKernel;

namespace Lt2.Notifications.MpMail;

/// <summary>
/// The outbox kinds of MP mail (design 8.19): written and sent by this module only, on the MP stream (its own sender address and
/// optionally its own SMTP account). Each payload holds its one address only until the email is sent and the row deleted.
/// </summary>
internal static class MpMailKinds
{
    public const string Digest = "email.mp-digest";
    public const string Introduction = "email.mp-introduction";
    public const string Notice = "email.mp-mail-notice";

    public static readonly IReadOnlyList<string> All = [Digest, Introduction, Notice];

    /// <summary>The stream name every MP mail kind is sent on.</summary>
    public const string Stream = "mp";
}

/// <summary>Which delivery row an email is, so the dispatcher can mark it sent, failed or bounced (design 8.19.3).</summary>
internal sealed record DeliveryRef(Guid RunId, string MpSlug, string Recipient);

/// <summary>One bill in one MP's summary: the frozen item, the MP's party numbers (groups of 20 or more) and the MP's response.</summary>
internal sealed record MpDigestBill(DigestItemInfo Item, DeliveryParty? Party, MpStance? Response);

/// <summary>
/// Payload of <see cref="MpMailKinds.Digest"/> (design 8.19.3, appendix A): one MP's summary, to the official address or, as a
/// copy, to the confirmed extra recipient. A copy carries the MP's name, its own "Nebegauti kopijų" link and never the MP's
/// preferences link.
/// </summary>
/// <param name="Recipient"><c>official</c> or <c>extra</c>.</param>
/// <param name="MpName">The MP's first and last name, in the nominative.</param>
/// <param name="Kind">The summary's kind: <c>weekly</c>, <c>before-vote</c> or <c>urgent</c>.</param>
/// <param name="PreferencesPath">The MP's page (official only).</param>
/// <param name="UnsubscribePath">The one-click stop (official only), for <c>List-Unsubscribe</c>.</param>
/// <param name="CopyStopPath">The extra recipient's own stop page (copies only).</param>
internal sealed record MpDigestEmail(
    string To,
    string Recipient,
    string MpName,
    string Kind,
    DateOnly RunDate,
    IReadOnlyList<MpDigestBill> Bills,
    string PublicCopyPath,
    string? PreferencesPath,
    string? UnsubscribePath,
    string? CopyStopPath,
    DeliveryRef? Delivery);

/// <summary>
/// Payload of <see cref="MpMailKinds.Introduction"/> (design 8.19.5): the letter as a person wrote and signed it, with what the
/// system adds below every MP email. <paramref name="Test"/>: the administrator's test copy, to their own address.
/// </summary>
internal sealed record MpIntroductionEmail(
    string To,
    int Version,
    string Subject,
    string Text,
    string SignerName,
    string SignerRole,
    string? PreferencesPath,
    string? UnsubscribePath,
    bool Test,
    DeliveryRef? Delivery);

/// <summary>
/// Payload of <see cref="MpMailKinds.Notice"/> (design 8.19.4): a change of the MP's choices, confirmed to the official address
/// (or, for <c>extra-added</c>, told to the extra recipient), with the link to the page.
/// </summary>
/// <param name="Notice">One of <see cref="MpMailNotices"/>.</param>
/// <param name="Frequency">For <c>changed</c>: the new choice.</param>
/// <param name="Path">The link the email carries: the MP's page, or the confirmation page for <c>extra-confirm</c>.</param>
/// <param name="ExtraAddress">For the notices about the extra recipient sent to the official address: which address.</param>
internal sealed record MpMailNoticeEmail(string To, string Notice, string MpName, string? Frequency, string? Path, string? ExtraAddress, int? ValidHours = null);

internal static class MpMailNotices
{
    /// <summary>The frequency changed (on the page, by one-click or on the linked account's page).</summary>
    public const string Changed = "changed";

    /// <summary>A new link was asked for; the old one stopped working.</summary>
    public const string NewLink = "new-link";

    /// <summary>An extra recipient was asked for: the link to confirm it, to the official address.</summary>
    public const string ExtraConfirm = "extra-confirm";

    /// <summary>The official address confirmed the extra recipient.</summary>
    public const string ExtraConfirmed = "extra-confirmed";

    /// <summary>To the extra recipient: copies will come, with their own link to stop them.</summary>
    public const string ExtraWelcome = "extra-welcome";

    /// <summary>The extra recipient was removed: by the MP on the page, or by the recipient from a copy.</summary>
    public const string ExtraRemoved = "extra-removed";

    public static readonly IReadOnlyList<string> All = [Changed, NewLink, ExtraConfirm, ExtraConfirmed, ExtraWelcome, ExtraRemoved];
}

/// <summary>The site paths of MP mail's pages and the one-click stop (design 8.19.4).</summary>
internal static class MpMailPaths
{
    public static string Preferences(string token) => "/suvestines/nustatymai/" + token;

    public static string OneClick(string token) => "/api/v1/mp-mail/" + token + "/stop";

    public static string ConfirmExtra(string token) => "/suvestines/nustatymai/patvirtinti/" + token;

    public static string StopCopies(string token) => "/suvestines/kopijos/" + token;

    public static string PublicCopy(DateOnly day) => "/suvestines/" + day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public const string Introduction = "/suvestines/pristatymas";

    public const string SignIn = "/prisijungti";
}
