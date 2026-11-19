using Lt2.SharedKernel;

namespace Lt2.Appeals.Domain;

/// <summary>The two sides of a record (design 8.2). Abstaining is not a request to an MP, so it is not offered.</summary>
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

    /// <summary>
    /// An argument is plain text, not blank, and once normalised no longer than <see cref="TooLong"/> allows with the
    /// setting <c>Texts:MaxLength</c> (design 5.6, 8.2, owner 2026-11-16).
    /// </summary>
    public static string? CheckReasonText(string? text, int maxLength)
    {
        var trimmed = NormaliseText(text);
        if (trimmed.Length == 0)
        {
            return "reason.empty";
        }

        return TooLong(trimmed, maxLength) ? "reason.too-long" : null;
    }

    /// <summary>
    /// The one length rule of members' texts (arguments, organisations' statements, MPs' responses): longer than the
    /// setting <c>Texts:MaxLength</c> allows, 0 meaning no limit but the technical ceiling, which holds whatever the setting
    /// says (<see cref="MemberTextLength"/>). Applied to the normalised text.
    /// </summary>
    public static bool TooLong(string normalised, int maxLength) => normalised.Length > MemberTextLength.Allowed(maxLength);

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

    /// <summary>
    /// "Man svarbu" on an argument of either side, with or without a side of one's own (owner, 2026-11-18), but only on
    /// someone else's argument (owner, 2026-11-17: the marks choose the arguments sent to MPs, so an author does not vote for
    /// their own), only with the positions consent (design 6.5: a mark is about a political question, as a side is), and
    /// within the setting's limit, if one is set, which counts every mark of the person on the bill, counted or not
    /// (<see cref="MarkCounts"/>). The giver's side is not an input: it decides only whether the mark counts.
    /// </summary>
    public static string? CheckEndorsement(bool ownReason, bool hasPositionConsent, int endorsementsAlreadyOnBill, int limit)
    {
        if (ownReason)
        {
            return "endorsement.own-reason";
        }

        if (!hasPositionConsent)
        {
            return "consent.required";
        }

        return MayAdd(endorsementsAlreadyOnBill, limit) ? null : "endorsement.limit";
    }

    /// <summary>
    /// A "Man svarbu" counts only while its giver's current side on the bill is the argument's side (owner, 2026-11-18): a
    /// mark given without a side, or on the other side's argument, is kept but counts nowhere (the figure beside the
    /// argument, the order of the arguments, the summaries). Read from the current position when the marks are counted,
    /// never stored, so taking, changing or withdrawing a side only starts or stops a mark counting and deletes none.
    /// </summary>
    public static bool MarkCounts(string? giversSide, string reasonSide) => giversSide is not null && giversSide == reasonSide;

    /// <summary>
    /// Editing an argument (design 8.2, owner 2026-11-17): only a shown or waiting one (a hidden or merged one is not found, as
    /// for withdrawing), only by its author, and only while nobody has reacted to it: no "Man svarbu" and no comment under it
    /// (any, hidden ones included), since those were given to its exact words. The record being open and a writing restriction
    /// are checked as for writing.
    /// </summary>
    public static string? CheckEdit(string status, bool isAuthor, int endorsements, int comments) =>
        !CountedReasonStatuses.Contains(status) ? "reason.not-found"
        : !isAuthor ? "reason.edit-author-only"
        : endorsements > 0 || comments > 0 ? "reason.reacted"
        : null;

    /// <summary>
    /// Nobody reports their own text (owner, 2026-11-17): an argument or a comment they wrote, the statement of an organisation
    /// they represent, the response of the MP they speak for, their own profile (Identity checks that one).
    /// </summary>
    public static string? CheckReport(bool ownText) => ownText ? "report.own" : null;

    /// <summary>
    /// The status an argument has after its author's edit (design 8.2, 8.3): a text pre-screening flags waits for a moderator
    /// again; an unflagged one keeps its status (a waiting one still waits for the moderator who was to look at it).
    /// </summary>
    public static string StatusAfterEdit(string status, bool flagged) => flagged ? ReasonStatuses.Pending : status;

    /// <summary>
    /// Withdrawing a side (design 8.7, owner 2026-11-18: pressing the pressed button again) withdraws only the side named,
    /// when the request names one: a withdrawal of a side the person no longer has (a second press arriving late, another
    /// tab that changed it) changes nothing. Without a side named, whatever side the person has goes.
    /// </summary>
    public static bool WithdrawalApplies(string? currentSide, string? namedSide) =>
        currentSide is not null && (namedSide is null || namedSide == currentSide);
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
/// Group numbers (supporters of the MP's party on a side, design 6.3, 8.4): published only for groups of at least the
/// setting <c>Digest:PartyGroupMinimum</c> (owner, 2026-11-17; 0 = no floor). An empty group is no group and is never shown.
/// </summary>
internal static class GroupRules
{
    public static bool MayPublish(int groupSize, int minimum) => groupSize > 0 && groupSize >= minimum;

    /// <summary>The number to publish for a group, or <c>null</c> when the group is empty or too small to show.</summary>
    public static int? Publishable(int groupSize, int minimum) => MayPublish(groupSize, minimum) ? groupSize : null;

    /// <summary>
    /// A group shown beside a total it is part of (a party's supporters beside a side's count): the group needs at least the
    /// minimum, and so does the rest of the total, or the rest could be worked out by subtraction. A rest of none is fine.
    /// </summary>
    public static int? PublishableBeside(int groupSize, int total, int minimum)
    {
        var rest = total - groupSize;
        return MayPublish(groupSize, minimum) && (rest == 0 || MayPublish(rest, minimum)) ? groupSize : null;
    }
}

