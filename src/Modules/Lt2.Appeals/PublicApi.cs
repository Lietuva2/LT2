using Lt2.SharedKernel;

namespace Lt2.Appeals;

// What the Appeals module answers and accepts. Codes are lower-case English words, stable for the API: side for |
// against; record status open | closed | rejected (hidden by a moderator); reason status pending | published | hidden |
// merged; threshold set by default | moderator; description source moderator | ai-draft | ai; report target record (its
// description) | reason | organisation | statement.
//
// Design 8.12: every active bill is open. A bill's record (the record) is created when first needed and has no
// initiator; the answers for an active bill without one are the empty open state, not "not found".
//
// Privacy (design 6.1, 6.3): nothing here carries another person's position, and nothing tells who took part first on a
// bill. The only side of a person any operation returns is the caller's own, in MyBillRecord.

/// <summary>
/// Both sides together, each with the count for accounts older than 30 days (design 6.6). Built only through the
/// module's count rule, so the two numbers never travel apart.
/// </summary>
public sealed record SideCounts(int For, int ForOlderThan30Days, int Against, int AgainstOlderThan30Days)
{
    public static readonly SideCounts None = new(0, 0, 0, 0);

    public int Total => For + Against;
}

/// <summary>The threshold as everyone sees it (design 8.12.2).</summary>
/// <param name="Value">The threshold that applies today: the bill's own or the default, never below the minimum.</param>
/// <param name="SetBy"><c>default</c> or <c>moderator</c>.</param>
/// <param name="Note">A moderator's public statement for the bill's own number.</param>
/// <param name="Participants">Positions on both sides together.</param>
/// <param name="EnteredSummaries">The bill has entered a summary sent to MPs.</param>
public sealed record ThresholdInfo(int Value, string SetBy, string? Note, int Participants, bool Reached, bool EnteredSummaries);

/// <summary>
/// A public argument. <paramref name="AuthorName"/> is <c>null</c> when the author gave no display name;
/// <paramref name="AuthorProfileId"/> is the public profile id of an author whose profile is public (design 8.25.4), null for
/// everyone else.
/// </summary>
public sealed record ReasonInfo(Guid Id, string Side, string Text, string? AuthorName, int Endorsements, string Status, DateTimeOffset CreatedAt, string? AuthorProfileId = null)
{
    /// <summary>How many comments it shows (design 8.23.6); set only in the bill's public answer.</summary>
    public int Comments { get; init; }
}

/// <summary>The card beside the name of a member with a public profile (design 8.25.4): once per person per bill's answer.</summary>
public sealed record AuthorCard(string ProfileId, string Name, string? About);

/// <summary>How many published arguments each side of a bill's record has (design 8.12.1): what "Rodoma 2 iš 15" counts.</summary>
public sealed record ReasonTotals(int For, int Against)
{
    public static readonly ReasonTotals None = new(0, 0);

    public int Of(string side) => side == Sides.For ? For : Against;
}

/// <summary>
/// One page of a side's published arguments (design 8.12.1, owner 2026-11-19), in the page's order: the counted "Man svarbu"
/// marks, most first, then the oldest first. <paramref name="Total"/> is the side's count, <paramref name="NextCursor"/> where
/// the next page starts (null after the last), <paramref name="Cards"/> the profiles among this page's names.
/// </summary>
public sealed record ReasonPage(string Side, IReadOnlyList<ReasonInfo> Items, int Total, string? NextCursor, IReadOnlyList<AuthorCard> Cards);

/// <summary>One published argument with its bill (a link to it, design 8.12.1), and its author's card when they have a profile.</summary>
public sealed record ReasonOnBill(string BillNumber, ReasonInfo Reason, IReadOnlyList<AuthorCard> Cards);

