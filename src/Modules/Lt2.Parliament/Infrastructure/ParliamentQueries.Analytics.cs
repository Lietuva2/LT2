using Lt2.Parliament.Domain;
using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.Statistics;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Parliament.Infrastructure;

/// <summary>The reads of the analytics page (design 8.26): bills discussed, votes by id, faction unity, ballots at votes.</summary>
internal sealed partial class ParliamentQueries
{
    public async Task<IReadOnlyList<string>> BillsDiscussedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTermRowAsync(db, cancellationToken);
        if (term is null || to < from)
        {
            return [];
        }

        var (start, stop) = Window(from, to);
        return await (from i in db.AgendaItems.AsNoTracking()
                      join s in db.Sittings on i.SittingId equals s.Id
                      join ses in db.Sessions on s.SessionId equals ses.Id
                      join b in db.Bills on i.BillId equals b.Id
                      where ses.TermId == term.Id && s.StartsAt >= start && s.StartsAt < stop
                      select b.Number).Distinct().OrderBy(n => n).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, VoteSummary>> VotesAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return new Dictionary<long, VoteSummary>();
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var votes = await db.Votes.AsNoTracking().Where(v => wanted.Contains(v.Id)).ToListAsync(cancellationToken);
        var bills = await BillsByIdAsync(db, votes.Select(v => v.BillId), cancellationToken);
        return votes.ToDictionary(v => v.Id, v => Summary(v, bills));
    }

    public async Task<IReadOnlyList<FactionUnityInfo>> FactionUnityAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTermRowAsync(db, cancellationToken);
        if (term is null || to < from)
        {
            return [];
        }

        var (start, stop) = Window(from, to);
        // Recorded votes only: a consensus vote has no ballots and no faction position (design 7.3).
        var rows = await (from fp in db.FactionPositions.AsNoTracking()
                          join v in db.Votes on fp.VoteId equals v.Id
                          join i in db.AgendaItems on v.AgendaItemId equals i.Id
                          join s in db.Sittings on i.SittingId equals s.Id
                          join ses in db.Sessions on s.SessionId equals ses.Id
                          where ses.TermId == term.Id && fp.FactionId != null && v.HasResult && !v.IsConsensus && v.HeldAt >= start && v.HeldAt < stop
                          select new { FactionId = fp.FactionId!.Value, fp.VoteId, v.HeldAt, fp.Position, fp.For, fp.Against, fp.Abstain })
            .ToListAsync(cancellationToken);
        var factions = await db.Bodies.AsNoTracking().Where(b => b.TermId == term.Id && b.Kind == BodyKind.Faction).ToDictionaryAsync(b => b.Id, cancellationToken);
        var seats = await SeatsAsync(db, factions.Keys, cancellationToken);
        var unity = rows.Where(r => factions.ContainsKey(r.FactionId))
            .GroupBy(r => r.FactionId)
            .Select(g => (Faction: factions[g.Key], Unity: FactionUnityRule.Compute(g.Select(r => new FactionVoteTally(r.VoteId, r.HeldAt, r.Position, r.For, r.Against, r.Abstain)))))
            .ToList();

        var splitIds = unity.SelectMany(u => u.Unity.MostSplit.Select(t => t.VoteId)).Distinct().ToList();
        var splitVotes = await (from v in db.Votes.AsNoTracking()
                                join i in db.AgendaItems on v.AgendaItemId equals i.Id
                                where splitIds.Contains(v.Id)
                                select new { v, i.Title }).ToDictionaryAsync(x => x.v.Id, cancellationToken);
        var bills = await BillsByIdAsync(db, splitVotes.Values.Select(x => x.v.BillId), cancellationToken);

        return unity
            .Select(u => new FactionUnityInfo(
                Ref(u.Faction),
                seats.GetValueOrDefault(u.Faction.Id),
                u.Unity.VotesWithPosition,
                u.Unity.FreeVotes,
                u.Unity.Share,
                u.Unity.SmallSample,
                u.Unity.MostSplit
                    .Where(t => splitVotes.ContainsKey(t.VoteId))
                    .Select(t => new FactionSplitVote(Ref(splitVotes[t.VoteId].v, bills), splitVotes[t.VoteId].Title, Codes.Of(t.Position), t.For, t.Against, t.Abstain))
                    .ToList()))
            .OrderByDescending(f => f.Seats).ThenBy(f => f.Faction.Name, ByLithuanian)
            .ToList();
    }

    public async Task<VoteBallotsInfo> BallotsAtAsync(IReadOnlyCollection<long> voteIds, CancellationToken cancellationToken)
    {
        var ids = voteIds.Distinct().ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTermRowAsync(db, cancellationToken);
        if (term is null || ids.Count == 0)
        {
            return new VoteBallotsInfo([], [], new Dictionary<string, MpSummary>(), new Dictionary<string, int>());
        }

        var ballots = await (from b in db.Ballots.AsNoTracking()
                             join p in db.People on b.PersonId equals p.Id
                             where ids.Contains(b.VoteId)
                             select new { b.VoteId, p.Id, p.Slug, b.Choice }).ToListAsync(cancellationToken);
        // Two labels resolving to one faction in a vote are one position, from their sum (FactionUnityRule.PerVote).
        var positions = (await (from fp in db.FactionPositions.AsNoTracking()
                                join v in db.Votes on fp.VoteId equals v.Id
                                join body in db.Bodies on fp.FactionId equals body.Id
                                where ids.Contains(fp.VoteId) && body.Kind == BodyKind.Faction
                                select new { fp.VoteId, v.HeldAt, body, fp.Position, fp.For, fp.Against, fp.Abstain }).ToListAsync(cancellationToken))
            .GroupBy(p => p.body.Id)
            .SelectMany(g => FactionUnityRule.PerVote(g.Select(p => new FactionVoteTally(p.VoteId, p.HeldAt, p.Position, p.For, p.Against, p.Abstain)))
                .Select(t => new { t.VoteId, g.First().body, t.Position }))
            .ToList();
        var summaries = await SummariesAsync(db, term.Id, cancellationToken);
        var mps = ballots.Select(b => b.Id).Distinct()
            .Where(summaries.ContainsKey)
            .ToDictionary(id => summaries[id].Slug, id => summaries[id], StringComparer.Ordinal);
        var factionBodies = positions.Select(p => p.body).DistinctBy(b => b.Id).ToList();
        var seats = await SeatsAsync(db, factionBodies.Select(b => b.Id).ToList(), cancellationToken);
        return new VoteBallotsInfo(
            ballots.Where(b => mps.ContainsKey(b.Slug)).Select(b => new VoteBallot(b.VoteId, b.Slug, Codes.Of(b.Choice))).ToList(),
            positions.Select(p => new VoteFactionPosition(p.VoteId, Ref(p.body), Codes.Of(p.Position))).ToList(),
            mps,
            factionBodies.ToDictionary(b => b.Slug, b => seats.GetValueOrDefault(b.Id), StringComparer.Ordinal));
    }

    /// <summary>The members each given body has today (Vilnius).</summary>
    private async Task<Dictionary<Guid, int>> SeatsAsync(ParliamentDb db, IReadOnlyCollection<Guid> bodyIds, CancellationToken cancellationToken)
    {
        var ids = bodyIds.ToList();
        var today = Today;
        return await db.Memberships.AsNoTracking().Where(m => (m.To == null || m.To >= today) && ids.Contains(m.BodyId))
            .GroupBy(m => m.BodyId).Select(g => new { g.Key, Count = g.Select(m => m.PersonId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    /// <summary>The moments a run of Vilnius days covers: from the start of the first to the start of the day after the last.</summary>
    private static (DateTimeOffset Start, DateTimeOffset Stop) Window(DateOnly from, DateOnly to) =>
        (VilniusTime.StartOf(from), VilniusTime.StartOf(to.AddDays(1)));
}
