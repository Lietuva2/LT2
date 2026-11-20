using System.Globalization;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Analytics;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Microsoft.Extensions.Caching.Memory;

namespace Lt2.Web.Services;

/// <summary>
/// The kept copies of the analytics (design 8.26.6, the owner, 2026-11-16: "we may cache this quite long, say per-session"),
/// one per application, in server memory only: never a table, never the browser, never a cacheable HTTP answer.
/// <list type="bullet">
/// <item>The public answer, one copy for everybody per filter combination and data version. A copy older than
/// <see cref="PublicLifetime"/> is still served as it is while one refresh runs in the background (<see cref="Refresh"/>),
/// so no visitor waits except the first after a start or after the data version changed. A record closing, hidden or
/// restored, or a finished import, changes the version, so those show at once; sides taken on open bills show within the
/// hour (the page says when its numbers were counted). Who refreshes is this one method: a periodic job could call
/// <see cref="PublicAsync"/> instead, without changing anything else.</item>
/// <item>A member's own comparison, one copy per account, made the first time the member's page asks and kept while the
/// member keeps using it (<see cref="PersonalIdle"/>), at most <see cref="PersonalLimit"/> accounts. It reveals the
/// member's sides, so it is handled as a side: never logged, made again when the data version changes, and dropped at
/// sign-out, account deletion and withdrawal of the positions consent (<see cref="Forget"/>).</item>
/// </list>
/// </summary>
internal sealed class AnalyticsCopies(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AnalyticsCopies> logger) : IDisposable
{
    public static readonly TimeSpan PublicLifetime = TimeSpan.FromHours(1);

    /// <summary>A public copy nobody asked for in a day is let go (a key holds an old data version by then).</summary>
    public static readonly TimeSpan PublicIdle = TimeSpan.FromDays(1);
    public static readonly TimeSpan PersonalIdle = TimeSpan.FromHours(12);
    public const int PersonalLimit = 10_000;

    private readonly MemoryCache _public = new(new MemoryCacheOptions { SizeLimit = 256 });
    private readonly MemoryCache _personal = new(new MemoryCacheOptions { SizeLimit = PersonalLimit });
    private readonly SemaphoreSlim _first = new(1, 1);

    /// <summary>One public copy, its age and whether a refresh is running.</summary>
    private sealed class PublicCopy(AnalyticsResponse answer, DateTimeOffset madeAt)
    {
        public AnalyticsResponse Answer { get; set; } = answer;
        public DateTimeOffset MadeAt { get; set; } = madeAt;
        public int Refreshing;
    }