/// <summary>
/// A bill as people take part on it (design 8.12.1), the same for everyone. <paramref name="Id"/> is null while the bill
/// has no record (nobody has taken part and no moderator has acted); the counts are then zero.
/// </summary>
/// <param name="Status"><c>open</c>, <c>closed</c>, or <c>rejected</c> (taking part closed by the moderators).</param>
/// <param name="Description">What the bill is about (design 8.13); empty while there is none.</param>
/// <param name="DescriptionSource"><c>moderator</c>, <c>ai-draft</c> or <c>ai</c>; null without a description.</param>
/// <param name="DescriptionUrl">The official text the description was made from, when known (design 8.13).</param>
/// <param name="DescriptionAt">When the description's text was made or last changed; null without one or when unknown.</param>
/// <param name="HiddenStatement">For a hidden record: the moderators' statement; nothing else of it is shown.</param>
/// <param name="Reasons">The first page of each side's arguments (<see cref="ReasonPages.First"/> each, design 8.12.1); the rest by <see cref="IAppealsModule.ReasonsAsync"/>.</param>
/// <param name="ReasonTotals">How many published arguments each side has.</param>
/// <param name="ClosedReason">
/// Why a closed record closed (design 8.1 step 6): <c>vote</c> (the final vote, <paramref name="ClosingVoteId"/>), <c>rejected</c>
/// or <c>withdrawn</c> (a moderator recorded that the Seimas rejected the bill or its initiators withdrew it, on
/// <paramref name="ClosedDecidedOn"/>, as <paramref name="ClosedSourceUrl"/> on lrs.lt says); null while open.
/// </param>
public sealed record BillRecordInfo(
    Guid? Id,
    BillFacts Bill,
    string Status,
    string Description,
    string? DescriptionSource,
    string? DescriptionUrl,
    DateTimeOffset? DescriptionAt,
    SideCounts Counts,
    ThresholdInfo Threshold,
    IReadOnlyList<ReasonInfo> Reasons,
    SurgeInfo? Surge,
    long? ClosingVoteId,
    DateTimeOffset? ClosedAt,
    PlannedDate? Planned,
    string? HiddenStatement,
    IReadOnlyList<AuthorCard>? Cards = null,
    ReasonTotals? ReasonTotals = null,
    string? ClosedReason = null,
    DateOnly? ClosedDecidedOn = null,
    string? ClosedSourceUrl = null,
    string? PlainTitle = null);

/// <summary>The arguments' pages (design 8.12.1, owner 2026-11-19): the record carries the first of each side, the rest come a page at a time.</summary>
public static class ReasonPages
{
    /// <summary>Each side's arguments shown first, carried in the bill's answer.</summary>
    public const int First = 2;

    /// <summary>The arguments "Rodyti daugiau argumentų" adds (owner, 2026-11-19: a list of texts, 20 a press).</summary>
    public const int Step = Lt2.SharedKernel.ListPaging.TextStep;

    /// <summary>The most one page holds.</summary>
    public const int Max = 100;

    /// <summary>A page's size: as asked, from 1 to <see cref="Max"/>; a <see cref="Step"/> when not asked.</summary>
    public static int Limit(int? asked) => asked is { } n ? Math.Clamp(n, 1, Max) : Step;

    /// <summary>Where a page starts: the cursor is the number of arguments before it; anything else starts at the first.</summary>
    public static int Offset(string? cursor) =>
        int.TryParse(cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>The next page's cursor, or null when the page holds the side's last argument.</summary>
    public static string? NextCursor(int offset, int taken, int total) =>
        offset + taken < total ? (offset + taken).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
}

/// <summary>
/// A bill as the agenda shows it (design 8.12.5): its record's public numbers, or zeros for an active bill nobody took
/// part in. Nothing about any person.
/// </summary>
/// <param name="RecordId">The record; null while the bill has none.</param>
/// <param name="Status"><c>open</c>, <c>closed</c>, or <c>rejected</c>.</param>
/// <param name="Arguments">The record's published arguments, for the agenda's order by activity (design 8.11.1); 0 for a hidden record.</param>
public sealed record BillRecordState(string BillNumber, Guid? RecordId, string Status, SideCounts Counts, ThresholdInfo Threshold, int Arguments = 0);

/// <summary>
/// The surge note (design 6.6): in the last <paramref name="WindowHours"/> hours, <paramref name="SharePercent"/> % of the
/// positions taken came from accounts that became members within the same hours. An aggregate; never a person.
/// </summary>
public sealed record SurgeInfo(int SharePercent, int WindowHours, int RecentPositions, int FromNewAccounts);

/// <summary>
/// A bill in <c>/projektai</c> (design 8.12.5); <paramref name="Id"/> null while it has no record. <paramref name="Arguments"/>
/// is how many published arguments it has; <paramref name="LastActivityAt"/> its last activity (owner, 2026-11-17), null while
/// there was none.
/// </summary>
public sealed record BillListItem(
    Guid? Id, BillFacts Bill, string Status, SideCounts Counts, ThresholdInfo Threshold, PlannedDate? Planned, int Arguments, DateTimeOffset? LastActivityAt = null,
    string? PlainTitle = null);

public sealed record BillListPage(IReadOnlyList<BillListItem> Items, int Total, string? NextCursor);

/// <summary>
/// What <c>/projektai</c> asks for (design 8.12.5): active bills (or, with <paramref name="Closed"/>, closed records), a
/// search by number or title, only those planned soon, the order (<see cref="BillSorts"/>; activity by default) and the page:
/// <paramref name="Limit"/> bills (20 when none; 1 to 200) from <paramref name="Cursor"/> on.
/// </summary>
public sealed record BillListQuery(string? Search = null, bool PlannedOnly = false, bool Closed = false, string? Cursor = null, string? Sort = null, int? Limit = null);

/// <summary>The orders of <c>/projektai</c> (design 8.12.5, decided 2026-11-14; <see cref="Latest"/> owner, 2026-11-17).</summary>
public static class BillSorts
{
    public const string Activity = "activity";
    public const string Participants = "participants";
    public const string Arguments = "arguments";
    public const string Planned = "planned";

