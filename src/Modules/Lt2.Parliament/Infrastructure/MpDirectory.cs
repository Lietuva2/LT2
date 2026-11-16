using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Parliament.Infrastructure;

/// <summary><see cref="IMpDirectory"/>: sitting MPs of the current term, their parties, and the sitting days.</summary>
internal sealed class MpDirectory(IDbContextFactory<ParliamentDb> contexts, TimeProvider clock) : IMpDirectory, IMpMailAddresses
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
                              p.Id,
                              p.SeimasPersonId,
                              p.Slug,
                              p.FirstName,
                              p.LastName,
                              m.NominatingParty,
                          })
            .ToListAsync(cancellationToken);

        // The one definition of an official address (design 8.5.1), also for the summaries' recipients (design 8.4).
        var people = rows.Select(r => r.Id).ToList();
        var contacts = await (from c in db.Contacts.AsNoTracking()
                              join p in db.People.AsNoTracking() on c.PersonId equals p.Id
                              where people.Contains(c.PersonId) && c.Kind == OfficialAddresses.EmailKind
                              select new { p.SeimasPersonId, c.Kind, c.Value })
            .ToListAsync(cancellationToken);
        var owners = OfficialAddresses.Owned(contacts.Select(c => (c.SeimasPersonId, c.Kind, c.Value))).Values.ToHashSet();
        return rows
            .OrderBy(r => r.LastName, StringComparer.Create(new System.Globalization.CultureInfo("lt-LT"), false))
            .Select(r => new MpRecipient(r.Slug, r.FirstName, r.LastName, r.NominatingParty, owners.Contains(r.SeimasPersonId), r.SeimasPersonId))
            .ToList();
    }

    /// <summary>Design 8.19.1: any stored response marked synthetic (the fact ParliamentMaintenance reads); real rows beside it do not matter.</summary>
    public async Task<bool> HoldsInventedDataAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.RawDocuments.AsNoTracking().AnyAsync(d => d.Synthetic, cancellationToken);
    }

    /// <summary>
    /// <see cref="IMpMailAddresses"/> (design 8.19.3): the official addresses of the given sitting MPs, by the one definition,
    /// for sending only. The address goes nowhere else from here.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, string>> OfficialAddressesForSendingAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seimasPersonIds);
        if (seimasPersonIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTerm.FindAsync(db, cancellationToken);
        if (term is null)
        {
            return new Dictionary<long, string>();
        }

        // Every sitting MP's email contacts (about 141 rows), so an address two of them list belongs to nobody.
        var sitting = db.Mandates.AsNoTracking().Where(CurrentTerm.SittingOn(VilniusTime.Today(clock))).Where(m => m.TermId == term.Id).Select(m => m.PersonId);
        var listed = await (from c in db.Contacts.AsNoTracking()
                            join p in db.People.AsNoTracking() on c.PersonId equals p.Id
                            where sitting.Contains(c.PersonId) && c.Kind == OfficialAddresses.EmailKind
                            select new { p.SeimasPersonId, c.Kind, c.Value })
            .ToListAsync(cancellationToken);
        var wanted = seimasPersonIds.ToHashSet();
        return OfficialAddresses.Owned(listed.Select(c => (c.SeimasPersonId, c.Kind, c.Value)))
            .Where(o => wanted.Contains(o.Value))
            .ToDictionary(o => o.Value, o => o.Key);
    }

    public async Task<MpFacts?> OfficialAddressOwnerAsync(string email, CancellationToken cancellationToken)
    {
        if (OfficialAddresses.Normalise(email) is not { } address)
        {
            return null;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = await CurrentTerm.FindAsync(db, cancellationToken);
        if (term is null)
        {
            return null;
        }

        // The email contacts of every sitting MP of the term (about 141 rows), compared in memory by the one definition: an
        // address two or more of them list belongs to nobody.
        var sitting = db.Mandates.AsNoTracking().Where(CurrentTerm.SittingOn(VilniusTime.Today(clock))).Where(m => m.TermId == term.Id).Select(m => m.PersonId);
        var listed = await (from c in db.Contacts.AsNoTracking()
                            join p in db.People.AsNoTracking() on c.PersonId equals p.Id
                            where sitting.Contains(c.PersonId) && c.Kind == OfficialAddresses.EmailKind
                            select new { p.SeimasPersonId, p.Slug, p.FirstName, p.LastName, c.Kind, c.Value })
            .ToListAsync(cancellationToken);
        var owner = OfficialAddresses.Owner(listed.Select(c => (c.SeimasPersonId, c.Kind, c.Value)), address);
        return owner is { } id && listed.First(c => c.SeimasPersonId == id) is var mp
            ? new MpFacts(mp.SeimasPersonId, mp.Slug, mp.FirstName, mp.LastName)
            : null;
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

    public async Task<IReadOnlyDictionary<long, string>> FactionsAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken)
    {
        var ids = seimasPersonIds.Distinct().ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var term = ids.Count == 0 ? null : await CurrentTerm.FindAsync(db, cancellationToken);
        if (term is null)
        {
            return new Dictionary<long, string>();
        }

        var today = VilniusTime.Today(clock);
        var people = await (from m in db.Mandates.AsNoTracking()
                            join p in db.People.AsNoTracking() on m.PersonId equals p.Id
                            where m.TermId == term.Id && ids.Contains(p.SeimasPersonId)
                            select new { p.Id, p.SeimasPersonId, Mandate = m }).ToListAsync(cancellationToken);
        var personIds = people.Select(p => p.Id).ToList();
        var factions = await (from m in db.Memberships.AsNoTracking()
                              join b in db.Bodies.AsNoTracking() on m.BodyId equals b.Id
                              where b.TermId == term.Id && b.Kind == Lt2.Parliament.Import.Parsing.BodyKind.Faction && personIds.Contains(m.PersonId)
                              select new { m.PersonId, m.From, m.To, b.Name }).ToListAsync(cancellationToken);
        var result = new Dictionary<long, string>();
        foreach (var person in people)
        {
            // As on the profiles: a sitting MP's faction is today's, or none (the mixed group); a former MP's, the last one.
            var sitting = CurrentTerm.IsSitting(person.Mandate, today);
            var faction = factions.Where(f => f.PersonId == person.Id && (!sitting || f.To is null || f.To >= today))
                .OrderByDescending(f => f.From).FirstOrDefault();
            if (faction is not null)
            {
                result[person.SeimasPersonId] = faction.Name;
            }
        }

        return result;
    }
}
