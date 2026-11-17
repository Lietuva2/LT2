using Lt2.SharedKernel;

namespace Lt2.Notifications.Digests;

/// <summary>What sending is now (design 8.19.1): the setting, unless a lock keeps every summary a dry run.</summary>
/// <param name="Mode"><c>off</c> or <c>automatic</c>.</param>
/// <param name="Setting">What <c>Digest:Sending</c> says.</param>
/// <param name="Lock">Why it is off although the setting says otherwise: <c>no-introduction</c> or <c>not-caught</c>; null when no lock holds.</param>
internal sealed record SendingState(string Mode, string Setting, string? Lock)
{
    public bool On => Mode != SendingRules.Off;
}

/// <summary>
/// Real sending (design 8.19), as pure functions: the effective state with its two locks, which runs count, which summaries a
/// preference lets through, what happens to each MP's delivery, and the order of the bills in one MP's email. Nobody approves a
/// summary (owner, 2026-11-17): it is prepared for review and goes at its time.
/// </summary>
internal static class SendingRules
{
    public const string Off = "off";
    public const string Automatic = "automatic";

    /// <summary>A moderator's "Nesiųsti" needs a statement of at least so many characters (design 8.19.2).</summary>
    public const int CancelStatementMinimum = 20;
    public const string Introduction = "introduction";

    /// <summary>The lock while no introduction letter has been sent (design 8.19.1).</summary>
    public const string NoIntroduction = "no-introduction";

    /// <summary>The lock where the data is invented and the deployment does not declare its mail caught (design 8.19.1).</summary>
    public const string NotCaught = "not-caught";

    /// <summary>
    /// Sending as it is now: nothing goes before the introduction letter; where the data is invented, nothing goes unless all
    /// mail is caught; otherwise what the setting says.
    /// </summary>
    public static SendingState State(string setting, bool introductionSent, bool inventedData, bool mailCaught)
    {
        var wanted = setting is Automatic ? setting : Off;
        if (inventedData && !mailCaught)
        {
            return new SendingState(Off, wanted, wanted == Off ? null : NotCaught);
        }

        if (!introductionSent)
        {
            return new SendingState(Off, wanted, wanted == Off ? null : NoIntroduction);
        }

        return new SendingState(wanted, wanted, null);
    }

    /// <summary>
    /// Whether MP mail may leave at all (the introduction letter included): only the invented-data lock applies, since the
    /// letter is what lifts the other.
    /// </summary>
    public static bool MailMayLeave(bool inventedData, bool mailCaught) => !inventedData || mailCaught;

    /// <summary>What a prepared summary becomes at its time (design 8.19.1): a dry run while sending is off, otherwise sent.</summary>
    public static string GoneStatus(string mode) => mode == Off ? RunStatuses.DryRun : RunStatuses.Sent;

    /// <summary>
    /// Which runs count for the weekly ceiling, "changed notably", "covered by Monday's summary" and "never yet in a
    /// summary" (design 8.19.1): a prepared one always (it goes unless declined or every bill drops out); while sending is on,
    /// sent ones, so earlier dry runs do not thin the first real summary; while it is off, dry runs too. A cancelled or dropped
    /// run never counts: it frees its place.
    /// </summary>
    public static bool Counts(string runStatus, bool sendingOn) => CountingStatuses(sendingOn).Contains(runStatus);

    /// <summary>The run statuses of <see cref="Counts"/>, as the database queries use them.</summary>
    public static IReadOnlyList<string> CountingStatuses(bool sendingOn) =>
        sendingOn ? [RunStatuses.Prepared, RunStatuses.Sent] : [RunStatuses.Prepared, RunStatuses.DryRun, RunStatuses.Sent];

    /// <summary>The delivery statuses that count towards an MP's weekly ceiling: an email that went, or would have gone.</summary>
    public static readonly IReadOnlyList<string> CeilingDeliveryStatuses =
    [
        DeliveryStatuses.DryRun, DeliveryStatuses.Queued, DeliveryStatuses.Sent, DeliveryStatuses.Failed,
        DeliveryStatuses.Bounced, DeliveryStatuses.Introduced,
    ];

    /// <summary>Whether one MP's delivery of a counting run counts towards that MP's weekly ceiling: one that went, or would go.</summary>
    public static bool CountsForCeiling(string runStatus, string deliveryStatus, bool sendingOn) =>
        Counts(runStatus, sendingOn) && CeilingDeliveryStatuses.Contains(deliveryStatus);

