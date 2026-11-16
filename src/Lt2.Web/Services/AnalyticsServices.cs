using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Analytics;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Parliament;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>
/// The public analytics (design 8.26.1–8.26.3), composed by the host: the Parliament module gives the term, the bills
/// discussed, the closing votes' numbers and faction unity; the Appeals module the bills' shown and frozen counts; the
/// Identity module which sitting MPs took over their profile. Nothing about the caller; the rules are
/// <see cref="AnalyticsRules"/>.
/// </summary>
internal sealed class AnalyticsReads(
    IParliamentQueries parliament, IAppealsModule appeals, IMpDirectory mps, IPoliticians politicians, TimeProvider clock)
{
    /// <summary>How many compared bills the plot shows: the latest by their closing vote (design 8.26.1).</summary>
    public const int PlotSize = 10;

    public async Task<AnalyticsResponse> ReadAsync(int? year, bool olderAccounts, CancellationToken ct)
    {
        var term = await parliament.CurrentTermAsync(ct);
        var sitting = await mps.SittingMpsAsync(ct);
        var takenOver = (await politicians.TakenOverAsync(sitting.Select(m => m.SeimasPersonId).ToList(), ct)).Count;
        if (term is null)
        {
            return new AnalyticsResponse(
                "", [], null, olderAccounts, AnalyticsRules.SmallSample, new AnalyticsFiguresResponse(0, 0, 0, 0, 0, null, takenOver, sitting.Count), [], []);
        }

        var today = VilniusTime.Today(clock);
        var years = AnalyticsRules.Years(term.From, term.To, today);
        var shownYear = year is { } y && years.Contains(y) ? year : null;
        var (from, to) = AnalyticsRules.Period(term.From, term.To, today, shownYear);

        // The four figures (design 8.26.1): the bills discussed in the period and their counts as their pages show them.
        var discussed = await parliament.BillsDiscussedAsync(from, to, ct);
        var states = (await appeals.BillStatesAsync(discussed, ct)).Values.Where(s => s.Status != "rejected").ToList();
        var takenPart = states.Count(s => Sample(s.Counts, olderAccounts) > 0);
        var positions = states.Sum(s => Sample(s.Counts, olderAccounts));

        // The closed bills of the period, compared (8.26.2), the latest closing vote first.
        var closed = await appeals.ClosedRecordsAsync(ct);
        var votes = await parliament.VotesAsync(closed.Select(c => c.ClosingVoteId).ToList(), ct);
        var bills = new List<BillComparisonResponse>();
        var dots = 0;
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
            var dot = compared.Dot && dots < PlotSize;
            dots += dot ? 1 : 0;
            bills.Add(new BillComparisonResponse(
                record.Record.BillNumber,
                vote.Vote.Bill?.Title ?? record.Record.BillNumber,
                vote.Vote.Id,
                vote.Vote.HeldAt,
                vote.Vote.IsConsensus,
                compared.SeimasShareFor,
                compared.SeimasMajorityFor,
                membersFor,
                membersAgainst,
                compared.MembersShareFor,
                compared.Compared,
                compared.Agreed,
                dot));
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
            new AnalyticsFiguresResponse(
                takenPart, discussed.Count, positions, agreed, comparedCount, AnalyticsRules.ShareFromSample(agreed, comparedCount), takenOver, sitting.Count),
            bills,
            factions);
    }

    /// <summary>The counts the filter chooses: every account, or only those older than 30 days (design 8.26.1).</summary>
    private static (int For, int Against) Sides(SideCounts counts, bool olderAccounts) =>
        olderAccounts ? (counts.ForOlderThan30Days, counts.AgainstOlderThan30Days) : (counts.For, counts.Against);

    private static int Sample(SideCounts counts, bool olderAccounts) => Sides(counts, olderAccounts) is var (f, a) ? f + a : 0;
}

