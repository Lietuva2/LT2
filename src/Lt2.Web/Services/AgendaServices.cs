using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Organisations;
using Lt2.Appeals;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Microsoft.Extensions.Caching.Memory;

namespace Lt2.Web.Services;

/// <summary>
/// The agenda (design 8.11.1), composed by the host from the Parliament module (planned items, recent stage results, when
/// the agenda was read) and the Appeals module (each bill's public numbers, both sides for every bill, design 8.12.5). It reads
/// nothing about the caller, so the answer is the same for everyone and cacheable.
/// </summary>
internal sealed class AgendaReads(IPlannedAgenda agenda, IBillCatalogue bills, IParliamentQueries parliament, IAppealsModule appeals, IOrganisations organisations, TimeProvider clock)
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
        var plannedNumbers = planned.Select(p => p.BillNumber).Distinct().ToList();
        var shown = await organisations.ShownOnBillsAsync(plannedNumbers, ct);
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
                            p.BillNumber, facts.GetValueOrDefault(p.BillNumber)?.Title, p.Stage, p.SetByModerator, p.SourceUrl, Record(state),
                            Organisations(state, shown.GetValueOrDefault(p.BillNumber)));
                    })
                    .ToList()))
            .ToList();
        var votes = recent
            .Select(v => new AgendaVoteResponse(
                v.Vote.Id, v.Vote.Bill!.Number, v.Vote.Bill.Title, v.Vote.Stage, v.Vote.HeldAt, v.Vote.IsConsensus, ParliamentReads.Totals(v), v.OfficialNote,
                Record(states.GetValueOrDefault(v.Vote.Bill.Number))))
            .ToList();
        return new AgendaResponse(days, votes, read.ReadAt, read.SourceUrl);
    }

    /// <summary>
    /// The organisations with a public statement on a bill, ordered as on its page (design 8.7); none while its record is
    /// hidden (its page then shows none) or when the bill is not in the catalogue.
    /// </summary>
    private static List<AgendaOrganisationResponse> Organisations(BillRecordState? state, IReadOnlyList<BillOrganisation>? shown) =>
        state is null || state.Status == "rejected" || shown is null
            ? []
            : shown.Select(o => new AgendaOrganisationResponse(o.Slug, o.Name, new OrganisationLabelResponse(o.Label.ConfirmedBy, o.Label.Domain), o.Stance)).ToList();

    /// <summary>A bill the catalogue knows has numbers (zeros while nobody took part); one it does not know has none.</summary>
    private static AgendaRecordResponse? Record(BillRecordState? state) =>
        state is null ? null : new AgendaRecordResponse(state.RecordId, state.Status, AppealMapping.Map(state.Counts), AppealMapping.Map(state.Threshold));
}

/// <summary>
/// The front page's agenda (design 8.7, "Cached"): the same public answer for everyone, kept in memory for
/// <see cref="Lifetime"/>, so the most visited page costs the database one read a minute. It is built only by
/// <see cref="AgendaReads"/>, which takes no caller, and holds only <see cref="AgendaResponse"/>, which has no field about
/// anyone; nothing of a member's own can enter it. The agenda page, the bill's page and the API stay live.
/// </summary>
internal sealed class FrontPageAgenda(AgendaReads reads, IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>The cache entry's key; a test removes it to read afresh without waiting.</summary>
    public const string CacheKey = "front-page:agenda";

    public async Task<AgendaResponse> ReadAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            return reads.ReadAsync(ct);
        }))!;
}

/// <summary>Server-side <see cref="IAgendaApi"/> for the server-rendered agenda and front pages.</summary>
internal sealed class ServerAgendaApi(AgendaReads reads) : IAgendaApi
{
    public async Task<ApiResult<AgendaResponse>> GetAgendaAsync(CancellationToken cancellationToken = default) =>
        ApiResult<AgendaResponse>.Ok(await reads.ReadAsync(cancellationToken));
}
