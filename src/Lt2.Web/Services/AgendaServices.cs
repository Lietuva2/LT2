using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Organisations;
using Lt2.Appeals;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Lt2.Web.Client.Agenda;
using Microsoft.Extensions.Caching.Memory;

namespace Lt2.Web.Services;

/// <summary>
/// The agenda (design 8.11.1), composed by the host from the Parliament module (planned items, recent stage results, when
/// the agenda was read) and the Appeals module (each bill's public numbers, both sides for every bill, design 8.12.5), with the
/// Notifications module's summary days for the bills whose threshold is reached (<see cref="RowSummaries"/>, design 8.12.2). It
/// reads nothing about the caller, so the answer is the same for everyone and cacheable.
/// </summary>
internal sealed class AgendaReads(
    IPlannedAgenda agenda, IBillCatalogue bills, IParliamentQueries parliament, IAppealsModule appeals, IOrganisations organisations, RowSummaries summaries, TimeProvider clock)
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

        // Design 8.12.2 (owner, 2026-11-21): a reached bill's row names its summary's day, read once for every row.
        var summaryOf = await summaries.OfAsync(states.Values.Select(s => (s.RecordId, s.Status, s.Threshold.Reached)), ct);
        AgendaRecordResponse? Record(BillRecordState? state)
        {
            var numbers = Numbers(state);
            return numbers is { Id: { } id } && summaryOf.TryGetValue(id, out var summary) ? numbers with { Summary = summary } : numbers;
        }

        var plannedNumbers = planned.Select(p => p.BillNumber).Distinct().ToList();
        var shown = await organisations.ShownOnBillsAsync(plannedNumbers, ct);
        var read = await parliament.AgendaReadAsync(ct);

        // Design 8.11.1 (owner, 2026-11-18): within a day, by activity, then stage, then number; bills the open data does not
        // know yet last (AgendaRules). The days earliest first; the votes' days newest first.
        int ActivityOf(string number) => states.GetValueOrDefault(number) is { Status: not BillRecordStatuses.Rejected } s ? AgendaRules.Activity(s.Counts.Total, s.Arguments) : 0;
        var days = planned
            .GroupBy(p => p.Date)
            .OrderBy(g => g.Key)
            .Select(g => new AgendaDayResponse(
                g.Key,
                AgendaRules.WithinDay(
                        g.Select(p =>
                        {
                            var state = states.GetValueOrDefault(p.BillNumber);
                            return new AgendaItemResponse(
                                p.BillNumber, facts.GetValueOrDefault(p.BillNumber)?.Title, p.Stage, p.SetByModerator, p.SourceUrl, Record(state),
                                Organisations(state, shown.GetValueOrDefault(p.BillNumber)));
                        }),
                        i => i.Title is not null, i => ActivityOf(i.Number), i => i.Stage, i => i.Number)
                    .ToList()))
            .ToList();
        var votes = recent
            .Select(v => new AgendaVoteResponse(
                v.Vote.Id, v.Vote.Bill!.Number, v.Vote.Bill.Title, v.Vote.Stage, v.Vote.HeldAt, v.Vote.IsConsensus, ParliamentReads.Totals(v), v.OfficialNote,
                Record(states.GetValueOrDefault(v.Vote.Bill.Number))))
            .GroupBy(v => VilniusTime.DateOf(v.HeldAt))
            .OrderByDescending(g => g.Key)
            .SelectMany(g => AgendaRules.WithinDay(g, _ => true, v => ActivityOf(v.Number), v => v.Stage, v => v.Number))
            .ToList();
        return new AgendaResponse(days, votes, read.ReadAt, read.SourceUrl);
    }

    /// <summary>
    /// The organisations with a public statement on a bill, ordered as on its page (design 8.7); none while its record is
    /// hidden (its page then shows none) or when the bill is not in the catalogue.
    /// </summary>
    private static List<AgendaOrganisationResponse> Organisations(BillRecordState? state, IReadOnlyList<BillOrganisation>? shown) =>
        state is null || state.Status == BillRecordStatuses.Rejected ? [] : OrganisationsOf(shown);

    /// <summary>
    /// The organisations as the rows of the agenda and of <c>/projektai</c> carry them (<see cref="BillListReads"/>): the same
    /// mapping of what <see cref="IOrganisations.ShownOnBillsAsync"/> says, none when it says nothing of the bill.
    /// </summary>
    public static List<AgendaOrganisationResponse> OrganisationsOf(IReadOnlyList<BillOrganisation>? shown) =>
        shown is null
            ? []
            : shown.Select(o => new AgendaOrganisationResponse(o.Slug, o.Name, new OrganisationLabelResponse(o.Label.ConfirmedBy, o.Label.Domain), o.Stance)).ToList();

    /// <summary>A bill the catalogue knows has numbers (zeros while nobody took part); one it does not know has none.</summary>
    private static AgendaRecordResponse? Numbers(BillRecordState? state) =>
        state is null ? null : new AgendaRecordResponse(state.RecordId, state.Status, AppealMapping.Map(state.Counts), AppealMapping.Map(state.Threshold));
}

