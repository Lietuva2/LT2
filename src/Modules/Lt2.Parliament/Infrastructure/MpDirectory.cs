using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Parliament.Infrastructure;

/// <summary><see cref="IMpDirectory"/>: sitting MPs of the current term, their parties, and the sitting days.</summary>
internal sealed class MpDirectory(IDbContextFactory<ParliamentDb> contexts, TimeProvider clock) : IMpDirectory
{
    public async Task<IReadOnlyList<MpRecipient>> SittingMpsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTerm.FindAsync(db, cancellationToken);
        if (term is null)
        {
            return [];
        }

        var rows = await (from m in db.Mandates.AsNoTracking().Where(CurrentTerm.SittingOn(VilniusTime.Today(clock)))
                          join p in db.People.AsNoTracking() on m.PersonId equals p.Id
                          where m.TermId == term.Id
                          select new
                          {
                              p.Slug,
                              p.FirstName,
                              p.LastName,
                              m.NominatingParty,
                              Official = db.Contacts.Any(c => c.PersonId == p.Id && c.Value.EndsWith("@lrs.lt")),
                          })
            .ToListAsync(cancellationToken);
        return rows
            .OrderBy(r => r.LastName, StringComparer.Create(new System.Globalization.CultureInfo("lt-LT"), false))
            .Select(r => new MpRecipient(r.Slug, r.FirstName, r.LastName, r.NominatingParty, r.Official))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> PartiesAsync(CancellationToken cancellationToken)
    {
        var mps = await SittingMpsAsync(cancellationToken);
        return mps.Select(m => m.Party).OfType<string>().Where(p => p.Length > 0).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Create(new System.Globalization.CultureInfo("lt-LT"), false))
            .ToList();
    }

    public async Task<IReadOnlyList<DateOnly>> SittingDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var planned = await db.PlannedItems.AsNoTracking()
            .Where(p => p.GoneAt == null && p.PlannedDate >= from && p.PlannedDate <= to)
            .Select(p => p.PlannedDate)
            .Distinct()
            .ToListAsync(cancellationToken);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);
        var held = (await db.Sittings.AsNoTracking().Where(s => s.StartsAt >= start && s.StartsAt < end).Select(s => s.StartsAt).ToListAsync(cancellationToken))
            .Select(s => VilniusTime.DateOf(s))
            .Where(d => d >= from && d <= to);
        return planned.Concat(held).Distinct().Order().ToList();
    }

    public async Task<MpFacts?> FindMpAsync(string slug, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // Only people with a mandate: an MP's profile exists for them (design 7.3).
        return await db.People.AsNoTracking()
            .Where(p => p.Slug == slug && db.Mandates.Any(m => m.PersonId == p.Id))
            .Select(p => new MpFacts(p.SeimasPersonId, p.Slug, p.FirstName, p.LastName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, MpFacts>> MpsAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken)
    {
        var ids = seimasPersonIds.ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.People.AsNoTracking()
            .Where(p => ids.Contains(p.SeimasPersonId))
            .Select(p => new MpFacts(p.SeimasPersonId, p.Slug, p.FirstName, p.LastName))
            .ToDictionaryAsync(p => p.SeimasPersonId, cancellationToken);
    }
}