    /// <summary>"Naujausia veikla": the latest activity first, bills without any last.</summary>
    public const string Latest = "latest";

    public static readonly IReadOnlyList<string> All = [Activity, Participants, Arguments, Planned, Latest];

    /// <summary>An unknown or missing sort is activity, the default.</summary>
    public static string Of(string? sort) => sort is not null && All.Contains(sort) ? sort : Activity;
}

/// <summary>
/// The caller's own part on one bill: their side (only ever their own), their "Man svarbu" marks and arguments, and what
/// they may still do. The same empty answer for an active bill without a record.
/// </summary>
public sealed record MyBillRecord(
    string? Side,
    IReadOnlyList<Guid> Endorsed,
    int? EndorsementsLeft,
    IReadOnlyList<ReasonInfo> MyReasons,
    int? ReasonsLeft,
    bool HasPositionConsent)
{
    /// <summary>The person's own arguments they may edit now (design 8.2, owner 2026-11-17: until someone reacts).</summary>
    public IReadOnlyList<Guid> Editable { get; init; } = [];

    /// <summary>
    /// The moderator's statement beside each of the person's own hidden arguments in <see cref="MyReasons"/> (owner, 2026-11-17:
    /// the author sees their hidden argument in its place with it); only in this never-cached answer.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> HiddenStatements { get; init; } = new Dictionary<Guid, string>();

    /// <summary>
    /// The person's own marks in <see cref="Endorsed"/> that do not count now (owner, 2026-11-18): given without a side, or on
    /// the other side's argument. They show pressed with a short note; only in this never-cached answer.
    /// </summary>
    public IReadOnlyList<Guid> Uncounted { get; init; } = [];
}

/// <summary>
/// The numbers the forms need (design 8.2, 8.12.2): the default threshold, the minimum, and the per-person limits (0 = no
/// limit, the default). <paramref name="AiSummaries"/>: the summary job can make descriptions (design 8.13: a provider is
/// configured, official texts can be read and <c>Ai:SummariesPerDay</c> is not 0); while false nothing promises one.
/// </summary>
public sealed record AppealRulesInfo(int ThresholdDefault, int ThresholdMinimum, int TextMaxLength, int ReasonsPerPerson, int EndorsementsPerPerson, int DescriptionMaxLength, bool AiSummaries)
{
    /// <summary>The setting <c>Appeals:MinReasonWords</c>: fewer words make an argument too short (owner, 2026-11-17); 0 = off.</summary>
    public int MinReasonWords { get; init; }
}

public sealed record NewReason(string Side, string Text);

/// <summary>A new argument: <c>pending</c> when pre-screening flagged it for a moderator, otherwise <c>published</c>.</summary>
public sealed record ReasonCreated(Guid Id, string Status);

public sealed record NewReport(string TargetKind, Guid TargetId, string Ground, string? Text);

public sealed record QueueReport(Guid Id, string Ground, string? Text, DateTimeOffset CreatedAt);

/// <summary>
/// A bill's record in the moderators' queue (design 8.12.3): reported, flagged about its date, surging, held, or under its
/// threshold on the agenda with at least one participant.
/// </summary>
public sealed record QueueRecord(
    Guid Id,
    BillFacts Bill,
    string Description,
    string? DescriptionSource,
    int Threshold,
    IReadOnlyList<QueueReport> Reports,
    SurgeInfo? Surge,
    PlannedDate? Planned,
    IReadOnlyList<QueueFlag> DateFlags,
    bool UnderThresholdOnAgenda,
    bool Held,
    int Participants,
    string? DescriptionUrl,
    string Status = BillRecordStatuses.Open);

/// <summary>
/// What a moderator's controls on the bill's page need (design 8.12.3): the state that decides which actions apply, and
/// the bill's waiting and published arguments with what the queue shows for each (decided 2026-11-14: arguments are
/// moderated on the bill's page too). <paramref name="Id"/> is null while the bill has no record; an action creates it.
/// <paramref name="DescriptionUrl"/> is the address the description form starts from, and
/// <paramref name="DescriptionReports"/> the open reports about the description as the queue shows them (decided by the
/// owner on 2026-11-14). Holds no position and nothing about who reported.
/// </summary>
public sealed record ModerationBillState(
    Guid? Id, string BillNumber, string Status, string Description, string? DescriptionSource, int Threshold, string ThresholdSetBy, bool Held,
    PlannedDate? Planned, bool HasOpenItems, IReadOnlyList<QueueReason> Reasons, string? DescriptionUrl, IReadOnlyList<QueueReport> DescriptionReports,
    string? ClosedReason = null, string? PlainTitle = null);

/// <summary>
/// A participant's flag about the planned date (design 8.4), with the date they say is right when they gave one (owner,
/// 2026-11-17). Who flagged is not shown.
/// </summary>
public sealed record QueueFlag(Guid Id, string Kind, string Link, string? Note, DateTimeOffset CreatedAt, DateOnly? Date = null);

/// <summary>A flag as sent: the kind, the link to the source, an optional note and the optional right date.</summary>
public sealed record NewFlag(string Kind, string Link, string? Note, DateOnly? Date = null);

/// <summary>An argument for a moderator: flagged, reported, or not yet seen.</summary>
public sealed record QueueReason(
    Guid Id,
    Guid RecordId,
    BillFacts Bill,
    string Side,
    string Text,
    string? AuthorName,
    string Status,
    bool Seen,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReport> Reports,
    IReadOnlyList<ReasonInfo> MergeCandidates,
    DateTimeOffset CreatedAt,
    string RecordStatus = BillRecordStatuses.Open)
{
    /// <summary>How many comments go with it when it is merged (design 8.23.7: the confirmation says so).</summary>
    public int Comments { get; init; }
}

/// <summary>
/// The moderators' queue (design 8.12.3): bills' records that need a look, and arguments; at most a hundred of each, and how
/// many there are in all (owner, 2026-11-18: "Rodoma 100 iš N").
/// </summary>
public sealed record ModerationItems(IReadOnlyList<QueueRecord> Records, IReadOnlyList<QueueReason> Reasons)
{
    public int RecordsTotal { get; init; }