    /// <summary>
    /// The public copy for <paramref name="key"/>: made now if there is none (once, however many ask at the same time),
    /// otherwise served as it is, with a refresh started in the background once it is older than <see cref="PublicLifetime"/>.
    /// <paramref name="make"/> gets a scope of its own, so a refresh outlives the request that started it.
    /// </summary>
    public async Task<AnalyticsResponse> PublicAsync(string key, Func<IServiceProvider, Task<AnalyticsResponse>> make)
    {
        if (_public.TryGetValue(key, out PublicCopy? copy) && copy is not null)
        {
            if (clock.GetUtcNow() - copy.MadeAt >= PublicLifetime)
            {
                Refresh(copy, make);
            }

            return copy.Answer;
        }

        await _first.WaitAsync();
        try
        {
            if (_public.TryGetValue(key, out copy) && copy is not null)
            {
                return copy.Answer;
            }

            copy = new PublicCopy(await MakeAsync(make), clock.GetUtcNow());
            _public.Set(key, copy, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = PublicIdle });
            return copy.Answer;
        }
        finally
        {
            _first.Release();
        }
    }

    /// <summary>Who refreshes a public copy: one background refresh at a time per copy; the old copy is served meanwhile.</summary>
    private void Refresh(PublicCopy copy, Func<IServiceProvider, Task<AnalyticsResponse>> make)
    {
        if (Interlocked.CompareExchange(ref copy.Refreshing, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                copy.Answer = await MakeAsync(make);
                copy.MadeAt = clock.GetUtcNow();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The old copy stays; the next request after the lifetime tries again.
                logger.LogWarning(exception, "Refreshing the analytics failed");
            }
            finally
            {
                Volatile.Write(ref copy.Refreshing, 0);
            }
        });
    }

    private async Task<AnalyticsResponse> MakeAsync(Func<IServiceProvider, Task<AnalyticsResponse>> make)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await make(scope.ServiceProvider);
    }

    public async Task<MyAgreementResponse> PersonalAsync(Guid userId, string version, Func<Task<MyAgreementResponse>> make)
    {
        if (_personal.TryGetValue(userId, out (string Version, MyAgreementResponse Answer) kept) && kept.Version == version)
        {
            return kept.Answer;
        }

        var answer = await make();
        _personal.Set(userId, (version, answer), new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = PersonalIdle });
        return answer;
    }

    /// <summary>Drops the member's own copy: at sign-out, account deletion and withdrawal of the positions consent.</summary>
    public void Forget(Guid userId) => _personal.Remove(userId);

    /// <summary>Tests: every copy made again on the next request.</summary>
    public void Clear()
    {
        _public.Compact(1.0);
        _personal.Compact(1.0);
    }

    public void Dispose()
    {
        _public.Dispose();
        _personal.Dispose();
        _first.Dispose();
    }
}
/// <summary>
/// The public analytics (design 8.26.1–8.26.3), composed by the host: the Parliament module gives the term, the bills
/// discussed, the closing votes' numbers and faction unity; the Appeals module the bills' shown and frozen counts; the
/// Identity module which sitting MPs took over their profile. Nothing about the caller; the rules are
/// <see cref="AnalyticsRules"/>; kept in <see cref="AnalyticsCopies"/>.
/// </summary>
internal sealed class AnalyticsReads(
    IParliamentQueries parliament, IAppealsModule appeals, IMpDirectory mps, IPoliticians politicians, AnalyticsCopies copies, TimeProvider clock)
{
    /// <summary>What a kept copy was made from: the closed records and the last finished import (design 8.26.6).</summary>
    public async Task<string> DataVersionAsync(CancellationToken ct)
    {
        var closed = await appeals.ClosedRecordsVersionAsync(ct);
        var imported = (await parliament.ImportsAsync(ct)).LastSuccessAt;
        return closed + "|" + (imported?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "-");
    }

    /// <summary>
    /// The answer for a period, the accounts counted and the list's query. The kept copy is per period and accounts only and
    /// holds every closed bill of the period; the list's filters, order and length are applied to it in memory on each
    /// request (<see cref="Select"/>), so they make no copy of their own and never count anything again: a term holds at
    /// most a few hundred closed bills (design 8.26.6).
    /// </summary>
    public async Task<AnalyticsResponse> ReadAsync(int? year, bool olderAccounts, AnalyticsBillQuery bills, CancellationToken ct)
    {
        var version = await DataVersionAsync(ct);
        var key = string.Create(CultureInfo.InvariantCulture, $"{year?.ToString(CultureInfo.InvariantCulture) ?? "term"}|{olderAccounts}|{version}");
        var whole = await copies.PublicAsync(key, services => services.GetRequiredService<AnalyticsReads>().BuildAsync(year, olderAccounts, CancellationToken.None));
        return Select(whole, bills);
    }

    /// <summary>
    /// The list's part of the answer (design 8.26.1 item 2): of every closed bill of the period in <paramref name="whole"/>,
    /// those the filters match, in the query's order, the first <see cref="AnalyticsBillQuery.Limit"/> of them. The figures
    /// and the factions are left as they are: they follow the period and the accounts counted, not the list's filters.
    /// </summary>
    public static AnalyticsResponse Select(AnalyticsResponse whole, AnalyticsBillQuery query)
    {
        ArgumentNullException.ThrowIfNull(whole);
        ArgumentNullException.ThrowIfNull(query);
        bool? agreed = query.Result switch
        {
            AnalyticsBillQuery.Agreed => true,
            AnalyticsBillQuery.Differed => false,
            _ => null,
        };
        var order = query.Sort switch
        {
            AnalyticsBillQuery.Difference => ComparedBillOrder.Difference,
            AnalyticsBillQuery.Positions => ComparedBillOrder.Positions,
            _ => ComparedBillOrder.Latest,
        };
        var found = whole.Bills
            .Where(b => AnalyticsRules.InDays(VilniusTime.DateOf(b.VotedAt), query.From, query.To)
                        && AnalyticsRules.MatchesSearch(query.Search, b.Number, b.Title)
                        && AnalyticsRules.MatchesResult(agreed, b.Agreed)
                        && AnalyticsRules.MatchesMajority(query.Majority, b.SeimasMajority))
            .ToList();
        var ordered = AnalyticsRules.OrderBills(found, order, b => new ComparedBillKey(b.VotedAt, b.Number, b.Difference, b.MembersFor + b.MembersAgainst));
        return whole with
        {
            BillsQuery = query,
            BillsClosed = whole.Bills.Count,
            BillsFound = found.Count,
            Bills = ordered.Take(query.Limit).ToList(),
        };
    }

    /// <summary>Counts everything afresh (no kept copy): what a refresh of <see cref="AnalyticsCopies"/> runs. It holds every closed bill of the period, newest first.</summary>
    public async Task<AnalyticsResponse> BuildAsync(int? year, bool olderAccounts, CancellationToken ct)
    {
        var countedAt = clock.GetUtcNow();
        var term = await parliament.CurrentTermAsync(ct);
        var sitting = await mps.SittingMpsAsync(ct);
        var takenOver = (await politicians.TakenOverAsync(sitting.Select(m => m.SeimasPersonId).ToList(), ct)).Count;
        if (term is null)
        {
            return new AnalyticsResponse(
                "", [], null, olderAccounts, AnalyticsRules.SmallSample, countedAt,
                new AnalyticsFiguresResponse(0, 0, 0, 0, 0, null, takenOver, sitting.Count), new AnalyticsBillQuery(), 0, 0, [], []);
        }

        var today = VilniusTime.Today(clock);
        var years = AnalyticsRules.Years(term.From, term.To, today);
        var shownYear = year is { } y && years.Contains(y) ? year : null;
        var (from, to) = AnalyticsRules.Period(term.From, term.To, today, shownYear);

        // The four figures (design 8.26.1): the bills discussed in the period and their counts as their pages show them.
        var discussed = await parliament.BillsDiscussedAsync(from, to, ct);
        var states = (await appeals.BillStatesAsync(discussed, ct)).Values.Where(s => s.Status != BillRecordStatuses.Rejected).ToList();
        var takenPart = states.Count(s => Sample(s.Counts, olderAccounts) > 0);
        var positions = states.Sum(s => Sample(s.Counts, olderAccounts));

        // The closed bills of the period, compared (8.26.2), the latest closing vote first.
        var closed = await appeals.ClosedRecordsAsync(ct);
        var votes = await parliament.VotesAsync(closed.Select(c => c.ClosingVoteId).ToList(), ct);
        var bills = new List<BillComparisonResponse>();
        foreach (var record in closed
                     .Where(c => votes.ContainsKey(c.ClosingVoteId))
                     .Select(c => (Record: c, Vote: votes[c.ClosingVoteId]))
                     .Where(x => VilniusTime.DateOf(x.Vote.Vote.HeldAt) is var day && day >= from && day <= to)
                     .OrderByDescending(x => x.Vote.Vote.HeldAt).ThenBy(x => x.Record.BillNumber, StringComparer.Ordinal))
        {
            var (membersFor, membersAgainst) = Sides(record.Record.Counts, olderAccounts);
            if (membersFor + membersAgainst == 0)
            {
                continue;
            }

            var vote = record.Vote;
            var compared = AnalyticsRules.Compare(
                new SeimasVoteNumbers(vote.Vote.IsConsensus, vote.Totals.For, vote.Totals.Against, vote.Totals.Abstain), membersFor, membersAgainst);
            bills.Add(new BillComparisonResponse(
                record.Record.BillNumber,
                vote.Vote.Bill?.Title ?? record.Record.BillNumber,
                vote.Vote.Id,
                vote.Vote.HeldAt,
                vote.Vote.IsConsensus,
                compared.SeimasShareFor,
                compared.SeimasMajority,
                membersFor,
                membersAgainst,
                compared.MembersShareFor,
                compared.Compared,
                compared.Agreed,
                AnalyticsRules.Difference(compared.SeimasShareFor, compared.MembersShareFor),
                string.IsNullOrWhiteSpace(vote.OfficialNote) ? null : vote.OfficialNote));
        }

        var comparedCount = bills.Count(b => b.Compared);
        var agreed = bills.Count(b => b.Agreed == true);
        var factions = (await parliament.FactionUnityAsync(from, to, ct))
            .Select(f => new FactionUnityResponse(
                f.Faction.Slug,
                f.Faction.Name,
                f.Seats,
                f.VotesWithPosition,
                f.FreeVotes,
                f.Share,
                f.SmallSample,
                f.MostSplit.Select(s => new SplitVoteResponse(
                    s.Vote.Id, s.Vote.HeldAt, s.Vote.Bill?.Number, s.Vote.Bill?.Title ?? s.AgendaItemTitle, s.Vote.Stage, s.Position, s.For, s.Against, s.Abstain)).ToList()))
            .ToList();

        return new AnalyticsResponse(
            term.Name,
            years,
            shownYear,
            olderAccounts,
            AnalyticsRules.SmallSample,
            countedAt,
            new AnalyticsFiguresResponse(
                takenPart, discussed.Count, positions, agreed, comparedCount, AnalyticsRules.ShareFromSample(agreed, comparedCount), takenOver, sitting.Count),
            new AnalyticsBillQuery(Limit: AnalyticsBillQuery.MaxLimit),
            bills.Count,
            bills.Count,
            bills,
            factions);
    }

    /// <summary>The counts the filter chooses: every account, or only those older than 30 days (design 8.26.1).</summary>
    private static (int For, int Against) Sides(SideCounts counts, bool olderAccounts) =>
        olderAccounts ? (counts.ForOlderThan30Days, counts.AgainstOlderThan30Days) : (counts.For, counts.Against);

    private static int Sample(SideCounts counts, bool olderAccounts) => Sides(counts, olderAccounts) is var (f, a) ? f + a : 0;
}