/// <summary>
/// The front page's agenda (design 8.7, "Cached"): the same public answer for everyone, kept in memory for
/// <see cref="Lifetime"/>, so the most visited page costs the database one read a minute. A side taken, changed or
/// withdrawn marks the copy out of date (<see cref="FrontPageFreshness"/>), and the next request reads the agenda again,
/// but never within <see cref="FrontPageFreshness.MinimumAge"/> of the copy being made. It is built only by
/// <see cref="AgendaReads"/>, which takes no caller, and holds only <see cref="AgendaResponse"/>, which has no field about
/// anyone; nothing of a member's own can enter it. The agenda page reads the same copy (owner, 2026-11-18: with a real
/// week it is as long a read as the front page's); the bill's page and <c>GET /api/v1/agenda</c> stay live.
/// </summary>
internal sealed class FrontPageAgenda(AgendaReads reads, IMemoryCache cache, FrontPageFreshness freshness)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>The cache entry's key; a test removes it to read afresh without waiting.</summary>
    public const string CacheKey = "front-page:agenda";

    public async Task<AgendaResponse> ReadAsync(CancellationToken ct)
    {
        if (freshness.TakeRebuild())
        {
            cache.Remove(CacheKey);
        }

        return (await cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            freshness.Built();
            return reads.ReadAsync(ct);
        }))!;
    }
}

/// <summary>
/// Whether the front page's copy of the agenda is out of date (design 8.7, "Cached"; owner, 2026-11-15): one per
/// application. The host marks it after a side is taken, changed or withdrawn (<see cref="TakingSides"/>); other changes
/// wait for the minute. A stale copy is rebuilt by the first request once it is at least <see cref="MinimumAge"/> old;
/// a younger one is served and stays stale, so however many sides are taken the page reads the agenda at most once every
/// two seconds. No timers: a timestamp and a flag, read and written atomically.
/// </summary>
internal sealed class FrontPageFreshness(TimeProvider clock)
{
    /// <summary>A copy is never replaced sooner than this after it was made.</summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromSeconds(2);

    private long _builtAt;
    private int _stale;

    /// <summary>A side was taken, changed or withdrawn: the copy no longer shows the counts.</summary>
    public void MarkStale() => Volatile.Write(ref _stale, 1);

    /// <summary>Whether the copy is marked out of date (read by tests; the page asks <see cref="TakeRebuild"/>).</summary>
    public bool IsStale => Volatile.Read(ref _stale) == 1;

    /// <summary>
    /// True when the copy is stale and old enough to be replaced; the flag is then cleared by this one caller, who
    /// rebuilds. A side marked meanwhile sets it again for the next copy.
    /// </summary>
    public bool TakeRebuild()
    {
        if (Volatile.Read(ref _stale) == 0 || clock.GetUtcNow().UtcTicks - Interlocked.Read(ref _builtAt) < MinimumAge.Ticks)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _stale, 0, 1) == 1;
    }

    /// <summary>A new copy is being made now, from the database as it is: it is not stale (a side marked after this sets it again).</summary>
    public void Built()
    {
        Volatile.Write(ref _stale, 0);
        Interlocked.Exchange(ref _builtAt, clock.GetUtcNow().UtcTicks);
    }
}

/// <summary>
/// Server-side <see cref="IAgendaApi"/> for the server-rendered agenda page: the front page's copy, kept a minute
/// (<see cref="FrontPageAgenda"/>).
/// </summary>
internal sealed class ServerAgendaApi(FrontPageAgenda agenda) : IAgendaApi
{
    public async Task<ApiResult<AgendaResponse>> GetAgendaAsync(CancellationToken cancellationToken = default) =>
        ApiResult<AgendaResponse>.Ok(await agenda.ReadAsync(cancellationToken));
}