    public int ReasonsTotal { get; init; }
}

/// <summary>
/// A moderator writes or corrects a bill's description (design 8.12.3, 8.13); <c>FromDraft</c>: it started from an AI draft.
/// <c>DescriptionUrl</c>: the official text's address, which becomes the record's; empty or null clears it (decided by the
/// owner on 2026-11-14).
/// </summary>
public sealed record ModeratorDescription(string Description, string Statement, bool FromDraft, string? DescriptionUrl = null, string? PlainTitle = null);

/// <summary>
/// One action on a bill's description and readable title (design 8.13.1), public: <c>ChangedAt</c> is shared by what one action changed.
/// <c>OldText</c>/<c>NewText</c> mean something only when <c>TextChanged</c> (null new text: removed; null old: the first one);
/// <c>OldTitle</c>/<c>NewTitle</c> only when <c>TitleChanged</c> (null: none).
/// </summary>
public sealed record DescriptionRevision(
    DateTimeOffset ChangedAt, string? OldText, string? NewText, bool TextChanged, string? OldTitle, string? NewTitle, bool TitleChanged);

/// <summary>A bill's readable title with the source of the description it is kept with (design 8.13.1).</summary>
public sealed record PlainTitleInfo(string Title, string Source);

/// <summary>An AI draft (design 8.11.4): the description and, when the answer had a valid one, the readable title.</summary>
public sealed record DescriptionDraft(string Draft, string? PlainTitle);

/// <summary>
/// The Appeals module (design 8). Every operation that acts for a person takes their account id; moderation operations
/// take the moderator. No operation returns another person's position.
/// </summary>
public interface IAppealsModule
{
    Task<AppealRulesInfo> RulesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Active bills (design 8.12.5) in the asked order, activity by default; or the closed records.
    /// </summary>
    Task<BillListPage> ListAsync(BillListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// The bill as people take part on it, the same for everyone: its record, or the empty open state for an active bill
    /// without one. <c>bill.not-found</c> for an unknown bill, <c>record.not-found</c> for a finished bill nobody took
    /// part in (the page shows the bill only).
    /// </summary>
    Task<Result<BillRecordInfo>> GetAsync(string billNumber, CancellationToken cancellationToken);

    /// <summary>
    /// One page of a side's published arguments (design 8.12.1, owner 2026-11-19), public and the same for everyone, in the
    /// page's order; <c>side.invalid</c> for another side, <c>record.not-found</c> for a bill without a shown record.
    /// </summary>
    Task<Result<ReasonPage>> ReasonsAsync(string billNumber, string side, string? cursor, int? limit, CancellationToken cancellationToken);

    /// <summary>One published argument of a shown record (a link to it); <c>reason.not-found</c> for any other, the same for all.</summary>
    Task<Result<ReasonOnBill>> ReasonAsync(Guid reasonId, CancellationToken cancellationToken);

    Task<Result<MyBillRecord>> MineAsync(string billNumber, Guid userId, CancellationToken cancellationToken);

    /// <summary>The description's and the readable title's edits (oldest first, one entry per action), public; no editor is named. Empty for a bill without a record.</summary>
    Task<Result<IReadOnlyList<DescriptionRevision>>> HistoryAsync(string billNumber, CancellationToken cancellationToken);

    /// <summary>
    /// The readable titles (design 8.13.1) of the given bills, by number, one query: only bills whose record is open or closed and has
    /// one, with the source of its description. For composing other modules' lists.
    /// </summary>
    Task<IReadOnlyDictionary<string, PlainTitleInfo>> PlainTitlesAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);

