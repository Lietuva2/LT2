using System.Globalization;
using Lt2.SharedKernel;

namespace Lt2.Notifications.Follows;

/// <summary>Design 8.11.2 as pure functions: which follow events are sent, when, and to whom.</summary>
internal static class FollowRules
{
    /// <summary>A vote held more than this long before the job first saw it is recorded and never sent.</summary>
    public static readonly TimeSpan VoteSendWindow = TimeSpan.FromDays(3);

    /// <summary>
    /// How far back the job reads votes, statements and answers: past the three days an event may be old when first seen and
    /// the week it may wait for a weekly email (design 8.24), so the email can still read what it reports.
    /// </summary>
    public static readonly TimeSpan EventLookBack = TimeSpan.FromDays(14);

    /// <summary>
    /// How long the follow job's records are kept (design 8.24, the designer's proposal: 30 days): past the 14-day look-back,
    /// a weekly email's week and the three-day rule, so no pass can still need an older one.
    /// </summary>
    public static readonly TimeSpan EventRetention = TimeSpan.FromDays(30);

    public static string PlannedKey(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly PlannedDateOf(string key) => DateOnly.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string VotedKey(long voteId) => voteId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The key of an MP's follow: the Seimas person id, stable across terms.</summary>
    public static string MpKey(long seimasPersonId) => seimasPersonId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The key of an organisation's follow, and of a statement's event: the id in the Appeals module.</summary>
    public static string IdKey(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// The anchor of a member's text on the bill's page (design 8.25.5): <c>reason-{id}</c> for an argument,
    /// <c>komentaras-{id}</c> for a comment (design 8.23.6).
    /// </summary>
    public static string TextAnchor(string kind, Guid id) =>
        kind == MemberTextKinds.Comment ? "komentaras-" + id.ToString("N", CultureInfo.InvariantCulture) : "reason-" + id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// The key of a comments notice's record (design 8.23.14, no person): the argument's id (its first 80 bits, as 20 hexadecimal
    /// digits) and the run's moment in milliseconds, within the 40 characters the key has.
    /// </summary>
    public static string CommentKey(Guid reasonId, DateTimeOffset run) =>
        reasonId.ToString("N", CultureInfo.InvariantCulture)[..20] + ":" + run.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>How far back the comments notice's first run looks (design 8.23.14): the last hour.</summary>
    public static readonly TimeSpan CommentsFirstLookBack = TimeSpan.FromHours(1);

    /// <summary>
    /// How long before a run its comments' window ends (design 8.23.14): a comment is stamped when it is written and becomes
    /// visible when its transaction commits, so the window stops this far behind the run and the next one starts there.
    /// </summary>
    public static readonly TimeSpan CommentsMargin = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The comments one run of the notice reads (design 8.23.9, 8.23.14): from where the last run that told someone stopped
    /// (or the last hour on the first run) to the run's moment less <see cref="CommentsMargin"/>; the windows of successive runs
    /// meet without a gap or an overlap; never more than three days back, as nothing older is sent (design 8.11.2).
    /// </summary>
    public static (DateTimeOffset Since, DateTimeOffset Until) CommentsWindow(DateTimeOffset? lastTold, DateTimeOffset now)
    {
        var since = (lastTold ?? now - CommentsFirstLookBack) - CommentsMargin;
        return (since < now - VoteSendWindow ? now - VoteSendWindow : since, now - CommentsMargin);
    }
    /// <summary>A planned date already in the past when first seen is recorded and never sent (so a backfill sends nothing).</summary>
    public static bool PlannedIsSendable(DateOnly plannedDate, DateTimeOffset firstSeen) => plannedDate >= VilniusTime.DateOf(firstSeen);

    /// <summary>A vote held more than three days before it was first seen is recorded and never sent.</summary>
    public static bool VoteIsSendable(DateTimeOffset heldAt, DateTimeOffset firstSeen) => firstSeen - heldAt <= VoteSendWindow;

    /// <summary>
    /// At most one "planned" email per bill per Vilnius day: a date that moves again the same day waits for the next day.
    /// </summary>
    public static bool PlannedMaySendNow(DateTimeOffset? lastPlannedSentForBill, DateTimeOffset now) =>
        lastPlannedSentForBill is not { } last || VilniusTime.DateOf(last) < VilniusTime.DateOf(now);

    /// <summary>
    /// A waiting planned date is sent only while it is still the bill's current one and not yet past; otherwise it is
    /// superseded (the current date is its own event) and never sent.
    /// </summary>
    public static bool PlannedStillHolds(DateOnly waitingDate, DateOnly? currentDate, DateTimeOffset now) =>
        currentDate == waitingDate && waitingDate >= VilniusTime.DateOf(now);

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

    /// <summary>
    /// A followed member's argument (design 8.25.5) that became public more than three days before the job first saw it is
    /// recorded and never sent, as an organisation's statement (the window of 8.11.2).
    /// </summary>
    public static bool MemberArgumentIsSendable(DateTimeOffset publishedAt, DateTimeOffset firstSeen) => firstSeen - publishedAt <= VoteSendWindow;

    /// <summary>
    /// A followed member's argument goes to those who followed the member before it became public and before the job first saw
    /// it (design 8.25.5: nobody hears of a text from before they followed), never to its author.
    /// </summary>
    public static bool IsMemberArgumentRecipient(Guid follower, Guid author, DateTimeOffset followedAt, DateTimeOffset firstSeen, DateTimeOffset publishedAt) =>
        follower != author && IsRecipient(followedAt, firstSeen, publishedAt);

    /// <summary>
    /// The key of an MP's answer as an event (design 8.20.1, 8.24): the Seimas person number and the moment the stance now shown
    /// became public, so a stance changed again is a new event and a changed explanation is none.
    /// </summary>
    public static string ResponseKey(long seimasPersonId, DateTimeOffset announcedAt) =>
        seimasPersonId.ToString(CultureInfo.InvariantCulture) + "@" + announcedAt.UtcTicks.ToString(CultureInfo.InvariantCulture);

    /// <summary>The person number and the moment of <see cref="ResponseKey"/>.</summary>
    public static (long SeimasPersonId, DateTimeOffset AnnouncedAt) ResponseOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var at = key.IndexOf('@', StringComparison.Ordinal);
        return (long.Parse(key[..at], CultureInfo.InvariantCulture), new DateTimeOffset(long.Parse(key[(at + 1)..], CultureInfo.InvariantCulture), TimeSpan.Zero));
    }

    /// <summary>
    /// An answer the job first sees more than three days after its stance became public is recorded and never sent, as a vote
    /// (design 8.24: the three-day rule is about when the job first sees an event).
    /// </summary>
    public static bool ResponseIsSendable(DateTimeOffset announcedAt, DateTimeOffset firstSeen) => firstSeen - announcedAt <= VoteSendWindow;

    /// <summary>The <c>Follows:ResponsesAt</c> setting as a time, the hour of the daily and weekly emails (design 8.24); an unreadable value falls back to the default, 08:00.</summary>
    public static TimeOnly ResponsesAt(string setting) =>
        TimeOnly.TryParseExact(setting, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : new TimeOnly(8, 0);

    /// <summary>
    /// The answers one follower of a bill is told about (design 8.20.1): those whose stance became public after the person
    /// followed the bill (never something from before they followed), leaving out the MP the person speaks for as the MP's own
    /// account or an office member (design 8.20.1: none about their own MP's responses). Empty: no email for that person.
    /// </summary>
    public static IReadOnlyList<PublicResponseFacts> ResponsesFor(DateTimeOffset followedAt, long? speaksFor, IEnumerable<PublicResponseFacts> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        return responses.Where(r => followedAt < r.AnnouncedAt && r.SeimasPersonId != speaksFor).ToList();
    }
}
