namespace Lt2.Parliament;

// What the Parliament module answers for the pages and the public API (design 7.3, 7.4). Codes are lower-case English
// words, stable for the API: kind faction | committee | commission | group | other; vote kind stage-result | amendment |
// procedural | other; stage presentation | consideration | adoption; choice for | against | abstain | did-not-vote;
// position for | against | abstain | free | nobody-voted.

public sealed record TermInfo(int Id, string Name, DateOnly From, DateOnly? To);

public sealed record BodyRef(string Slug, string Name, string Kind);

public sealed record MpRef(string Slug, string FirstName, string LastName);

/// <param name="Sitting">The mandate has not ended; false means "former MP".</param>
public sealed record MpSummary(string Slug, string FirstName, string LastName, string? Gender, bool Sitting, BodyRef? Faction, string? NominatingParty);

public sealed record MpFilter(string? Faction = null, string? Committee = null, string? Query = null, string? Cursor = null, int Limit = 200);

public sealed record MpPage(IReadOnlyList<MpSummary> Items, int Total, string? NextCursor);

public sealed record MandateInfo(DateOnly From, DateOnly? To, string? NominatingParty, string? ElectedHow, string? Constituency, int? TermsServed);

public sealed record MembershipInfo(BodyRef Body, string Role, DateOnly From, DateOnly? To, bool Current);

public sealed record ContactInfo(string Kind, string Value);

/// <summary>The counts behind the two statistics; a share is null when there is nothing to count.</summary>
public sealed record MpStatisticsInfo(
    int VotesHeld, int VotesTakenPart, decimal? Participation, int VotesWithFactionPosition, int VotedWithFaction, decimal? WithFaction, int FreeVotes);

public sealed record BillRef(string Number, string Title);

public sealed record VoteRef(long Id, DateTimeOffset HeldAt, string Wording, string Kind, string? Stage, bool IsConsensus, bool HasResult, BillRef? Bill);

/// <summary>How one faction voted in one vote, by the faction label of the per-MP results.</summary>
public sealed record FactionSplit(string Label, BodyRef? Faction, string Position, int For, int Against, int Abstain, int DidNotVote);

/// <param name="Choice">The MP's choice; null for a vote approved by consensus (no per-MP votes exist).</param>
/// <param name="Faction">The split of the MP's faction in that vote; null without per-MP votes or a faction label.</param>
public sealed record MpVote(VoteRef Vote, string? Choice, FactionSplit? Faction);

public sealed record MpVotePage(IReadOnlyList<MpVote> Items, string? NextCursor);

public sealed record MpProfile(
    MpSummary Mp,
    TermInfo Term,
    MandateInfo Mandate,
    IReadOnlyList<MembershipInfo> Memberships,
    IReadOnlyList<ContactInfo> Contacts,
    MpStatisticsInfo? Statistics,
    IReadOnlyList<MpVote> RecentVotes);

public sealed record BodySummary(string Slug, string Name, string Kind, int Members);

public sealed record BodyMember(MpRef Mp, string Role, DateOnly From, DateOnly? To, bool Current);

public sealed record FactionVote(VoteRef Vote, FactionSplit Split);

/// <param name="RecentVotes">For a faction, its split in the latest recorded votes; empty for other bodies.</param>
public sealed record BodyDetail(BodySummary Body, TermInfo Term, IReadOnlyList<BodyMember> Members, IReadOnlyList<FactionVote> RecentVotes);

public sealed record BillSummary(string Number, string Title, DateTimeOffset? LastVoteAt);

public sealed record BillPage(IReadOnlyList<BillSummary> Items, int Total, string? NextCursor);

public sealed record VoteTotalsInfo(int? For, int? Against, int? Abstain, int? Voted, int? Seats);

/// <param name="OfficialNote">The Seimas's own comment, for example that per-MP votes do not match the protocol totals.</param>
public sealed record VoteSummary(VoteRef Vote, VoteTotalsInfo Totals, string? OfficialNote);

public sealed record BillDetail(string Number, string Title, string? ESeimasUrl, IReadOnlyList<VoteSummary> Votes);

public sealed record BallotInfo(MpRef Mp, string? FactionLabel, string Choice);

/// <param name="ClassificationCorrected">A moderator corrected the vote's kind or stage (design 7.2); the page says so, never who.</param>
public sealed record VoteDetail(
    VoteSummary Summary, DateTimeOffset SittingStartsAt, string AgendaItemTitle, IReadOnlyList<FactionSplit> Factions, IReadOnlyList<BallotInfo> Ballots,
    bool ClassificationCorrected = false);

/// <summary>Which votes the history lists (design 8.18).</summary>
public enum VoteHistoryKind
{
    /// <summary>The stage results on bills (the default, what "Balsuota" shows).</summary>
    StageResults,

    /// <summary>The stage results of adoption.</summary>
    Adoptions,

    /// <summary>Every vote, with or without a bill.</summary>
    All,
}

/// <param name="BillNumbers">The bills a search of bills kept (design 8.12.5, found by the Appeals module); a vote without a bill never matches. Null: no search.</param>
/// <param name="Until">The last Vilnius day included.</param>
public sealed record VoteHistoryFilter(IReadOnlyCollection<string>? BillNumbers, VoteHistoryKind Kind, DateOnly? Until, string? Cursor, int Limit);

public sealed record VoteHistoryItem(VoteSummary Summary, string AgendaItemTitle);

public sealed record VoteHistoryPage(IReadOnlyList<VoteHistoryItem> Items, int Total, string? NextCursor);

