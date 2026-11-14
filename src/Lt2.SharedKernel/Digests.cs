namespace Lt2.SharedKernel;

/// <summary>An argument as it goes into a summary: its side, words and "Man svarbu" count. No author: a summary holds no person.</summary>
public sealed record DigestReason(string Side, string Text, int Endorsements);

/// <summary>
/// An appeal as the summaries see it (design 8.4): both sides with the counts for accounts older than 30 days, the
/// threshold, the surge note, the planned date, and the most endorsed arguments a moderator has seen.
/// </summary>
public sealed record DigestCandidate(
    Guid AppealId,
    string BillNumber,
    string BillTitle,
    int For,
    int ForOlderThan30Days,
    int Against,
    int AgainstOlderThan30Days,
    int Threshold,
    bool ReachedThreshold,
    bool Held,
    int? SurgeSharePercent,
    int? SurgeWindowHours,
    PlannedDate? Planned,
    IReadOnlyList<DigestReason> Reasons);

/// <summary>
/// How many on each side said they support a party (design 8.4, 5.2 party_support). Only ever published for groups of at
/// least 20 (design 6.3); smaller groups never leave the module as a number.
/// </summary>
public sealed record PartyNumbers(string Party, int? For, int? Against);

/// <summary>What the summaries read from the Appeals module. Implemented by Appeals.</summary>
public interface IDigestSource
{
    /// <summary>Every open appeal with its numbers now; the summary rules decide which go in.</summary>
    Task<IReadOnlyList<DigestCandidate>> CandidatesAsync(CancellationToken cancellationToken);

    /// <summary>Per appeal and party, the supporters on each side, with every group under 20 already withheld.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<PartyNumbers>>> PartyNumbersAsync(IReadOnlyCollection<Guid> appealIds, CancellationToken cancellationToken);
}

/// <summary>A sitting MP a summary would go to. No address: M2a never sends, so none leaves the Parliament module.</summary>
/// <param name="HasOfficialAddress">Open data lists an official address (design 8.4: every sitting MP with one).</param>
public sealed record MpRecipient(string Slug, string FirstName, string LastName, string? Party, bool HasOfficialAddress);

/// <summary>The MPs and the sitting days. Implemented by the Parliament module.</summary>
public interface IMpDirectory
{
    Task<IReadOnlyList<MpRecipient>> SittingMpsAsync(CancellationToken cancellationToken);

    /// <summary>The parties MPs of the current term were nominated by: the choices for party support.</summary>
    Task<IReadOnlyList<string>> PartiesAsync(CancellationToken cancellationToken);

    /// <summary>Days with a plenary sitting in [from, to]: planned items and sittings in the open data.</summary>
    Task<IReadOnlyList<DateOnly>> SittingDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>An MP of any term by the profile's slug; null when unknown (design 8.11.2: following an MP).</summary>
    Task<MpFacts?> FindMpAsync(string slug, CancellationToken cancellationToken);

    /// <summary>MPs by Seimas person id; unknown ids are missing.</summary>
    Task<IReadOnlyDictionary<long, MpFacts>> MpsAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken);
}

/// <summary>
/// Who said they support which party (design 5.2 party_support): private and optional, used only for group numbers of
/// at least 20. Implemented by Identity; the ids never leave the server.
/// </summary>
public interface IPartySupporters
{
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<Guid>>> SupportersByPartyAsync(CancellationToken cancellationToken);
}