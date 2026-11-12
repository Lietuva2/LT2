using Lt2.SharedKernel;

namespace Lt2.Appeals.Domain;

/// <summary>The two sides of an appeal (design 8.2). Abstaining is not a request to an MP, so it is not offered.</summary>
internal static class Sides
{
    public const string For = "for";
    public const string Against = "against";

    public static readonly IReadOnlyList<string> All = [For, Against];

    public static bool IsSide(string? value) => value is For or Against;
}

/// <summary>
/// The counted rules of appeals (design 8.2, 8.4, 6.6; <c>AGENTS.md</c> "Appeals"), as pure functions. Each returns the
/// error code a request gets when it breaks the rule, or <c>null</c> when it keeps it.
/// </summary>
internal static class AppealRules
{
    /// <summary>An argument is plain text of at most this many characters.</summary>
    public const int ReasonMaxLength = 500;

    /// <summary>A person may write at most this many arguments per bill.</summary>
    public const int ReasonsPerPersonPerBill = 2;

    /// <summary>A person may mark at most this many arguments "Man svarbu" per bill.</summary>
    public const int EndorsementsPerPersonPerBill = 3;

    /// <summary>The neutral description is short; the design gives no number, this one is a choice.</summary>
    public const int DescriptionMaxLength = 1500;

    /// <summary>
    /// One appeal per bill: a bill whose appeal is pending, returned, open or closed has one; only a rejected appeal
    /// leaves the bill free for a new one.
    /// </summary>
    public static bool BillAlreadyHasAppeal(IEnumerable<string> statusesOfAppealsOnTheBill) =>
        statusesOfAppealsOnTheBill.Any(s => s != AppealStatuses.Rejected);

    /// <summary>A side is "for" or "against"; nothing else is a side.</summary>
    public static string? CheckSide(string? side) => Sides.IsSide(side) ? null : "side.invalid";

    /// <summary>An argument is plain text, not blank, at most 500 characters once trimmed.</summary>
    public static string? CheckReasonText(string? text)
    {
        var trimmed = NormaliseText(text);
        if (trimmed.Length == 0)
        {
            return "reason.empty";
        }

        return trimmed.Length > ReasonMaxLength ? "reason.too-long" : null;
    }

    /// <summary>The neutral description is plain text, not blank, at most <see cref="DescriptionMaxLength"/> characters.</summary>
    public static string? CheckDescription(string? text)
    {
        var trimmed = NormaliseText(text);
        if (trimmed.Length == 0)
        {
            return "description.empty";
        }

        return trimmed.Length > DescriptionMaxLength ? "description.too-long" : null;
    }

    /// <summary>
    /// Plain text as stored: line ends unified, control characters other than line breaks dropped, outer white space
    /// trimmed. It is encoded on output, never interpreted (design 6.4).
    /// </summary>
    public static string NormaliseText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var kept = unified.Where(c => c == '\n' || !char.IsControl(c)).ToArray();
        return new string(kept).Trim();
    }

    /// <summary>
    /// A person may write an argument only on the side they have taken, and at most two per bill (every argument they
    /// wrote on the bill counts, whatever happened to it since).
    /// </summary>
    public static string? CheckNewReason(string? authorsSide, string reasonSide, int reasonsAlreadyWritten)
    {
        if (authorsSide is null)
        {
            return "position.required";
        }

        if (authorsSide != reasonSide)
        {
            return "reason.other-side";
        }

        return reasonsAlreadyWritten >= ReasonsPerPersonPerBill ? "reason.limit" : null;
    }

    /// <summary>"Man svarbu" only on an argument of the person's own side, and at most three per bill.</summary>
    public static string? CheckEndorsement(string? endorsersSide, string reasonSide, int endorsementsAlreadyOnBill)
    {
        if (endorsersSide is null)
        {
            return "position.required";
        }

        if (endorsersSide != reasonSide)
        {
            return "endorsement.other-side";
        }

        return endorsementsAlreadyOnBill >= EndorsementsPerPersonPerBill ? "endorsement.limit" : null;
    }

    /// <summary>
    /// Changing sides keeps one position per person per bill: the row is overwritten, and the "Man svarbu" marks on the
    /// old side's arguments go, since they are allowed only on one's own side.
    /// </summary>
    public static bool ChangingSideDropsEndorsementsOfTheOldSide(string? oldSide, string newSide) =>
        oldSide is not null && oldSide != newSide;
}

/// <summary>
/// The appeal's threshold (design 8.4): public, set by the initiator (the form offers the default), never below the
/// administrator's minimum, changeable by the initiator until the appeal first enters a summary, final once a moderator
/// changed it.
/// </summary>
internal static class ThresholdRules
{
    /// <summary>Anyone who sets a threshold, initiator or moderator, sets it at or above the minimum.</summary>
    public static string? CheckRequested(int requested, int minimum) =>
        requested < minimum ? "threshold.below-minimum" : null;

