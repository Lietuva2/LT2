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

    /// <summary>A per-person limit set to this means no limit (settings Appeals:ReasonsPerPerson, Appeals:EndorsementsPerPerson).</summary>
    public const int NoLimit = 0;

    /// <summary>
    /// The argument statuses that count towards a person's limit (design 8.2): shown or waiting for a moderator. Hidden
    /// and merged ones do not; a withdrawn one is gone.
    /// </summary>
    public static readonly IReadOnlyList<string> CountedReasonStatuses = [ReasonStatuses.Published, ReasonStatuses.Pending];

    /// <summary>The neutral description is short; the design gives no number, this one is a choice.</summary>
    public const int DescriptionMaxLength = 1500;

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
    /// A per-person limit (design 8.2): with the setting at 0 there is none; otherwise a person may add while they have
    /// fewer than the limit. Nothing already there is removed, so someone over a limit switched on later simply cannot
    /// add.
    /// </summary>
    public static bool MayAdd(int alreadyCounted, int limit) => limit == NoLimit || alreadyCounted < limit;

    /// <summary>How many a person may still add, or <c>null</c> when there is no limit (the pages then show no counter).</summary>
    public static int? Left(int alreadyCounted, int limit) => limit == NoLimit ? null : Math.Max(0, limit - alreadyCounted);

    /// <summary>
    /// An argument may be written for either side, whatever the author's own side and whether they took one (design 8.2,
    /// decided 2026-11-13), so the author's side is not an input. Across both sides, the person's shown or waiting
    /// arguments on the bill count towards the setting's limit, if one is set.
    /// </summary>
    public static string? CheckNewReason(string reasonSide, int reasonsCounted, int limit) =>
        CheckSide(reasonSide) ?? (MayAdd(reasonsCounted, limit) ? null : "reason.limit");

    /// <summary>"Man svarbu" only on an argument of the person's own side, within the setting's limit, if one is set.</summary>
    public static string? CheckEndorsement(string? endorsersSide, string reasonSide, int endorsementsAlreadyOnBill, int limit)
    {
        if (endorsersSide is null)
        {
            return "position.required";
        }

        if (endorsersSide != reasonSide)
        {
            return "endorsement.other-side";
        }

        return MayAdd(endorsementsAlreadyOnBill, limit) ? null : "endorsement.limit";
    }

    /// <summary>
    /// Changing sides keeps one position per person per bill: the row is overwritten, and the "Man svarbu" marks on the
    /// old side's arguments go, since they are allowed only on one's own side. The person's own arguments stay.
    /// </summary>
    public static bool ChangingSideDropsEndorsementsOfTheOldSide(string? oldSide, string newSide) =>
        oldSide is not null && oldSide != newSide;
}

/// <summary>
/// The threshold (design 8.12.2): one for all bills, <c>Digest:ThresholdDefault</c>, read when it is used and never below
/// the administrator's minimum; a moderator may set a bill's own number, which stays until a moderator changes it. A bill
/// that has been in a summary really sent to MPs stays eligible.
/// </summary>
internal static class ThresholdRules
{
    /// <summary>A moderator sets a bill's own number at or above the minimum.</summary>
    public static string? CheckRequested(int requested, int minimum) =>
        requested < minimum ? "threshold.below-minimum" : null;

    /// <summary>
    /// The threshold that applies today: the bill's own number if a moderator set one, otherwise the default; never below
    /// the minimum, so raising the minimum lifts every bill under it.
    /// </summary>
    public static int Effective(int? stored, int defaultValue, int minimum) => Math.Max(stored ?? defaultValue, minimum);

    /// <summary>
    /// The threshold counts participants on both sides together. A bill already in a summary really sent to MPs stays
    /// eligible whatever the threshold or the participants do later.
    /// </summary>
    public static bool Reached(int participantsBothSides, int effectiveThreshold, DateTimeOffset? firstSummarisedAt = null) =>
        firstSummarisedAt is not null || participantsBothSides >= effectiveThreshold;

    /// <summary>What a moderator's form starts from: the default, but never below the minimum.</summary>
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
    /// <summary>Only an open record closes, and only on a final vote; a hidden one stays hidden.</summary>
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
        "position.required" or "reason.limit" or "endorsement.other-side" or "endorsement.limit"
            or "appeal.not-open" or "moderation.wrong-state" or "draft.not-configured" => Error.Conflict(code),
        "draft.too-many" => new Error(ErrorKind.TooManyRequests, code),
        "statement.required" => Error.Validation(code, "statement"),
        "draft.source-required" or "draft.source-too-long" => Error.Validation(code, "sourceText"),
        "draft.none" => Error.Validation(code, "fromDraft"),
        "description.unchanged" => Error.Validation(code, "description"),
        "description-url.invalid" => Error.Validation(code, "descriptionUrl"),
        "side.invalid" => Error.Validation(code, "side"),
        "reason.empty" or "reason.too-long" => Error.Validation(code, "text"),
        "description.empty" or "description.too-long" => Error.Validation(code, "description"),
        "threshold.below-minimum" => Error.Validation(code, "threshold"),
        _ => Error.Validation(code),
    };
}