    /// <summary>
    /// Whether an MP has had the introduction letter (design 8.19.5): a delivery of an introduction run that went to the outbox
    /// or was sent, or a summary's delivery replaced by the letter.
    /// </summary>
    public static bool Introduced(string runKind, string deliveryStatus) =>
        deliveryStatus == DeliveryStatuses.Introduced
        || (runKind == Introduction && deliveryStatus is DeliveryStatuses.Queued or DeliveryStatuses.Sent);

    /// <summary>
    /// The MP's choice against a summary's kind (design 8.19.4): the default lets everything through; "Tik kas savaitę" the
    /// weekly one; "Tik prieš balsavimus" the before-vote and urgent ones (an urgent summary counts as one before votes);
    /// "Nesiųsti" nothing. The introduction letter goes unless the MP stopped.
    /// </summary>
    public static bool PreferenceAllows(string frequency, string kind) => frequency switch
    {
        Frequencies.Stopped => false,
        Frequencies.Weekly => kind is DigestRules.Weekly or Introduction,
        Frequencies.BeforeVote => kind is DigestRules.BeforeVote or DigestRules.Urgent or Introduction,
        _ => true,
    };

    /// <summary>
    /// What happens to an MP's own copy when a summary is let go (design 8.19.3): never to a suppressed address; not when the
    /// MP's choice excludes this kind; the introduction letter instead to an MP who has not had it; not over the weekly ceiling
    /// (an urgent summary may exceed it by one); otherwise queued.
    /// </summary>
    public static string DeliveryStatus(bool suppressed, string frequency, string kind, bool introduced, int alreadyThisWeek, int maxPerWeek) =>
        suppressed ? DeliveryStatuses.Suppressed
        : !PreferenceAllows(frequency, kind) ? DeliveryStatuses.SkippedPreference
        : !introduced ? DeliveryStatuses.Introduced
        : !DigestRules.WithinCeiling(alreadyThisWeek, maxPerWeek, kind == DigestRules.Urgent) ? DeliveryStatuses.SkippedCeiling
        : DeliveryStatuses.Queued;

    /// <summary>
    /// What happens to an office member's copy (design 8.19.3, owner 2026-11-17), whatever the MP's own choice: never to a
    /// suppressed address; not when the member's own choice excludes this kind; not over the member's own weekly ceiling (an
    /// urgent summary may exceed it by one); otherwise queued.
    /// </summary>
    public static string OfficeStatus(bool suppressed, string frequency, string kind, int alreadyThisWeek, int maxPerWeek) =>
        suppressed ? DeliveryStatuses.Suppressed
        : !PreferenceAllows(frequency, kind) ? DeliveryStatuses.SkippedPreference
        : !DigestRules.WithinCeiling(alreadyThisWeek, maxPerWeek, kind == DigestRules.Urgent) ? DeliveryStatuses.SkippedCeiling
        : DeliveryStatuses.Queued;

    /// <summary>
    /// A confirmed extra recipient gets a copy only of what the MP gets: an email the MP's own address was sent, and never at a
    /// suppressed address.
    /// </summary>
    public static string? ExtraStatus(string officialStatus, bool extraConfirmed, bool extraSuppressed) =>
        !extraConfirmed || officialStatus != DeliveryStatuses.Queued ? null
        : extraSuppressed ? DeliveryStatuses.Suppressed
        : DeliveryStatuses.Queued;

    /// <summary>
    /// The bills in one MP's email, most relevant first (design 8.4, 8.19.3): bills whose lead committee is one of the MP's,
    /// then bills planned in the coming days by date, then the rest by participants. The import does not know lead committees
    /// yet, so that group is empty and the rest holds.
    /// </summary>
    public static IReadOnlyList<DigestItemInfo> OrderForMp(IEnumerable<DigestItemInfo> items, IReadOnlySet<string> ledByMpsCommittees) =>
        items.OrderBy(i => !ledByMpsCommittees.Contains(i.BillNumber))
            .ThenBy(i => i.PlannedDate is null)
            .ThenBy(i => i.PlannedDate)
            .ThenByDescending(i => i.For + i.Against)
            .ThenBy(i => i.BillNumber, StringComparer.Ordinal)
            .ToList();

    /// <summary>The extra recipient's address as the page takes it: one plain address, at most 254 characters.</summary>
    public static bool IsAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address)
        && address.Trim().Length <= 254
        && !address.Trim().Any(char.IsWhiteSpace)
        && address.Trim().IndexOf('@', StringComparison.Ordinal) is var at and > 0
        && at == address.Trim().LastIndexOf('@')
        && address.Trim()[(at + 1)..].Contains('.', StringComparison.Ordinal)
        && System.Net.Mail.MailAddress.TryCreate(address.Trim(), out _);
}