    /// <summary>
    /// Takes or changes the person's side. The value is true when the person had no side on the bill before (a first side,
    /// for which the host follows the bill, design 8.11.2), false when their side was kept or changed.
    /// </summary>
    Task<Result<bool>> TakePositionAsync(string billNumber, Guid userId, string side, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws the person's side (design 8.7, owner 2026-11-18: pressing the pressed button again). With
    /// <paramref name="side"/> only that side is withdrawn: a withdrawal of a side the person no longer has changes nothing.
    /// Their "Man svarbu" marks stay and stop counting.
    /// </summary>
    Task<Result<Unit>> ClearPositionAsync(string billNumber, Guid userId, string? side, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ReasonInfo>>> FindSimilarAsync(string billNumber, NewReason request, CancellationToken cancellationToken);

    Task<Result<ReasonCreated>> WriteReasonAsync(string billNumber, Guid userId, NewReason request, CancellationToken cancellationToken);

    /// <summary>
    /// The author edits their argument's text and side while the record is open and nobody has reacted (design 8.2, owner
    /// 2026-11-17): <c>reason.reacted</c> after a "Man svarbu" or a comment; <c>reason.edit-author-only</c> for someone else's.
    /// The status after the edit: <c>pending</c> when pre-screening flagged the new text.
    /// </summary>
    Task<Result<ReasonCreated>> EditReasonAsync(Guid reasonId, Guid userId, NewReason request, CancellationToken cancellationToken);

    Task<Result<Unit>> EndorseAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken);

    Task<Result<Unit>> RemoveEndorsementAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The author withdraws their own argument while the bill is open (design 8.2).</summary>
    Task<Result<Unit>> WithdrawReasonAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The caller's own writing restriction that holds now, or <c>null</c> (design 8.3).</summary>
    Task<MyRestriction?> MyRestrictionAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<Unit>> ReportAsync(Guid userId, NewReport request, CancellationToken cancellationToken);

    /// <summary>"The date is wrong" or "this is being voted sooner", with a link (design 8.4); participants only.</summary>
    Task<Result<Unit>> FlagDateAsync(string billNumber, Guid userId, NewFlag request, CancellationToken cancellationToken);

    /// <summary>Each given bill's public numbers (design 8.12.5); bills not in the catalogue are missing.</summary>
    Task<IReadOnlyDictionary<string, BillRecordState>> BillStatesAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);

