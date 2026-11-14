using System.Globalization;

namespace Lt2.Notifications.Follows;

/// <summary>Design 8.11.2 as pure functions: which follow events are sent, when, and to whom.</summary>
internal static class FollowRules
{
    /// <summary>A vote held more than this long before the job first saw it is recorded and never sent.</summary>
    public static readonly TimeSpan VoteSendWindow = TimeSpan.FromDays(3);

    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    public static DateOnly VilniusDay(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Vilnius).DateTime);

    public static string PlannedKey(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly PlannedDateOf(string key) => DateOnly.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string VotedKey(long voteId) => voteId.ToString(CultureInfo.InvariantCulture);

    /// <summary>A planned date already in the past when first seen is recorded and never sent (so a backfill sends nothing).</summary>
    public static bool PlannedIsSendable(DateOnly plannedDate, DateTimeOffset firstSeen) => plannedDate >= VilniusDay(firstSeen);

    /// <summary>A vote held more than three days before it was first seen is recorded and never sent.</summary>
    public static bool VoteIsSendable(DateTimeOffset heldAt, DateTimeOffset firstSeen) => firstSeen - heldAt <= VoteSendWindow;

    /// <summary>
    /// At most one "planned" email per bill per Vilnius day: a date that moves again the same day waits for the next day.
    /// </summary>
    public static bool PlannedMaySendNow(DateTimeOffset? lastPlannedSentForBill, DateTimeOffset now) =>
        lastPlannedSentForBill is not { } last || VilniusDay(last) < VilniusDay(now);

    /// <summary>
    /// A waiting planned date is sent only while it is still the bill's current one and not yet past; otherwise it is
    /// superseded (the current date is its own event) and never sent.
    /// </summary>
    public static bool PlannedStillHolds(DateOnly waitingDate, DateOnly? currentDate, DateTimeOffset now) =>
        currentDate == waitingDate && waitingDate >= VilniusDay(now);

    /// <summary>
    /// An event goes to members who followed the bill before it was first seen; a vote, also only to those who followed
    /// before it was held, so nobody is told about an event from before they followed (design 8.11.7).
    /// </summary>
    public static bool IsRecipient(DateTimeOffset followedAt, DateTimeOffset firstSeen, DateTimeOffset? heldAt) =>
        followedAt < firstSeen && (heldAt is not { } held || followedAt < held);

    /// <summary>
    /// An organisation's statement that first became public more than three days before the job first saw it is recorded and
    /// never sent (design 8.14.14, stage B; the window of 8.11.2).
    /// </summary>
    public static bool StatementIsSendable(DateTimeOffset firstPublicAt, DateTimeOffset firstSeen) => firstSeen - firstPublicAt <= VoteSendWindow;

    /// <summary>
    /// A statement goes to those who followed the organisation before the job first saw it and before it became public, so
    /// nobody is told about a statement from before they followed (design 8.14.14: "only those who followed before").
    /// </summary>
    public static bool IsStatementRecipient(DateTimeOffset followedAt, DateTimeOffset firstSeen, DateTimeOffset firstPublicAt) =>
        IsRecipient(followedAt, firstSeen, firstPublicAt);

    /// <summary>A new follow is refused once the person follows <paramref name="max"/> bills, MPs and organisations together.</summary>
    public static bool MayFollowMore(int following, int max) => following < max;
}