/// <summary>
/// "Kas balsuoja taip kaip jūs" (design 8.26.4), for the caller only: their own sides on closed records (Appeals) against
/// the MPs' public ballots and the factions' positions at those records' closing votes (Parliament). Computed per request;
/// nothing of it is stored, cached or logged, and the answer is under <c>/api</c>, which says <c>no-store</c>.
/// </summary>
internal sealed class MyAgreementReads(IAppealsModule appeals, IParliamentQueries parliament)
{
    public async Task<MyAgreementResponse> ReadAsync(Guid userId, CancellationToken ct)
    {
        var sides = await appeals.MyClosedSidesAsync(userId, ct);
        var votes = await parliament.VotesAsync(sides.Select(s => s.ClosingVoteId).ToList(), ct);
        var consensus = sides.Count(s => votes.GetValueOrDefault(s.ClosingVoteId)?.Vote.IsConsensus == true);
        var recorded = sides.Where(s => votes.GetValueOrDefault(s.ClosingVoteId) is { Vote.IsConsensus: false }).ToList();
        var info = await parliament.BallotsAtAsync(recorded.Select(s => s.ClosingVoteId).ToList(), ct);

        var ballotsByMp = info.Ballots.GroupBy(b => b.MpSlug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.GroupBy(b => b.VoteId).ToDictionary(v => v.Key, v => v.First().Choice), StringComparer.Ordinal);
        var mpRows = ballotsByMp
            .Where(g => info.Mps.ContainsKey(g.Key))
            .Select(g =>
            {
                var mp = info.Mps[g.Key];
                var agreement = AnalyticsRules.AgreementOf(recorded.Select(s => (s.Side, g.Value.GetValueOrDefault(s.ClosingVoteId))));
                return (Row: new AgreementRowResponse(mp.Slug, mp.FirstName + " " + mp.LastName, mp.Faction?.Name, null, !mp.Sitting, agreement.Matched, agreement.Common, agreement.Free, agreement.Share), Agreement: agreement);
            });

        var factionRows = info.FactionPositions.GroupBy(p => p.Faction.Slug, StringComparer.Ordinal)
            .Select(g =>
            {
                var byVote = g.GroupBy(p => p.VoteId).ToDictionary(v => v.Key, v => v.First().Position);
                var faction = g.First().Faction;
                var agreement = AnalyticsRules.AgreementOf(recorded.Select(s => (s.Side, byVote.GetValueOrDefault(s.ClosingVoteId))));
                return (Row: new AgreementRowResponse(faction.Slug, faction.Name, null, info.Seats.GetValueOrDefault(faction.Slug), false, agreement.Matched, agreement.Common, agreement.Free, agreement.Share), Agreement: agreement);
            });

        return new MyAgreementResponse(
            sides.Count,
            consensus,
            AnalyticsRules.SmallSample,
            AnalyticsRules.OrderForCaller(factionRows, r => r.Agreement, r => r.Row.Name).Select(r => r.Row).ToList(),
            AnalyticsRules.OrderForCaller(mpRows, r => r.Agreement, r => r.Row.Name).Select(r => r.Row).ToList());
    }
}

/// <summary>
/// Server-side <see cref="IAnalyticsApi"/> for the server-rendered page. The caller's comparison is asked only by the
/// island in the browser; asked on the server by mistake, it answers with the request's own account, and nothing for a visitor.
/// </summary>
internal sealed class ServerAnalyticsApi(AnalyticsReads reads, MyAgreementReads mine, IHttpContextAccessor accessor) : IAnalyticsApi
{
    public async Task<ApiResult<AnalyticsResponse>> GetAsync(int? year, bool olderAccounts, CancellationToken cancellationToken = default) =>
        ApiResult<AnalyticsResponse>.Ok(await reads.ReadAsync(year, olderAccounts, cancellationToken));

    public async Task<ApiResult<MyAgreementResponse>> GetMyAgreementAsync(CancellationToken cancellationToken = default) =>
        accessor.HttpContext?.User.UserId() is { } id
            ? ApiResult<MyAgreementResponse>.Ok(await mine.ReadAsync(id, cancellationToken))
            : ApiResult<MyAgreementResponse>.Fail(new ApiProblem { Status = 401, Code = "auth.required" });
}
