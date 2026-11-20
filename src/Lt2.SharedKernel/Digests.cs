namespace Lt2.SharedKernel;

/// <summary>An argument as it goes into a summary: its side, words and "Man svarbu" count. No author: a summary holds no person.</summary>
public sealed record DigestReason(string Side, string Text, int Endorsements);

/// <summary>
/// The rule for "(iš N)" beside a side's arguments in a summary (design 8.4, owner 2026-11-20): the number of published
/// arguments on that side is shown only when the arguments shown are not all of them, so the reader can tell the top of many
/// from all there are. Null (no number) for a side whose shown arguments are all of its arguments, and for a snapshot made
/// before the number was kept. Marks are never totalled: the same people would be counted more than once.
/// </summary>
public static class DigestReasonCount
{
    public static int? Shown(int? published, int shown) => published is { } n && n > shown ? n : null;
}

/// <summary>
/// An organisation named in a summary's line under a bill (design 8.14.4): its stance, its name and profile address, and the
/// statement the name links to on the bill's page. Never the statement's text, never a representative.
/// </summary>
/// <param name="Stance">One of <see cref="StatementStances"/>.</param>
public sealed record DigestOrganisation(string Stance, Guid OrganisationId, string Slug, string Name, Guid StatementId);

/// <summary>
/// An organisation's statement on a bill as the summaries read it from the Appeals module (design 8.14.4): what the line's
/// rule (<see cref="DigestOrganisationRules"/>) decides by. No text, no person.
/// </summary>
/// <param name="StatementStatus">One of <see cref="StatementStatuses"/>.</param>
/// <param name="OrganisationStatus">One of <see cref="OrganisationStatuses"/>.</param>
/// <param name="Confirmed">"Patvirtinta": by its domain or by a moderator.</param>
/// <param name="KeptOutOfSummaries">A moderator keeps the organisation out of summaries (design 8.14.9).</param>
/// <param name="FirstPublicAt">When the statement first became public; null while it never was.</param>
/// <param name="PublishedAt">When it was published: the order on the bill's page.</param>
public sealed record DigestStatementFacts(
    Guid StatementId,
    Guid OrganisationId,
    string Slug,
    string Name,
    string BillNumber,
    string Stance,
    string StatementStatus,
    string OrganisationStatus,
    bool Confirmed,
    bool KeptOutOfSummaries,
    DateTimeOffset? FirstPublicAt,
    DateTimeOffset PublishedAt);

/// <summary>
/// The rule for the organisations' line under a bill in a summary (design 8.14.4, owner 2026-11-20). A statement is named
/// when it is published now and was public when the summary was prepared (the moment its arguments were fixed), and its
/// organisation is active (not blocked or closed), confirmed ("Patvirtinta") and not kept out of summaries by a moderator.
/// The names are grouped by stance (asks to vote for, against, remarks) and, within a stance, in the order the bill's page
/// shows them: the order published (design 8.14.3, 8.14.8: the same order rule for all).
/// </summary>
public static class DigestOrganisationRules
{
    public static bool Named(DigestStatementFacts statement, DateTimeOffset preparedAt)
    {
        ArgumentNullException.ThrowIfNull(statement);
        return statement.StatementStatus == StatementStatuses.Published
            && statement.FirstPublicAt is { } first && first <= preparedAt
            && statement.OrganisationStatus == OrganisationStatuses.Active
            && statement.Confirmed
            && !statement.KeptOutOfSummaries
            && StatementStances.All.Contains(statement.Stance);
    }

    /// <summary>The line of one bill: the organisations named, for, against, then remarks, each stance in the order published.</summary>
    public static IReadOnlyList<DigestOrganisation> Line(IEnumerable<DigestStatementFacts> statements, DateTimeOffset preparedAt) =>
        statements.Where(s => Named(s, preparedAt))
            .OrderBy(s => IndexOf(s.Stance))
            .ThenBy(s => s.PublishedAt)
            .ThenBy(s => s.StatementId)
            .Select(s => new DigestOrganisation(s.Stance, s.OrganisationId, s.Slug, s.Name, s.StatementId))
            .ToList();

    /// <summary>The line of each bill (compared without case), from statements on several bills; a bill with none named is missing.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<DigestOrganisation>> ByBill(IEnumerable<DigestStatementFacts> statements, DateTimeOffset preparedAt) =>
        statements.GroupBy(s => s.BillNumber, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, Line: Line(g, preparedAt)))
            .Where(x => x.Line.Count > 0)
            .ToDictionary(x => x.Key, x => x.Line, StringComparer.OrdinalIgnoreCase);

    private static int IndexOf(string stance)
    {
        for (var i = 0; i < StatementStances.All.Count; i++)
        {
            if (StatementStances.All[i] == stance)
            {
                return i;
            }
        }

        return StatementStances.All.Count;
    }
}

/// <summary>
/// A record as the summaries see it now (design 8.4): both sides with the counts for accounts older than 30 days, the
/// threshold, the surge note and the planned date. Its arguments come from <see cref="IDigestSource.ReasonPoolAsync"/>.
/// </summary>
public sealed record DigestCandidate(
    Guid RecordId,
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
    PlannedDate? Planned);

/// <summary>
/// An argument a summary may hold (design 8.4, 8.19.2, owner 2026-11-17): published and not hidden, with its "Man svarbu" count
/// now and when it first became public. No author.
/// </summary>
public sealed record DigestPoolReason(Guid Id, Guid RecordId, string Side, string Text, int Endorsements, DateTimeOffset PublishedAt);

