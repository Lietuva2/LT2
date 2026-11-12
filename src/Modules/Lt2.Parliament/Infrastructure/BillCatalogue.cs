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
}