/// <summary>
/// Design 8.1 step 6: a record closes when the Seimas takes the final vote on its bill, or when a moderator records that the
/// Seimas rejected it or its initiators withdrew it (owner, 2026-11-19); the counts are frozen then.
/// </summary>
internal static class ClosingRules
{
    /// <summary>Only an open record closes, and only on a final vote; a hidden one stays hidden.</summary>
    public static bool ShouldClose(string status, long? finalVoteId) => status == BillRecordStatuses.Open && finalVoteId is not null;

    /// <summary>
    /// Closes the record with its counts frozen as <paramref name="counts"/> (all four), why, and for a moderator's close the day
    /// and the source; nothing can be taken or changed afterwards and the page shows these numbers.
    /// </summary>
    public static void Close(BillRecord record, SideCounts counts, DateTimeOffset now, string reason, long? closingVoteId, DateOnly? decidedOn, string? sourceUrl)
    {
        record.Status = BillRecordStatuses.Closed;
        record.ClosedAt = now;
        record.ChangedAt = now;
        record.ClosingVoteId = closingVoteId;
        record.ClosedReason = reason;
        record.ClosedDecidedOn = decidedOn;
        record.ClosedSourceUrl = sourceUrl;
        record.ClosedFor = counts.For;
        record.ClosedForOlderThan30Days = counts.ForOlderThan30Days;
        record.ClosedAgainst = counts.Against;
        record.ClosedAgainstOlderThan30Days = counts.AgainstOlderThan30Days;
    }

    /// <summary>
    /// Undoes a moderator's close (design 8.1 step 6): open again, everything of the close cleared; the positions were kept, so
    /// the live counts come back as they are.
    /// </summary>
    public static void Reopen(BillRecord record, DateTimeOffset now)
    {
        record.Status = BillRecordStatuses.Open;
        record.ClosedAt = null;
        record.ChangedAt = now;
        record.ClosingVoteId = null;
        record.ClosedReason = null;
        record.ClosedDecidedOn = null;
        record.ClosedSourceUrl = null;
        record.ClosedFor = null;
        record.ClosedForOlderThan30Days = null;
        record.ClosedAgainst = null;
        record.ClosedAgainstOlderThan30Days = null;
    }

    /// <summary>The counts shown: frozen ones once closed, live ones before.</summary>
    public static SideCounts Shown(SideCounts live, int? closedFor, int? closedForOlder, int? closedAgainst, int? closedAgainstOlder) =>
        closedFor is { } f && closedForOlder is { } fo && closedAgainst is { } a && closedAgainstOlder is { } ao
            ? CountRules.Publish(f, fo, a, ao)
            : live;

    /// <summary>The record's counts were frozen when it closed; its live counts are not needed then.</summary>
    public static bool IsFrozen(BillRecord record) =>
        record.ClosedFor is not null && record.ClosedForOlderThan30Days is not null && record.ClosedAgainst is not null && record.ClosedAgainstOlderThan30Days is not null;

    /// <summary>The counts a record shows: its frozen ones once closed, otherwise <paramref name="live"/>.</summary>
    public static SideCounts Shown(BillRecord record, SideCounts live) =>
        Shown(live, record.ClosedFor, record.ClosedForOlderThan30Days, record.ClosedAgainst, record.ClosedAgainstOlderThan30Days);
}

/// <summary>
/// A moderator's statement of reasons (design 8.3, 6.8: checked where it is stored, not only by the HTTP validators):
/// plain text of at most 1 000 characters once trimmed; required for every action but "Palikti", publishing and confirming.
/// </summary>
internal static class StatementRules
{
    public const int MaxLength = StaffStatements.MaxLength;

    /// <summary>The code of <see cref="StaffStatements.Check"/>: the one rule for every staff statement.</summary>
    public static string? Check(string? statement, bool required) => StaffStatements.Check(statement, required)?.Code;

    /// <summary>The statement as stored: trimmed, or null when none was given.</summary>
    public static string? Normalise(string? statement) => string.IsNullOrWhiteSpace(statement) ? null : statement.Trim();
}

/// <summary>Turns a rule's error code into the module's error.</summary>
internal static class RuleErrors
{
    public static Error Of(string code) => code switch
    {
        "consent.required" or "reason.limit" or "endorsement.limit" or "endorsement.own-reason"
            or "record.not-open" or "moderation.wrong-state" or "draft.not-configured" or "reason.reacted" => Error.Conflict(code),
        "reason.not-found" => Error.NotFound(code),
        "reason.edit-author-only" or "report.own" => Error.Forbidden(code),
        "draft.too-many" => new Error(ErrorKind.TooManyRequests, code),
        "statement.required" or "staff-statement.too-long" => Error.Validation(code, "statement"),
        "draft.source-required" or "draft.source-too-long" => Error.Validation(code, "sourceText"),
        "draft.none" => Error.Validation(code, "fromDraft"),
        "description.unchanged" => Error.Validation(code, "description"),
        "description-url.invalid" => Error.Validation(code, "descriptionUrl"),
        "side.invalid" => Error.Validation(code, "side"),
        "reason.empty" or "reason.too-long" => Error.Validation(code, "text"),
        "description.empty" or "description.too-long" => Error.Validation(code, "description"),
        "threshold.below-minimum" => Error.Validation(code, "threshold"),
        "closing.reason" => Error.Validation(code, "reason"),
        "closing.date-required" or "closing.date-future" => Error.Validation(code, "decidedOn"),
        "closing-url.invalid" => Error.Validation(code, "sourceUrl"),
        _ => Error.Validation(code),
    };
}