/// <param name="LastSuccessAt">When the last successful import finished.</param>
/// <param name="ParliamentDataInvented">The data came from the stand-in.</param>
public sealed record ImportsInfo(DateTimeOffset? LastSuccessAt, DateTimeOffset? LastRunAt, string? LastRunStatus, bool ParliamentDataInvented);

/// <summary>Read side of the Parliament module, for the public API and the pages. Null means not found.</summary>
public interface IParliamentQueries
{
    Task<TermInfo?> CurrentTermAsync(CancellationToken cancellationToken);

    /// <summary>MPs of the current term in Lithuanian alphabetical order of last name; never ordered by a statistic.</summary>
    Task<MpPage> MpsAsync(MpFilter filter, CancellationToken cancellationToken);

    Task<MpProfile?> MpAsync(string slug, CancellationToken cancellationToken);

    Task<MpVotePage?> MpVotesAsync(string slug, string? cursor, int limit, CancellationToken cancellationToken);

    /// <param name="kind"><c>faction</c> or <c>committee</c>.</param>
    Task<IReadOnlyList<BodySummary>> BodiesAsync(string kind, CancellationToken cancellationToken);

    Task<BodyDetail?> BodyAsync(string kind, string slug, CancellationToken cancellationToken);

    /// <param name="billNumbers">The bills a search of bills kept (design 8.12.5, found by the Appeals module); null for no search.</param>
    Task<BillPage> BillsAsync(IReadOnlyCollection<string>? billNumbers, string? cursor, int limit, CancellationToken cancellationToken);

    Task<BillDetail?> BillAsync(string number, CancellationToken cancellationToken);

    Task<VoteDetail?> VoteAsync(long id, CancellationToken cancellationToken);

    /// <summary>
    /// The history of votes, newest first (design 8.18), read and counted in the database a part at a time; at most 300
    /// in one part.
    /// </summary>
    Task<VoteHistoryPage> VoteHistoryAsync(VoteHistoryFilter filter, CancellationToken cancellationToken);

    Task<ImportsInfo> ImportsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stage-result votes on bills held on the last <paramref name="days"/> Vilnius days that had one, however long ago,
    /// newest first (design 8.7 "Paskutiniai balsavimai"); at most 200.
    /// </summary>
    Task<IReadOnlyList<VoteSummary>> StageResultsOfLastDaysAsync(int days, CancellationToken cancellationToken);

    /// <summary>
    /// When the upcoming agenda was last read: the later of the watcher's last good read of the lrs.lt page and the last save of
    /// an entered day, with that one's address (design 8.4, 8.11.1); and the watcher's own last good read's address.
    /// </summary>
    Task<AgendaReadInfo> AgendaReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Design 8.26.1: the numbers of the bills on the agenda of a sitting of the current term held from <paramref name="from"/>
    /// to <paramref name="to"/> (Vilnius days, inclusive).
    /// </summary>
    Task<IReadOnlyList<string>> BillsDiscussedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The given votes with their totals, by id; unknown ids are missing.</summary>
    Task<IReadOnlyDictionary<long, VoteSummary>> VotesAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Design 8.26.3: every faction of the current term with a recorded vote held in the period, its unity and the votes
    /// where it split most, in order of its seats today, then by name.
    /// </summary>
    Task<IReadOnlyList<FactionUnityInfo>> FactionUnityAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Design 8.26.4: every MP's ballot and every faction's position at the given votes, with the MPs as the list shows them
    /// (today's faction, or the last one for a former MP). Public data about a public office.
    /// </summary>
    Task<VoteBallotsInfo> BallotsAtAsync(IReadOnlyCollection<long> voteIds, CancellationToken cancellationToken);
}

/// <summary>A faction's unity over a period (design 8.26.3); <paramref name="Share"/> null with no vote with a position.</summary>
/// <param name="Seats">Its members today.</param>
/// <param name="SmallSample">Fewer than 20 votes with a position.</param>
public sealed record FactionUnityInfo(
    BodyRef Faction, int Seats, int VotesWithPosition, int FreeVotes, decimal? Share, bool SmallSample, IReadOnlyList<FactionSplitVote> MostSplit);

/// <summary>A vote where a faction split: its position (<c>free</c> included) and its members' counts.</summary>
public sealed record FactionSplitVote(VoteRef Vote, string AgendaItemTitle, string Position, int For, int Against, int Abstain);

/// <summary>One MP's ballot at a vote: <c>for</c>, <c>against</c>, <c>abstain</c> or <c>did-not-vote</c>.</summary>
public sealed record VoteBallot(long VoteId, string MpSlug, string Choice);

/// <summary>One faction's position at a vote (design 7.3): <c>for</c>, <c>against</c>, <c>abstain</c>, <c>free</c> or <c>nobody-voted</c>.</summary>
public sealed record VoteFactionPosition(long VoteId, BodyRef Faction, string Position);

/// <summary>The ballots and faction positions at some votes, with every MP named in them and every faction's seats today.</summary>
public sealed record VoteBallotsInfo(
    IReadOnlyList<VoteBallot> Ballots,
    IReadOnlyList<VoteFactionPosition> FactionPositions,
    IReadOnlyDictionary<string, MpSummary> Mps,
    IReadOnlyDictionary<string, int> Seats);

/// <summary>
/// The last read of the upcoming agenda (design 8.4): the watcher's last good read or the last entered day's save, whichever
/// is later, and its address; both null when it was never read nor entered. <paramref name="WatcherUrl"/> is the address of the
/// watcher's last good read alone (null when it never read one), the source of a date the watcher read.
/// </summary>
public sealed record AgendaReadInfo(DateTimeOffset? ReadAt, string? SourceUrl, string? WatcherUrl = null);