    /// <summary>
    /// The initiator may change the threshold while the appeal has never entered a summary and no moderator has
    /// changed it; never on a closed or rejected appeal.
    /// </summary>
    public static string? CheckInitiatorChange(string status, string setBy, DateTimeOffset? firstSummarisedAt)
    {
        if (status is AppealStatuses.Closed or AppealStatuses.Rejected)
        {
            return "appeal.not-open";
        }

        if (setBy == ThresholdSetters.Moderator)
        {
            return "threshold.set-by-moderator";
        }

        return firstSummarisedAt is not null ? "threshold.locked" : null;
    }

    /// <summary>Whether the initiator still controls the threshold (shown on the appeal page to the initiator only).</summary>
    public static bool InitiatorMayChange(string status, string setBy, DateTimeOffset? firstSummarisedAt) =>
        CheckInitiatorChange(status, setBy, firstSummarisedAt) is null;

    /// <summary>
    /// The threshold that applies today. If the minimum was raised after the appeal set its number, the appeal is lifted
    /// to the minimum; an appeal already in summaries keeps its number (it stays in).
    /// </summary>
    public static int Effective(int stored, int minimum, DateTimeOffset? firstSummarisedAt) =>
        firstSummarisedAt is not null ? stored : Math.Max(stored, minimum);

    /// <summary>The threshold counts participants on both sides together.</summary>
    public static bool Reached(int participantsBothSides, int effectiveThreshold) => participantsBothSides >= effectiveThreshold;

    /// <summary>What the form offers: the default, but never below the minimum.</summary>
    public static int Offered(int defaultValue, int minimum) => Math.Max(defaultValue, minimum);
}

/// <summary>
/// Published numbers (design 6.6, 8.2): both sides always together, each always with the count for accounts older than
/// 30 days.
/// </summary>
internal static class CountRules
{
    /// <summary>An account counts as "older than 30 days" from the moment 30 full days have passed since it became a member.</summary>
    public static readonly TimeSpan EstablishedAge = TimeSpan.FromDays(30);

    /// <summary>The moment before which an account must have become a member to count as older than 30 days.</summary>
    public static DateTimeOffset EstablishedBefore(DateTimeOffset now) => now - EstablishedAge;

    public static bool IsOlderThan30Days(DateTimeOffset memberSince, DateTimeOffset now) => memberSince <= EstablishedBefore(now);

    /// <summary>Builds the published counts; refuses numbers that cannot be true (an older-than-30-days count above its total).</summary>
    public static SideCounts Publish(int forTotal, int forOlder, int againstTotal, int againstOlder)
    {
        if (forTotal < 0 || againstTotal < 0 || forOlder < 0 || againstOlder < 0 || forOlder > forTotal || againstOlder > againstTotal)
        {
            throw new ArgumentOutOfRangeException(nameof(forOlder), "Counts must be non-negative and the older-account counts within their totals.");
        }

        return new SideCounts(forTotal, forOlder, againstTotal, againstOlder);
    }
}

/// <summary>
/// Group numbers (for example supporters of a party, design 6.3, 8.2): published only for groups of at least 20 people.
/// Nothing uses it before the summaries.
/// </summary>
internal static class GroupRules
{
    public const int MinimumGroupSize = 20;

    public static bool MayPublish(int groupSize) => groupSize >= MinimumGroupSize;

    /// <summary>The number to publish for a group, or <c>null</c> when the group is too small to show.</summary>
    public static int? Publishable(int groupSize) => MayPublish(groupSize) ? groupSize : null;

    /// <summary>
    /// A group shown beside a total it is part of (a party's supporters beside a side's count): the group needs at least
    /// 20, and so does the rest of the total, or the rest could be worked out by subtraction. A rest of none is fine.
    /// </summary>
    public static int? PublishableBeside(int groupSize, int total)
    {
        var rest = total - groupSize;
        return MayPublish(groupSize) && (rest == 0 || MayPublish(rest)) ? groupSize : null;
    }
}

/// <summary>Design 8.1: an appeal closes when the Seimas takes the final vote on its bill; the counts are frozen then.</summary>
internal static class ClosingRules
{
    /// <summary>Only an open appeal closes, and only on a final vote; a pending one waits for its moderator.</summary>
    public static bool ShouldClose(string status, long? finalVoteId) => status == AppealStatuses.Open && finalVoteId is not null;

    /// <summary>The counts shown: frozen ones once closed, live ones before.</summary>
    public static SideCounts Shown(SideCounts live, int? closedFor, int? closedForOlder, int? closedAgainst, int? closedAgainstOlder) =>
        closedFor is { } f && closedForOlder is { } fo && closedAgainst is { } a && closedAgainstOlder is { } ao
            ? CountRules.Publish(f, fo, a, ao)
            : live;
}

/// <summary>Turns a rule's error code into the module's error.</summary>
internal static class RuleErrors
{
    public static Error Of(string code) => code switch
    {
        "position.required" or "reason.other-side" or "reason.limit" or "endorsement.other-side" or "endorsement.limit"
            or "threshold.set-by-moderator" or "threshold.locked" or "appeal.not-open" => Error.Conflict(code),
        "side.invalid" => Error.Validation(code, "side"),
        "reason.empty" or "reason.too-long" => Error.Validation(code, "text"),
        "description.empty" or "description.too-long" => Error.Validation(code, "description"),
        "threshold.below-minimum" => Error.Validation(code, "threshold"),
        _ => Error.Validation(code),
    };
}
