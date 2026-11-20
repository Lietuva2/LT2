using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.Statistics;

namespace Lt2.Parliament.Domain;

// Design 5.1, schema "parliament". Seimas identifiers are the keys of what the Seimas numbers (terms, sessions,
// sittings, agenda items, votes); our own rows (people, bodies, bills) have UUID v7 keys and a unique Seimas key.

/// <summary>A Seimas term (<c>kadencijos_id</c>; 10 is 2024–2028).</summary>
internal sealed class Term
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }
}

internal sealed class Session
{
    public long Id { get; set; }

    public int TermId { get; set; }

    public string Name { get; set; } = "";

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }
}

internal sealed class Sitting
{
    public long Id { get; set; }

    public long SessionId { get; set; }

    public string? Number { get; set; }

    public string? Kind { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }
}

/// <summary>One person for life: <c>asmens_id</c> stays the same across terms.</summary>
internal sealed class Person
{
    public Guid Id { get; set; }

    public long SeimasPersonId { get; set; }

    public string FirstName { get; set; } = "";

    public string LastName { get; set; } = "";

    /// <summary>Public address part (<c>/p/jonas-petraitis</c>); set once, never changed.</summary>
    public string Slug { get; set; } = "";

    public string? Gender { get; set; }
}

/// <summary>A person's mandate in one term; <see cref="To"/> set means the mandate ended ("former MP").</summary>
internal sealed class Mandate
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public int TermId { get; set; }

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }

    public string? NominatingParty { get; set; }

    public string? ElectedHow { get; set; }

    public string? Constituency { get; set; }

    public int? TermsServed { get; set; }
}

/// <summary>A faction, committee, commission or group of one term.</summary>
internal sealed class Body
{
    public Guid Id { get; set; }

    public long SeimasId { get; set; }

    public int TermId { get; set; }

    public BodyKind Kind { get; set; }

    public string Name { get; set; } = "";

    public string Slug { get; set; } = "";
}

/// <summary>Membership with dates, so "faction at the time of the vote" can be answered.</summary>
internal sealed class Membership
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public Guid BodyId { get; set; }

    public string Role { get; set; } = "";

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }
}

/// <summary>Official contact from open data: public data about a public office.</summary>
internal sealed class Contact
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }

    public string Kind { get; set; } = "";

    public string Value { get; set; } = "";
}

/// <summary>A bill, created from agenda items (<c>XVP-0412</c>). Enrichment from e-seimas comes later.</summary>
internal sealed class Bill
{
    public Guid Id { get; set; }

    public string Number { get; set; } = "";

    public string Title { get; set; } = "";

    public string? ESeimasUrl { get; set; }
}

internal sealed class AgendaItem
{
    public long Id { get; set; }

    public long SittingId { get; set; }

    public Guid? BillId { get; set; }

    public string? Number { get; set; }

    public string Title { get; set; } = "";

    /// <summary>The stage as the agenda states it ("Pateikimas").</summary>
    public string? Stage { get; set; }
}

/// <summary>A vote of an agenda item. Consensus votes have no ballots.</summary>
internal sealed class Vote
{
    public long Id { get; set; }

    public long AgendaItemId { get; set; }

    public Guid? BillId { get; set; }

    public DateTimeOffset HeldAt { get; set; }

    public string Wording { get; set; } = "";

    public string? Type { get; set; }

    /// <summary>
    /// The effective kind every reader uses (design 7.2): a moderator's correction when there is one, otherwise what
    /// <see cref="VoteRules.Classify"/> found. Written only through <see cref="VoteClassification.Effective"/>.
    /// </summary>
    public VoteKind Kind { get; set; }

    /// <summary>The effective stage, with <see cref="Kind"/>.</summary>
    public BillStage? Stage { get; set; }

    /// <summary>A moderator's correction of the kind (design 7.2); null while the rules' classification stands. Never
    /// touched by the import.</summary>
    public VoteKind? KindOverride { get; set; }

    /// <summary>The stage of the correction: chosen for a stage result, the agenda item's for an amendment, none otherwise.</summary>
    public BillStage? StageOverride { get; set; }

