namespace Lt2.Api.Contracts.Parliament;

// Design 7.4: the public parliament API. Codes are lower-case English words: kind faction | committee | commission |
// group | other; vote kind stage-result | amendment | procedural | other; stage presentation | consideration | adoption;
// choice for | against | abstain | did-not-vote; position for | against | abstain | free | nobody-voted.

public sealed record TermResponse(int Id, string Name, DateOnly From, DateOnly? To);

public sealed record BodyLink(string Slug, string Name, string Kind);

public sealed record MpLink(string Slug, string FirstName, string LastName);

/// <param name="Sitting">False for a former MP (the mandate ended).</param>
public sealed record MpListItem(string Slug, string FirstName, string LastName, string? Gender, bool Sitting, BodyLink? Faction, string? NominatingParty);

public sealed record MpListResponse(IReadOnlyList<MpListItem> Items, int Total, string? NextCursor);

public sealed record MandateResponse(DateOnly From, DateOnly? To, string? NominatingParty, string? ElectedHow, string? Constituency, int? TermsServed);

public sealed record MembershipResponse(BodyLink Body, string Role, DateOnly From, DateOnly? To, bool Current);

public sealed record ContactResponse(string Kind, string Value);

/// <summary>The two statistics with their counts (design 7.3). A share is null when there is nothing to count.</summary>
public sealed record MpStatisticsResponse(
    int VotesHeld, int VotesTakenPart, decimal? Participation, int VotesWithFactionPosition, int VotedWithFaction, decimal? WithFaction, int FreeVotes);

/// <summary>A bill in a list; <c>PlainTitle</c> is its readable title (design 8.13.1) when its record is open or closed and has one.</summary>
public sealed record BillLink(string Number, string Title, string? PlainTitle = null);

public sealed record VoteLink(long Id, DateTimeOffset HeldAt, string Wording, string Kind, string? Stage, bool IsConsensus, bool HasResult, BillLink? Bill);

public sealed record FactionSplitResponse(string Label, BodyLink? Faction, string Position, int For, int Against, int Abstain, int DidNotVote);

/// <param name="Choice">Null for a vote approved by consensus: no per-MP votes exist.</param>
public sealed record MpVoteResponse(VoteLink Vote, string? Choice, FactionSplitResponse? Faction);

public sealed record MpVotesResponse(IReadOnlyList<MpVoteResponse> Items, string? NextCursor);

/// <param name="ManagedSince">
/// Design 8.5.2: since when the profile is taken over ("Profilį tvarko Seimo narys arba jo biuras"), the one public trace of
/// a link; null for a profile nobody took over, which says nothing about joining. Never which account.
/// </param>
/// <param name="Followers">How many members follow the MP (design 8.11.2): a number only, never who.</param>
public sealed record MpProfileResponse(
    MpListItem Mp,
    TermResponse Term,
    MandateResponse Mandate,
    IReadOnlyList<MembershipResponse> Memberships,
    IReadOnlyList<ContactResponse> Contacts,
    MpStatisticsResponse? Statistics,
    IReadOnlyList<MpVoteResponse> RecentVotes,
    DateTimeOffset? ManagedSince = null,
    int Followers = 0);

public sealed record BodyListItem(string Slug, string Name, string Kind, int Members);

public sealed record BodyMemberResponse(MpLink Mp, string Role, DateOnly From, DateOnly? To, bool Current);

public sealed record FactionVoteResponse(VoteLink Vote, FactionSplitResponse Split);

public sealed record BodyResponse(BodyListItem Body, TermResponse Term, IReadOnlyList<BodyMemberResponse> Members, IReadOnlyList<FactionVoteResponse> RecentVotes);

public sealed record BillListItem(string Number, string Title, DateTimeOffset? LastVoteAt);

public sealed record BillListResponse(IReadOnlyList<BillListItem> Items, int Total, string? NextCursor);

public sealed record VoteTotalsResponse(int? For, int? Against, int? Abstain, int? Voted, int? Seats);

/// <param name="OfficialNote">The Seimas's own comment, for example that per-MP votes do not match the protocol totals.</param>
public sealed record VoteSummaryResponse(VoteLink Vote, VoteTotalsResponse Totals, string? OfficialNote);

/// <param name="Planned">The bill's current planned date (design 8.11.1: "Planuojama …"), from today on; null when none.</param>
/// <param name="PlannedAgendaUrl">
/// Where a date read from the agenda is published: the agenda page it was last read from (design 8.4, owner 2026-11-16),
/// not <see cref="Appeals.PlannedDateResponse.SourceUrl"/>, which is the row's e-seimas document when it has one
/// (assumption A5). Null for a moderator's date, without a planned date, or when the agenda was never read.
/// </param>
public sealed record BillResponse(
    string Number, string Title, string? ESeimasUrl, IReadOnlyList<VoteSummaryResponse> Votes, Appeals.PlannedDateResponse? Planned = null,
    string? PlannedAgendaUrl = null);

/// <summary>One vote of the history (design 8.18); <paramref name="AgendaItemTitle"/> names a vote without a bill.</summary>
public sealed record VoteListItem(VoteSummaryResponse Summary, string AgendaItemTitle);

/// <param name="Total">How many votes the search, the kind and the day find in all.</param>
public sealed record VoteListResponse(IReadOnlyList<VoteListItem> Items, int Total, string? NextCursor);

public sealed record BallotResponse(MpLink Mp, string? FactionLabel, string Choice);

/// <param name="ClassificationCorrected">
/// A moderator corrected the vote's kind or stage (design 7.2); the page says "Klasifikaciją pataisė moderatoriai", never who.
/// </param>
public sealed record VoteResponse(
    VoteSummaryResponse Summary, DateTimeOffset SittingStartsAt, string AgendaItemTitle, IReadOnlyList<FactionSplitResponse> Factions, IReadOnlyList<BallotResponse> Ballots,
    bool ClassificationCorrected = false);
