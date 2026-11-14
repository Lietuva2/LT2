using Lt2.Parliament.Import.Parsing;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Parliament.Infrastructure;

/// <summary><see cref="IBillCatalogue"/> for the Appeals module: bills by number, a context per call.</summary>
internal sealed class BillCatalogue(IDbContextFactory<ParliamentDb> contexts) : IBillCatalogue
{
    public async Task<BillFacts?> FindAsync(string number, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var normalised = number.Trim().ToUpperInvariant();
        return await db.Bills.AsNoTracking()
            .Where(b => b.Number == normalised)
            .Select(b => new BillFacts(b.Number, b.Title))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, BillFacts>> FindManyAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var list = numbers.ToList();
        var found = await db.Bills.AsNoTracking()
            .Where(b => list.Contains(b.Number))
            .Select(b => new BillFacts(b.Number, b.Title))
            .ToListAsync(cancellationToken);
        return found.ToDictionary(b => b.Number, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<BillFacts>> ActiveAsync(CancellationToken cancellationToken)
    {
        // The same rule as FinalVoteAsync: a bill is finished once it has a stage result at the adoption stage.
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Bills.AsNoTracking()
            .Where(b => !db.Votes.Any(v => v.BillId == b.Id && v.Kind == VoteKind.StageResult && v.Stage == BillStage.Adoption))
            .OrderBy(b => b.Number)
            .Select(b => new BillFacts(b.Number, b.Title))
            .ToListAsync(cancellationToken);
    }

    public async Task<BillFinalVote?> FinalVoteAsync(string number, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var normalised = number.Trim().ToUpperInvariant();
        return await (from b in db.Bills.AsNoTracking()
                      join v in db.Votes.AsNoTracking() on b.Id equals v.BillId
                      where b.Number == normalised && v.Kind == VoteKind.StageResult && v.Stage == BillStage.Adoption
                      orderby v.HeldAt descending
                      select new BillFinalVote(v.Id, v.HeldAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StageVoteFacts>> StageResultsAsync(IReadOnlyCollection<string> numbers, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var list = numbers.ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await (from b in db.Bills.AsNoTracking()
                          join v in db.Votes.AsNoTracking() on b.Id equals v.BillId
                          where list.Contains(b.Number) && v.Kind == VoteKind.StageResult && v.HeldAt >= since
                          orderby v.HeldAt, v.Id
                          select new { b.Number, v })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new StageVoteFacts(
                r.v.Id, r.Number, Codes.Of(r.v.Stage), r.v.HeldAt, r.v.IsConsensus, r.v.HasResult, r.v.For, r.v.Against, r.v.Abstain, r.v.OfficialNote))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<long, string>> ChoicesAsync(long voteId, IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken)
    {
        var ids = seimasPersonIds.ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await (from b in db.Ballots.AsNoTracking()
                          join p in db.People.AsNoTracking() on b.PersonId equals p.Id
                          where b.VoteId == voteId && ids.Contains(p.SeimasPersonId)
                          select new { p.SeimasPersonId, b.Choice })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.SeimasPersonId, r => Codes.Of(r.Choice));
    }
}