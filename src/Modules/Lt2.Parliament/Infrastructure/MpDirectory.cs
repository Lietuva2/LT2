using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Parliament.Infrastructure;

/// <summary><see cref="IMpDirectory"/>: sitting MPs of the current term, their parties, and the sitting days.</summary>
internal sealed class MpDirectory(IDbContextFactory<ParliamentDb> contexts) : IMpDirectory
{
    public async Task<IReadOnlyList<MpRecipient>> SittingMpsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await db.Terms.AsNoTracking().OrderByDescending(t => t.From).Select(t => (int?)t.Id).FirstOrDefaultAsync(cancellationToken);
        if (term is null)
        {
            return [];
        }

        var rows = await (from m in db.Mandates.AsNoTracking()
                          join p in db.People.AsNoTracking() on m.PersonId equals p.Id
                          where m.TermId == term && m.To == null
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
            .Select(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s, PlannedAgenda.Vilnius).DateTime))
            .Where(d => d >= from && d <= to);
        return planned.Concat(held).Distinct().Order().ToList();
    }
}