/// <summary>
/// How many on each side said they support a party (design 8.4, 5.2 party_support). Only ever published for groups of at
/// least 20 (design 6.3); smaller groups never leave the module as a number.
/// </summary>
public sealed record PartyNumbers(string Party, int? For, int? Against);

/// <summary>What the summaries read from the Appeals module. Implemented by Appeals.</summary>
public interface IDigestSource
{
    /// <summary>Every open record with its numbers now; the summary rules decide which go in.</summary>
    Task<IReadOnlyList<DigestCandidate>> CandidatesAsync(CancellationToken cancellationToken);

    /// <summary>One record as <see cref="CandidatesAsync"/> gives it; null when it is not open.</summary>
    Task<DigestCandidate?> CandidateAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>
    /// The arguments a summary may choose from on the given records (design 8.19.2, owner 2026-11-17): published now (so
    /// neither hidden, merged nor waiting) and first public no later than <paramref name="publishedBy"/>, the summary's
    /// preparation. Whether a moderator has seen one does not matter.
    /// </summary>
    Task<IReadOnlyList<DigestPoolReason>> ReasonPoolAsync(IReadOnlyCollection<Guid> recordIds, DateTimeOffset publishedBy, CancellationToken cancellationToken);

    /// <summary>
    /// The organisations' statements published now on the given bills (design 8.14.4), with their organisations' state, for
    /// the line under each bill (<see cref="DigestOrganisationRules"/> decides which are named). Never a text or a person.
    /// </summary>
    Task<IReadOnlyList<DigestStatementFacts>> OrganisationStatementsAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);

    /// <summary>Per record and party, the supporters on each side, with every group under 20 already withheld.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<PartyNumbers>>> PartyNumbersAsync(IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken);

    /// <summary>
    /// The MPs' published responses on the given bills (design 8.5.5, 8.19.3: "Jūsų atsakymas …" in the summary): the stance
    /// and when the text now shown was written. Withdrawn and hidden responses are not given. Never who wrote one.
    /// </summary>
    Task<IReadOnlyList<MpStance>> PublishedResponsesAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);

    /// <summary>
    /// Design 8.12.2, 8.19.1: a summary that holds these records was really sent, so each counts as summarised from now on
    /// (<c>first_summarised_at</c>, set once). Written in the caller's transaction, so it commits with the sending.
    /// </summary>
    Task MarkSummarisedAsync(IReadOnlyCollection<Guid> recordIds, DateTimeOffset at, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>An MP's published response on a bill as a summary gives it (design 8.19.3): the stance and its date, no text, no writer.</summary>
/// <param name="Stance"><c>for</c>, <c>against</c> or <c>undecided</c>.</param>
public sealed record MpStance(long SeimasPersonId, string BillNumber, string Stance, DateTimeOffset WrittenAt);

/// <summary>A sitting MP a summary would go to. No address: it leaves the Parliament module only through <see cref="IMpMailAddresses"/>.</summary>
/// <param name="HasOfficialAddress">
/// Open data lists an official address (design 8.4: every sitting MP with one), by the one definition of design 8.5.1.
/// </param>
public sealed record MpRecipient(string Slug, string FirstName, string LastName, string? Party, bool HasOfficialAddress, long SeimasPersonId = 0);

/// <summary>
/// Design 8.19.3: the one call that gives MPs' official addresses out of the Parliament module, made for sending MP mail only
/// (summaries, the introduction letter, the preference emails) and for comparing with the suppression list. An address it
/// gives travels only into an outbox row or a keyed hash; it is never logged, stored elsewhere or answered.
/// </summary>
public interface IMpMailAddresses
{
    /// <summary>
    /// The official address (design 8.5.1, the one definition) of each given sitting MP who has one, by Seimas person id; an MP
    /// without one, or whose mandate has ended, is missing.
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> OfficialAddressesForSendingAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken);

    /// <summary>
    /// Design 8.19.1: whether the parliament data holds anything invented: any stored response marked synthetic (the
    /// stand-in's), whatever else is stored beside it. The stand-in MPs' addresses are on the real <c>lrs.lt</c> domain, so
    /// MP mail is locked while this is true unless the deployment declares all its mail caught.
    /// </summary>
    Task<bool> HoldsInventedDataAsync(CancellationToken cancellationToken);
}

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

    /// <summary>
    /// Each given MP's faction as profiles show it (design 8.20.1, the follow email about responses): today's for a sitting MP of
    /// the current term, none in the mixed group; the last one for a former MP. MPs without a faction are missing.
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> FactionsAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken);

    /// <summary>
    /// "Whose official address is this" (design 8.5.1): the MP for whom open data lists <paramref name="email"/> as an email
    /// contact on the Seimas's own domain, compared ignoring case, while the mandate has not ended; an address listed for
    /// more than one such MP belongs to nobody. Null for every other address. No address leaves the module otherwise.
    /// </summary>
    Task<MpFacts?> OfficialAddressOwnerAsync(string email, CancellationToken cancellationToken);
}

/// <summary>
/// Design 8.19.2 (owner, 2026-11-17): where the notice of a prepared summary goes. Implemented by Identity: the own addresses of
/// the accounts holding an activated moderator or administrator role (as <c>GET /me</c>'s <c>staffAccount</c>), used only to
/// write outbox rows; never logged or shown.
/// </summary>
public interface IStaffAddresses
{
    Task<IReadOnlyList<string>> ModeratorAddressesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Who said they support which party (design 5.2 party_support): private and optional, used only for group numbers of
/// at least 20. Implemented by Identity; the ids never leave the server.
/// </summary>
public interface IPartySupporters
{
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<Guid>>> SupportersByPartyAsync(CancellationToken cancellationToken);
}