    public DateTimeOffset? OverriddenAt { get; set; }

    /// <summary>The moderator's account (design 6.9: a staff action keeps the id); never shown.</summary>
    public Guid? OverriddenBy { get; set; }

    /// <summary>The moderator's statement of the correction.</summary>
    public string? OverrideStatement { get; set; }

    public bool IsConsensus { get; set; }

    /// <summary>The per-MP results have been read (always false for a consensus vote).</summary>
    public bool HasResult { get; set; }

    public int? For { get; set; }

    public int? Against { get; set; }

    public int? Abstain { get; set; }

    public int? Voted { get; set; }

    public int? Seats { get; set; }

    /// <summary>The official comment, for example that per-MP votes do not match the protocol totals.</summary>
    public string? OfficialNote { get; set; }
}

/// <summary>One MP's vote. <see cref="FactionLabel"/> is the faction as the result states it at the time of the vote.</summary>
internal sealed class Ballot
{
    public long VoteId { get; set; }

    public Guid PersonId { get; set; }

    public string? FactionLabel { get; set; }

    /// <summary>The faction body the person belonged to on the day of the vote, from the memberships.</summary>
    public Guid? FactionId { get; set; }

    public BallotChoice Choice { get; set; }
}

/// <summary>Derived: a faction's position in one vote (design 5.1), by the faction label of the per-MP results.</summary>
internal sealed class FactionPositionRow
{
    public long VoteId { get; set; }

    public string FactionLabel { get; set; } = "";

    /// <summary>The faction body most of the label's MPs belonged to on the day; null if none could be resolved.</summary>
    public Guid? FactionId { get; set; }

    public FactionPosition Position { get; set; }

    public int For { get; set; }

    public int Against { get; set; }

    public int Abstain { get; set; }

    public int DidNotVote { get; set; }
}

/// <summary>Derived: the counts behind an MP's two statistics in one term (design 5.1), recomputed after each import that changed data.</summary>
internal sealed class MpStat
{
    public Guid PersonId { get; set; }

    public int TermId { get; set; }

    public int VotesHeld { get; set; }

    public int VotesTakenPart { get; set; }

    public int VotesWithFactionPosition { get; set; }

    public int VotedWithFaction { get; set; }

    public int FreeVotes { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}

internal sealed class ImportRun
{
    public Guid Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary><c>running</c>, <c>succeeded</c>, <c>failed</c>.</summary>
    public string Status { get; set; } = "running";

    /// <summary>The base address imported from.</summary>
    public string Source { get; set; } = "";

    /// <summary>At least one response of this run came from the stand-in.</summary>
    public bool Synthetic { get; set; }

    public int Requests { get; set; }

    /// <summary>Data rows added, changed or removed.</summary>
    public int Changes { get; set; }

    public string? Error { get; set; }
}

/// <summary>Every distinct response, compressed, so parsing can be re-run and any number traced to its source.</summary>
internal sealed class RawDocument
{
    public Guid Id { get; set; }

    /// <summary>Path and query relative to the base address, for example <c>p2b.ad_seimo_nariai?kadencijos_id=10</c>.</summary>
    public string Request { get; set; } = "";

    public string Service { get; set; } = "";

    public string Url { get; set; } = "";

    public string Sha256 { get; set; } = "";

    /// <summary>The body as received, gzip-compressed.</summary>
    public byte[] Body { get; set; } = [];

    public bool Synthetic { get; set; }

    public Guid ImportRunId { get; set; }

    public DateTimeOffset FirstFetchedAt { get; set; }

    public DateTimeOffset LastFetchedAt { get; set; }
}

/// <summary>Where each request stands: what makes the import resumable and lets it skip what has not changed.</summary>
internal sealed class FetchState
{
    public string Request { get; set; } = "";

    public Guid RawDocumentId { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>When the stored response was parsed and applied; null if that has not happened yet.</summary>
    public DateTimeOffset? ParsedAt { get; set; }

    /// <summary>The parser version that applied it; a newer parser applies stored responses again.</summary>
    public int ParserVersion { get; set; }
}
