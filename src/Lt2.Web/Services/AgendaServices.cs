using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Appeals;
using Lt2.Parliament;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>
/// The agenda (design 8.11.1), composed by the host from the Parliament module (planned items, recent stage results, when
/// the agenda was read) and the Appeals module (each bill's public numbers, both sides for every bill, design 8.12.5). It reads
/// nothing about the caller, so the answer is the same for everyone and cacheable.
/// </summary>
internal sealed class AgendaReads(IPlannedAgenda agenda, IBillCatalogue bills, IParliamentQueries parliament, IAppealsModule appeals, TimeProvider clock)
{
    /// <summary>"Recently voted": the stage results of the last seven days.</summary>
    public const int RecentDays = 7;

    public async Task<AgendaResponse> ReadAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = VilniusTime.DateOf(now);
        var planned = await agenda.BetweenAsync(today, DateOnly.MaxValue, ct);
        var recent = await parliament.StageResultsSinceAsync(now.AddDays(-RecentDays), ct);
        var numbers = planned.Select(p => p.BillNumber).Concat(recent.Select(v => v.Vote.Bill!.Number)).Distinct().ToList();
        var facts = numbers.Count == 0 ? new Dictionary<string, BillFacts>() : await bills.FindManyAsync(numbers, ct);
        var states = await appeals.BillStatesAsync(numbers, ct);
        var read = await parliament.AgendaReadAsync(ct);

        var days = planned
            .GroupBy(p => p.Date)
            .OrderBy(g => g.Key)
            .Select(g => new AgendaDayResponse(
                g.Key,
                g.OrderBy(p => p.BillNumber, StringComparer.Ordinal)
                    .Select(p =>
                    {
                        var state = states.GetValueOrDefault(p.BillNumber);
                        return new AgendaItemResponse(
                            p.BillNumber, facts.GetValueOrDefault(p.BillNumber)?.Title, p.Stage, p.SetByModerator, p.SourceUrl, Appeal(state));
                    })
                    .ToList()))
            .ToList();
        var votes = recent
            .Select(v => new AgendaVoteResponse(
                v.Vote.Id, v.Vote.Bill!.Number, v.Vote.Bill.Title, v.Vote.Stage, v.Vote.HeldAt, v.Vote.IsConsensus, ParliamentReads.Totals(v), v.OfficialNote,
                Appeal(states.GetValueOrDefault(v.Vote.Bill.Number))))
            .ToList();
        return new AgendaResponse(days, votes, read.ReadAt, read.SourceUrl);
    }

    /// <summary>A bill the catalogue knows has numbers (zeros while nobody took part); one it does not know has none.</summary>
    private static AgendaAppealResponse? Appeal(BillAppealState? state) =>
        state is null ? null : new AgendaAppealResponse(state.AppealId, state.Status, AppealMapping.Map(state.Counts), AppealMapping.Map(state.Threshold));
}

/// <summary>Server-side <see cref="IAgendaApi"/> for the server-rendered agenda and front pages.</summary>
internal sealed class ServerAgendaApi(AgendaReads reads) : IAgendaApi
{
    public async Task<ApiResult<AgendaResponse>> GetAgendaAsync(CancellationToken cancellationToken = default) =>
        ApiResult<AgendaResponse>.Ok(await reads.ReadAsync(cancellationToken));
}