/// <summary>
/// "Kas Seime balsuoja taip, kaip prašote jūs" (design 8.26.4), for the caller only: their own sides on closed records (Appeals) against
/// the MPs' public ballots and the factions' positions at those records' closing votes (Parliament). Made the first time the
/// member's page asks and kept for the member in server memory (<see cref="AnalyticsCopies"/>, the owner, 2026-11-16);
/// never stored elsewhere or logged, and the answer is under <c>/api</c>, which says <c>no-store</c>.
/// </summary>
internal sealed class MyAgreementReads(IAppealsModule appeals, IParliamentQueries parliament, AnalyticsReads analytics, AnalyticsCopies copies)
{
    public async Task<MyAgreementResponse> ReadAsync(Guid userId, CancellationToken ct) =>
        await copies.PersonalAsync(userId, await analytics.DataVersionAsync(ct), () => BuildAsync(userId, ct));

    private async Task<MyAgreementResponse> BuildAsync(Guid userId, CancellationToken ct)
    {
        var sides = await appeals.MyClosedSidesAsync(userId, ct);
        var votes = await parliament.VotesAsync(sides.Select(s => s.ClosingVoteId).ToList(), ct);
        var consensus = sides.Count(s => votes.GetValueOrDefault(s.ClosingVoteId)?.Vote.IsConsensus == true);
        var recorded = sides.Where(s => votes.GetValueOrDefault(s.ClosingVoteId) is { Vote.IsConsensus: false }).ToList();
        var info = await parliament.BallotsAtAsync(recorded.Select(s => s.ClosingVoteId).ToList(), ct);

        // MPs are named first name first and ordered, as every list of MPs, by last name, then first name.
        var ballotsByMp = info.Ballots.GroupBy(b => b.MpSlug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.GroupBy(b => b.VoteId).ToDictionary(v => v.Key, v => v.First().Choice), StringComparer.Ordinal);
        var mpRows = ballotsByMp
            .Where(g => info.Mps.ContainsKey(g.Key))
            .Select(g =>
            {
                var mp = info.Mps[g.Key];
                var agreement = AnalyticsRules.AgreementOf(recorded.Select(s => (s.Side, g.Value.GetValueOrDefault(s.ClosingVoteId))));
                var row = new AgreementRowResponse(mp.Slug, mp.FirstName + " " + mp.LastName, mp.Faction?.Name, null, !mp.Sitting, agreement.Matched, agreement.Common, agreement.Free, agreement.Share);
                return (Row: row, Agreement: agreement, SortName: mp.LastName + " " + mp.FirstName);
            });

        var factionRows = info.FactionPositions.GroupBy(p => p.Faction.Slug, StringComparer.Ordinal)
            .Select(g =>
            {
                var byVote = g.GroupBy(p => p.VoteId).ToDictionary(v => v.Key, v => v.First().Position);
                var faction = g.First().Faction;
                var agreement = AnalyticsRules.AgreementOf(recorded.Select(s => (s.Side, byVote.GetValueOrDefault(s.ClosingVoteId))));
                var row = new AgreementRowResponse(faction.Slug, faction.Name, null, info.Seats.GetValueOrDefault(faction.Slug), false, agreement.Matched, agreement.Common, agreement.Free, agreement.Share);
                return (Row: row, Agreement: agreement, SortName: faction.Name);
            });

        return new MyAgreementResponse(
            sides.Count,
            consensus,
            AnalyticsRules.SmallSample,
            AnalyticsRules.OrderForCaller(factionRows, r => r.Agreement, r => r.SortName).Select(r => r.Row).ToList(),
            AnalyticsRules.OrderForCaller(mpRows, r => r.Agreement, r => r.SortName).Select(r => r.Row).ToList());
    }
}

/// <summary>Server-side <see cref="IAnalyticsApi"/> for the server-rendered page: the public analytics only.</summary>
internal sealed class ServerAnalyticsApi(AnalyticsReads reads) : IAnalyticsApi
{
    public async Task<ApiResult<AnalyticsResponse>> GetAsync(int? year, bool olderAccounts, AnalyticsBillQuery? bills = null, CancellationToken cancellationToken = default) =>
        ApiResult<AnalyticsResponse>.Ok(await reads.ReadAsync(year, olderAccounts, bills ?? new AnalyticsBillQuery(), cancellationToken));

    /// <summary>
    /// Refused on the server, always (design 8.26.4): the comparison is built only for the browser that asks for it, so no
    /// server-rendered page can ever hold it.
    /// </summary>
    public Task<ApiResult<MyAgreementResponse>> GetMyAgreementAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ApiResult<MyAgreementResponse>.Fail(new ApiProblem { Status = 404, Code = "browser-only" }));
}
