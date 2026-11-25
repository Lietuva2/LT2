namespace Lt2.Parliament.Import.Parsing;

// What the parsers return: the Seimas data as read, with Seimas identifiers, before any of our own keys exist.

internal sealed record ParsedTerm(int Id, string Name, DateOnly From, DateOnly? To);

internal sealed record ParsedMember(
    long PersonId,
    string FirstName,
    string LastName,
    string? Gender,
    DateOnly From,
    DateOnly? To,
    string? NominatingParty,
    string? ElectedHow,
    string? Constituency,
    int? TermsServed,
    IReadOnlyList<ParsedMembership> Memberships,
    IReadOnlyList<ParsedContact> Contacts);

internal sealed record ParsedMembership(long BodyId, string BodyName, BodyKind Kind, string Role, DateOnly From, DateOnly? To);

internal sealed record ParsedContact(string Kind, string Value);

internal sealed record ParsedSession(long Id, string Name, DateOnly From, DateOnly? To);

internal sealed record ParsedSitting(long Id, string? Number, string? Kind, DateTimeOffset StartsAt, DateTimeOffset? EndsAt);

internal sealed record ParsedAgendaItem(long Id, string? Number, string Title, string? Stage, string? BillNumber, string? BillUrl);

internal sealed record ParsedProceedings(IReadOnlyList<ParsedAgendaItem> Items, IReadOnlyList<ParsedVoteReference> Votes);

/// <summary>A vote as listed in an item's proceedings.</summary>
internal sealed record ParsedVoteReference(long VoteId, long AgendaItemId, DateTimeOffset HeldAt, string Wording, bool IsConsensus, string? Stage);

internal sealed record ParsedVoteResult(long VoteId, VoteTotals Totals, string? OfficialNote, IReadOnlyList<ParsedBallot> Ballots);

/// <summary>Official totals; each may be missing.</summary>
internal sealed record VoteTotals(int? For, int? Against, int? Abstain, int? Voted, int? Seats);

/// <summary>A Seimas bill among the day's TAIS projects: its number without the revision, its title and e-seimas link as TAIS gives them.</summary>
internal sealed record ParsedProject(string BillNumber, string? Title, string? Url);

/// <param name="PersonId"><c>asmens_id</c> on the per-MP row: assumed, so the import also matches by name when it is missing.</param>
internal sealed record ParsedBallot(long? PersonId, string FirstName, string LastName, string? FactionLabel, BallotChoice Choice);
