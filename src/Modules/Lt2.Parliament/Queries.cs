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

public sealed record VoteDetail(VoteSummary Summary, DateTimeOffset SittingStartsAt, string AgendaItemTitle, IReadOnlyList<FactionSplit> Factions, IReadOnlyList<BallotInfo> Ballots);

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

    Task<BillPage> BillsAsync(string? query, string? cursor, int limit, CancellationToken cancellationToken);

    Task<BillDetail?> BillAsync(string number, CancellationToken cancellationToken);

    Task<VoteDetail?> VoteAsync(long id, CancellationToken cancellationToken);

    Task<ImportsInfo> ImportsAsync(CancellationToken cancellationToken);
}