    /// <summary>
    /// The caller's own side on each given bill that has one, in one read (design 8.7, the front page): the person's own,
    /// never anyone else's. A bill whose record the moderators hid is left out, as its page shows no taking part.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> MySidesAsync(Guid userId, IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);

    /// <summary>
    /// Design 8.26.2: every closed record (hidden ones are left out) with its closing vote and its frozen counts, the latest
    /// closed first. Aggregates only.
    /// </summary>
    Task<IReadOnlyList<ClosedRecordFacts>> ClosedRecordsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Design 8.26.4: the caller's own side on every closed record (hidden ones are left out), with the record's closing vote.
    /// The person's own, never anyone else's; read per request and never stored elsewhere.
    /// </summary>
    Task<IReadOnlyList<MyClosedSide>> MyClosedSidesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Design 8.26.6: a short text that changes whenever the set of closed records changes (one closes, is hidden or
    /// restored), so a kept copy of the analytics is made again. No person in it.
    /// </summary>
    Task<string> ClosedRecordsVersionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A moderator's close of a record (design 8.1 step 6): <paramref name="Reason"/> <c>rejected</c> or <c>withdrawn</c>, the day of
/// that decision (not in the future), the source on lrs.lt and the statement.
/// </summary>
public sealed record RecordClosure(string Reason, DateOnly DecidedOn, string SourceUrl, string Statement);

/// <summary>A closed record as the analytics page compares it (design 8.26.2): the vote that closed it and its frozen counts.</summary>
public sealed record ClosedRecordFacts(string BillNumber, long ClosingVoteId, DateTimeOffset ClosedAt, SideCounts Counts);

/// <summary>The caller's own side (<c>for</c> or <c>against</c>) on a closed record, and the vote that closed it (design 8.26.4).</summary>
public sealed record MyClosedSide(string BillNumber, long ClosingVoteId, string Side);

/// <summary>Moderation after the fact (design 8.12.3). Every action is audited; an argument's author is told why.</summary>
public interface IAppealsModeration
{
    Task<ModerationItems> QueueAsync(CancellationToken cancellationToken);

    /// <summary>"Palikti": keeps a reported or flagged record as it is; its open reports and date flags close.</summary>
    Task<Result<Unit>> KeepRecordAsync(Guid recordId, string? statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Hides a bill's record: taking part on the bill closes; sides and arguments are kept unseen (design 8.12.3).</summary>
    Task<Result<Unit>> HideRecordAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>
    /// Closes an open record because the Seimas rejected the bill or its initiators withdrew it (design 8.1 step 6, owner
    /// 2026-11-19): under the bill's lock, the counts frozen, the reason, the day and the lrs.lt source kept and shown; audited
    /// <c>record.closed</c>. Followers hear of it through the follow emails. <c>moderation.wrong-state</c> on a record that is not
    /// open or whose bill's final vote is recorded.
    /// </summary>
    Task<Result<Unit>> CloseRecordAsync(Guid recordId, RecordClosure closure, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>
    /// Undoes a moderator's close (design 8.1 step 6): the record is open again with its live counts; never one closed by a
    /// vote (<c>moderation.wrong-state</c>). Audited <c>record.reopened</c>; nobody is told.
    /// </summary>
    Task<Result<Unit>> ReopenRecordAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>
    /// Writes or corrects the description of an active bill (design 8.12.3, 8.13), creating the bill's record if needed:
    /// shown at once, recorded in the public history, audited with the required statement.
    /// </summary>
    Task<Result<Unit>> WriteDescriptionAsync(string billNumber, ModeratorDescription request, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms an AI summary nobody has read (design 8.13): the text stays and its label becomes "Sukurta DI · taisė
    /// žmonės" (<c>ai-draft</c>). Audited with the optional note; open reports on the description close.
    /// </summary>
    Task<Result<Unit>> ConfirmDescriptionAsync(string billNumber, string? note, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the bill's description, whatever made it (design 8.13), with a required statement: the bill then says it has
    /// none, the removal is in the public history, and the summary job never writes a description on the bill again.
    /// </summary>
    Task<Result<Unit>> RemoveDescriptionAsync(string billNumber, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>The state a moderator's controls on the bill's page need; <c>bill.not-found</c> for an unknown bill.</summary>
    Task<Result<ModerationBillState>> BillStateAsync(string billNumber, CancellationToken cancellationToken);

    /// <summary>
    /// The bill's record for a moderator's action on it (design 8.12.1: an action creates it): found or created on an
    /// active bill. Creating it alone shows nothing new and is not an action.
    /// </summary>
    Task<Result<Guid>> EnsureRecordAsync(string billNumber, CancellationToken cancellationToken);

    /// <summary>Hidden records and reasons, newest hide first, with who hid them, when and why (design 8.3).</summary>
    Task<HiddenItems> HiddenAsync(CancellationToken cancellationToken);

    /// <summary>Brings a hidden record back as it was (design 8.12.3): open, or closed if it had closed.</summary>
    Task<Result<Unit>> RestoreRecordAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Brings a hidden reason back as published, while its bill is open; its marks are gone (design 8.3).</summary>
    Task<Result<Unit>> RestoreReasonAsync(Guid reasonId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Sets the bill's own threshold with a public statement (design 8.12.2); it stays until a moderator changes it.</summary>
    Task<Result<Unit>> SetThresholdAsync(Guid recordId, int threshold, string statement, StaffActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> PublishReasonAsync(Guid reasonId, string? statement, StaffActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> HideReasonAsync(Guid reasonId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> MergeReasonAsync(Guid reasonId, Guid intoReasonId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Holds sending for the bill (design 8.4): it enters no summary until released.</summary>
    Task<Result<Unit>> HoldAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Releases a held bill: summaries take it again by their rules.</summary>
    Task<Result<Unit>> ReleaseAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Corrects the planned date or stage of the bill (design 8.4); closes its open date flags.</summary>
    Task<Result<Unit>> SetPlannedAsync(Guid recordId, DateOnly date, string? stage, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>
    /// What a moderator sees before restricting the author of an argument (design 8.3): the display name, their arguments,
    /// earlier moderation actions on them and earlier restrictions. Never the address, a side or a mark.
    /// </summary>
    Task<Result<AuthorView>> AuthorOfReasonAsync(Guid reasonId, CancellationToken cancellationToken);

    /// <summary>
    /// Restricts the author of the argument from writing until a day (inclusive) or, with <paramref name="until"/> null,
    /// until lifted; audited and emailed with the moderators' <paramref name="contact"/> address. Returns the restriction id.
    /// </summary>
    Task<Result<Guid>> RestrictAuthorOfReasonAsync(Guid reasonId, DateOnly? until, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>Restrictions that hold now, for the moderators' screen.</summary>
    Task<IReadOnlyList<RestrictionInfo>> RestrictionsAsync(CancellationToken cancellationToken);

    /// <summary>Lifts a restriction early (a moderator or an administrator); audited and emailed.</summary>
    Task<Result<Unit>> LiftRestrictionAsync(Guid restrictionId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>Whether an AI draft of a description can be asked for: a provider is configured (design 8.11.4).</summary>
    bool DraftAvailable { get; }

    /// <summary>
    /// An AI draft of a neutral description from the pasted official text (design 8.11.4): <c>draft.not-configured</c>
    /// without a provider; an answer not in the fixed form is <c>draft.failed</c>, one saying the source is not enough has no title.
    /// Audited (who, which bill, when; not the text), at most 20 an hour per moderator.
    /// </summary>
    Task<Result<DescriptionDraft>> DraftDescriptionAsync(string billNumber, string sourceText, StaffActor moderator, CancellationToken cancellationToken);
}

/// <summary>
/// A hidden record for the moderators' list (design 8.12.3). <paramref name="RestoresAs"/> is the status "Atkurti" gives it.
/// </summary>
public sealed record HiddenRecord(
    Guid Id, BillFacts Bill, string Description, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, string RestoresAs);

/// <summary>A hidden reason for the moderators' list; it can be restored only while its bill's record is open.</summary>
public sealed record HiddenReason(
    Guid Id, BillFacts Bill, string Side, string Text, string? AuthorName, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, bool Restorable);

public sealed record HiddenItems(IReadOnlyList<HiddenRecord> Records, IReadOnlyList<HiddenReason> Reasons);

/// <summary>The caller's own writing restriction (design 8.3): its last day (null = until lifted) and the statement.</summary>
public sealed record MyRestriction(DateOnly? Until, string Statement);

/// <summary>An argument as a moderator sees it on the author's screen; its side is the argument's, public anyway.</summary>
public sealed record AuthorReason(Guid Id, string BillNumber, string Side, string Text, string Status, DateTimeOffset CreatedAt);

/// <summary>An earlier moderation action on the person's arguments or restrictions.</summary>
public sealed record ModerationEntry(string TargetKind, string Action, string? Statement, DateTimeOffset At);

/// <summary>A writing restriction for the moderators' screens; <c>Holds</c> is whether it applies now.</summary>
public sealed record RestrictionInfo(Guid Id, string? DisplayName, DateOnly? Until, string Statement, DateTimeOffset SetAt, bool Holds, DateTimeOffset? LiftedAt, string? LiftStatement);

/// <summary>The author's screen for a moderator (design 8.3). No address, no side, no mark.</summary>
public sealed record AuthorView(
    string? DisplayName,
    IReadOnlyList<AuthorReason> Reasons,
    IReadOnlyList<ModerationEntry> Actions,
    IReadOnlyList<RestrictionInfo> Restrictions);

/// <summary>
/// What one run of the summary job did (design 8.13). <paramref name="Status"/>: <c>off</c> (<c>Ai:SummariesPerDay</c> is
/// 0), <c>no-reader</c> (no official text can be read), <c>no-provider</c> (no AI provider configured), <c>done</c>.
/// </summary>
public sealed record SummaryRun(string Status, int Asked, int Made);

/// <summary>
/// The summary job (design 8.13), run by the worker: AI summaries for active bills without a description, from their
/// official text, shown at once with "Sukurta DI · žmonių neperžiūrėta".
/// </summary>
public interface IDescriptionSummaries
{
    Task<SummaryRun> RunAsync(CancellationToken cancellationToken);
}

/// <summary>Scheduled work of the Appeals module, run by the worker.</summary>
public interface IAppealsMaintenance
{
    /// <summary>
    /// Writes the daily aggregate of every bill's open or closed record for one Vilnius day: today's rows on every run, a
    /// finished day's once, by the first run after it ended. Returns how many rows were written.
    /// </summary>
    Task<int> AggregateDayAsync(DateOnly day, CancellationToken cancellationToken);

    /// <summary>Closes open records on a final vote or explicit rejection, freezes the counts, and returns how many closed.</summary>
    Task<int> CloseFinishedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Who wrote an item the moderators see (design 8.3), for the administrators' full lock of that person's account. The answer is the
/// account's id for the host to hand to Identity; it goes to no browser. Only the kinds the writing restriction is reached from:
/// <c>reason</c> (an argument), <c>comment</c> and <c>statement</c> (an organisation's, by the person who wrote it). An MP's
/// response is not among them: the restriction does not reach it, so neither does the lock.
/// </summary>
public interface IAuthorLookup
{
    /// <summary><c>reason.not-found</c>, <c>comment.not-found</c> or <c>statement.not-found</c> when the item or its author's account is gone.</summary>
    Task<Result<Guid>> AuthorOfAsync(string kind, Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The search of bills (design 8.12.5): one for every list of bills (the bill list, the votes, the statistics). It reads this
/// module's copy of the bill catalogue (<c>appeals.bill_card</c>): the number, the official title and the readable title.
/// </summary>
public interface IBillCards
{
    /// <summary>
    /// Makes the copy match the Parliament module's catalogue: a card for every bill, the official title current. Called after
    /// every import and when the worker starts; the readable titles are never touched here.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The numbers of the bills that match: every word of the query found in the number, the official title or the readable
    /// title, by stem or as part of a word with case and Lithuanian letters ignored. Null when the query has no words: no filter.
    /// </summary>
    Task<IReadOnlyCollection<string>?> SearchAsync(string? query, CancellationToken cancellationToken);
}
