using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Parliament;

namespace Lt2.Api.Contracts.Agenda;

// Design 8.11.1, 8.12.5, 8.7: the agenda page's and the front page's one public, cacheable answer. It holds nothing about the caller: planned items
// and recent stage results with each bill's public numbers, both sides for every bill.

/// <summary>
/// A bill's public numbers (design 8.12.5): both sides with the counts for accounts older than 30 days and the threshold.
/// <c>Id</c> is the bill's record, null while nobody has taken part; <c>Status</c> is <c>open</c>, <c>closed</c> or
/// <c>rejected</c> (taking part closed by the moderators, no numbers shown).
/// </summary>
public sealed record AgendaRecordResponse(Guid? Id, string Status, SideCountsResponse Counts, ThresholdResponse Threshold)
{
    /// <summary>
    /// For a bill whose threshold is reached, the summary the row names in place of "riba pasiekta" (design 8.12.2): the one
    /// planned that would hold it, else the last one really sent that held it; null otherwise.
    /// </summary>
    public Digests.RecordSummaryResponse? Summary { get; init; }

    /// <summary>
    /// The number of published arguments, which only the bills' list gives (owner, 2026-11-23: it stands in the row's right column,
    /// after the participants); null in the agenda's and the front page's answer, where it is left out altogether.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Arguments { get; init; }
}

/// <summary>
/// An organisation with a public statement on the bill (design 8.7): its profile's address, name, label and stance
/// (<c>for</c>, <c>against</c>, <c>remarks</c>); never its text and never a count.
/// </summary>
public sealed record AgendaOrganisationResponse(string Slug, string Name, Organisations.OrganisationLabelResponse Label, string Stance);

/// <summary>
/// One planned item. <c>Title</c> is null when the bill is not (yet) in the open data, and then the bill has no page here
/// and no <c>Record</c>. <c>Organisations</c> are those with a public statement on it, ordered as on the bill's page (for,
/// against, remarks; within each, in the order published); none while its record is hidden.
/// </summary>
public sealed record AgendaItemResponse(
    string Number, string? Title, string? Stage, bool SetByModerator, string? SourceUrl, AgendaRecordResponse? Record,
    IReadOnlyList<AgendaOrganisationResponse> Organisations);

/// <summary>
/// The items planned for one sitting day ("Planuojama …"), in the agenda's order (design 8.11.1, owner 2026-11-18): by
/// activity (participants plus published arguments), then stage (adoption, consideration, presentation), then number; items
/// without a title last.
/// </summary>
public sealed record AgendaDayResponse(DateOnly Date, IReadOnlyList<AgendaItemResponse> Items);

/// <summary>A stage result of the last seven days, with the bill's public numbers.</summary>
public sealed record AgendaVoteResponse(
    long VoteId, string Number, string Title, string? Stage, DateTimeOffset HeldAt, bool IsConsensus, VoteTotalsResponse Totals, string? OfficialNote,
    AgendaRecordResponse? Record);

/// <param name="RecentVotes">By Vilnius day, newest day first; within a day in the agenda's order (as the planned items).</param>
/// <param name="AgendaReadAt">When the lrs.lt agenda was last read; null when never.</param>
/// <param name="AgendaSourceUrl">Where it was read from.</param>
public sealed record AgendaResponse(
    IReadOnlyList<AgendaDayResponse> Days, IReadOnlyList<AgendaVoteResponse> RecentVotes, DateTimeOffset? AgendaReadAt, string? AgendaSourceUrl);

public static class AgendaRoutes
{
    public const string Agenda = ApiRoutes.Prefix + "/agenda";
}

/// <summary>Design 8.11.1: <c>GET /api/v1/agenda</c>.</summary>
public interface IAgendaApi
{
    Task<ApiResult<AgendaResponse>> GetAgendaAsync(CancellationToken cancellationToken = default);
}
