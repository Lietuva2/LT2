using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Parliament;

namespace Lt2.Api.Contracts.Agenda;

// Design 8.11.1, 8.12.5: the agenda page's one public, cacheable answer. It holds nothing about the caller: planned items
// and recent stage results with each bill's public numbers, both sides for every bill.

/// <summary>
/// A bill's public numbers (design 8.12.5): both sides with the counts for accounts older than 30 days and the threshold.
/// <c>Id</c> is the bill's record, null while nobody has taken part; <c>Status</c> is <c>open</c>, <c>closed</c> or
/// <c>rejected</c> (taking part closed by the moderators, no numbers shown).
/// </summary>
public sealed record AgendaAppealResponse(Guid? Id, string Status, SideCountsResponse Counts, ThresholdResponse Threshold);

/// <summary>
/// One planned item. <c>Title</c> is null when the bill is not (yet) in the open data, and then the bill has no page here
/// and no <c>Appeal</c>.
/// </summary>
public sealed record AgendaItemResponse(
    string Number, string? Title, string? Stage, bool SetByModerator, string? SourceUrl, AgendaAppealResponse? Appeal);

/// <summary>The items planned for one sitting day ("Planuojama …").</summary>
public sealed record AgendaDayResponse(DateOnly Date, IReadOnlyList<AgendaItemResponse> Items);

/// <summary>A stage result of the last seven days, with the bill's public numbers.</summary>
public sealed record AgendaVoteResponse(
    long VoteId, string Number, string Title, string? Stage, DateTimeOffset HeldAt, bool IsConsensus, VoteTotalsResponse Totals, string? OfficialNote,
    AgendaAppealResponse? Appeal);

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
