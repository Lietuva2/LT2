using System.Globalization;
using Lt2.Parliament.Import.Parsing;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Parliament.Infrastructure;

/// <summary><see cref="IBillCatalogue"/> for the Appeals module: bills by number, a context per call.</summary>
internal sealed class BillCatalogue(IDbContextFactory<ParliamentDb> contexts) : IBillCatalogue
{
    // Official wording confirmed in the full proceedings, e.g. vote -60862. The outcome and the motion both matter.
    private const string ApprovedRejection = "Pritarta – dėl pagrindinio komiteto siūlymo atmesti šį projektą";

    public async Task<BillFacts?> FindAsync(string number, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var normalised = Normalise(number);
        return await db.Bills.AsNoTracking()
            .Where(b => b.Number == normalised)
            .Select(b => new BillFacts(b.Number, b.Title))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, BillFacts>> FindManyAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var list = numbers.Select(Normalise).Distinct().ToList();
        var found = await db.Bills.AsNoTracking()
            .Where(b => list.Contains(b.Number))
            .Select(b => new BillFacts(b.Number, b.Title))
            .ToListAsync(cancellationToken);
        return found.ToDictionary(b => b.Number, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<BillFacts>> AllAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Bills.AsNoTracking()
            .OrderBy(b => b.Number)
            .Select(b => new BillFacts(b.Number, b.Title))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Bills.AsNoTracking().CountAsync(cancellationToken);
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
        var normalised = Normalise(number);
        return await (from b in db.Bills.AsNoTracking()
                      join v in db.Votes.AsNoTracking() on b.Id equals v.BillId
                      where b.Number == normalised && v.Kind == VoteKind.StageResult && v.Stage == BillStage.Adoption
                      orderby v.HeldAt descending
                      select new BillFinalVote(v.Id, v.HeldAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, BillRejection>> RejectionsAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken)
    {
        var list = numbers.Select(Normalise).Distinct().ToList();
        if (list.Count == 0)
        {
            return new Dictionary<string, BillRejection>(StringComparer.Ordinal);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var votes = db.Votes.AsNoTracking();
        var decisions = await (from bill in db.Bills.AsNoTracking()
                               join vote in votes on bill.Id equals vote.BillId
                               where list.Contains(bill.Number) && vote.Wording == ApprovedRejection
                                   && !votes.Any(later => later.BillId == bill.Id && later.Id != vote.Id
                                       && (later.HeldAt > vote.HeldAt || (later.HeldAt == vote.HeldAt && later.Wording != ApprovedRejection)))
                               select new { bill.Number, vote.Id, vote.HeldAt }).ToListAsync(cancellationToken);

        return decisions.OrderBy(v => v.Id).DistinctBy(v => v.Number, StringComparer.Ordinal)
            .ToDictionary(v => v.Number,
                v => new BillRejection(v.Id, v.HeldAt, string.Create(CultureInfo.InvariantCulture,
                    $"https://www.lrs.lt/sip/portal.show?p_r=37067&p_bals_id={v.Id}")),
                StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<StageVoteFacts>> StageResultsAsync(IReadOnlyCollection<string> numbers, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var list = numbers.Select(Normalise).Distinct().ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await (from b in db.Bills.AsNoTracking()
                          join v in db.Votes.AsNoTracking() on b.Id equals v.BillId
                          where list.Contains(b.Number) && v.Kind == VoteKind.StageResult && v.HeldAt >= since
                          orderby v.HeldAt, v.Id
                          select new { b.Number, v })
            .ToListAsync(cancellationToken);
        return rows.Select(r => Facts(r.Number, r.v)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, StageVoteFacts>> LatestStageResultsAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken)
    {
        var list = numbers.Select(Normalise).Distinct().ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await (from b in db.Bills.AsNoTracking()
                          where list.Contains(b.Number)
                          let latest = db.Votes.AsNoTracking()
                              .Where(v => v.BillId == b.Id && v.Kind == VoteKind.StageResult)
                              .OrderByDescending(v => v.HeldAt).ThenByDescending(v => v.Id)
                              .FirstOrDefault()
                          where latest != null
                          select new { b.Number, Vote = latest })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Number, r => Facts(r.Number, r.Vote!), StringComparer.OrdinalIgnoreCase);
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

    private static StageVoteFacts Facts(string number, Domain.Vote v) =>
        new(v.Id, number, Codes.Of(v.Stage), v.HeldAt, v.IsConsensus, v.HasResult, v.For, v.Against, v.Abstain, v.OfficialNote);

    /// <summary>Bill numbers as the Seimas writes them (<c>XVP-0412</c>): every lookup takes them in this form.</summary>
    private static string Normalise(string number) => number.Trim().ToUpperInvariant();
}
