using System.Globalization;
using System.Text.Json;
using Lt2.Parliament.Domain;
using Lt2.Parliament.Import.Agenda;
using Lt2.Parliament.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Lt2.Parliament.Application;

/// <summary>
/// <see cref="IAgendaEntries"/> (design 8.4, "Entering a day's agenda"): the day, the document's address and the lines are
/// checked here (<see cref="AgendaEntryRules"/>), for every caller. A save takes the day's advisory lock, reads the day's
/// entered rows, applies the list in one transaction (only rows with source <see cref="PlannedSources.Entry"/> of that day),
/// creates the bills the site does not know, keeps the text as sent in <c>agenda_entry</c> and writes the audit entry in the
/// same transaction.
/// </summary>
internal sealed class AgendaEntries(IDbContextFactory<ParliamentDb> contexts, IAuditLog audit, TimeProvider clock) : IAgendaEntries
{
    public async Task<IReadOnlyList<EnteredDay>> DaysAsync(CancellationToken cancellationToken)
    {
        var today = VilniusTime.Today(clock);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var days = await db.PlannedItems.AsNoTracking()
            .Where(p => p.Source == PlannedSources.Entry && p.GoneAt == null && p.PlannedDate >= today)
            .GroupBy(p => p.PlannedDate)
            .Select(g => new { Day = g.Key, Bills = g.Count() })
            .ToListAsync(cancellationToken);
        var saves = await db.AgendaEntries.AsNoTracking()
            .Where(e => e.PlannedDate >= today)
            .OrderByDescending(e => e.EnteredAt)
            .Select(e => new { e.PlannedDate, e.EnteredAt, e.SourceUrl })
            .ToListAsync(cancellationToken);
        var last = saves.GroupBy(s => s.PlannedDate).ToDictionary(g => g.Key, g => g.First());
        return days
            .OrderBy(d => d.Day)
            .Select(d => last.TryGetValue(d.Day, out var save)
                ? new EnteredDay(d.Day, d.Bills, save.EnteredAt, save.SourceUrl)
                : new EnteredDay(d.Day, d.Bills, null, null))
            .ToList();
    }

    public async Task<Result<AgendaEntryOutcome>> PreviewAsync(DateOnly day, string sourceUrl, string lines, CancellationToken cancellationToken)
    {
        if (Check(day, sourceUrl, lines) is { } refused)
        {
            return refused;
        }

        var source = AgendaEntryRules.Source(sourceUrl)!;
        var parse = AgendaEntryRules.Parse(lines);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var current = await EnteredAsync(db, day, cancellationToken);
        if (parse.Refused.Count == 0 && parse.Lines.Count == 0 && current.Count > 0)
        {
            return Error.Validation(AgendaEntryProblems.Empty, "lines");
        }

        var diff = AgendaEntryRules.Diff(current.Select(r => new EnteredRow(r.BillNumber, r.Stage)).ToList(), parse.Lines);
        var known = await KnownAsync(db, parse.Lines, cancellationToken);
        return Outcome(day, source, saved: false, diff, known, parse.Refused);
    }

    public async Task<Result<AgendaEntryOutcome>> SaveAsync(DateOnly day, string sourceUrl, string lines, StaffActor staff, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staff);
        if (Check(day, sourceUrl, lines) is { } refused)
        {
            return refused;
        }

        var source = AgendaEntryRules.Source(sourceUrl)!;
        var parse = AgendaEntryRules.Parse(lines);
        if (parse.Refused.Count > 0)
        {
            return Error.Validation(AgendaEntryProblems.Lines, "lines");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Saves of one day take turns: each reads the day's rows only after the one before it has committed.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",
            [new NpgsqlParameter("key", "lt2.agenda-entry:" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))],
            cancellationToken);
        var current = await EnteredAsync(db, day, cancellationToken, tracked: true);
        if (parse.Lines.Count == 0 && current.Count > 0)
        {
            return Error.Validation(AgendaEntryProblems.Empty, "lines");
        }

        var diff = AgendaEntryRules.Diff(current.Select(r => new EnteredRow(r.BillNumber, r.Stage)).ToList(), parse.Lines);
        var known = await KnownAsync(db, parse.Lines, cancellationToken);
        var now = clock.GetUtcNow();
        var rowOf = current.GroupBy(r => r.BillNumber, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var change in diff.Bills)
        {
            var line = change.Line;
            if (change.Change == AgendaEntryChanges.Unchanged)
            {
                var row = rowOf[line.Number];
                row.LastSeen = now;
                row.SourceUrl = source;
                row.ConfirmedBy = staff.UserId;
                continue;
            }

            if (change.Change == AgendaEntryChanges.StageChanged)
            {
                rowOf[line.Number].GoneAt = now;
            }

            db.PlannedItems.Add(new PlannedItem
            {
                Id = Ids.New(clock),
                BillNumber = line.Number,
                PlannedDate = day,
                Stage = line.Stage,
                SourceUrl = source,
                Source = PlannedSources.Entry,
                FirstSeen = now,
                LastSeen = now,
                ConfirmedBy = staff.UserId,
            });
        }

        foreach (var number in diff.Removed)
        {
            foreach (var row in current.Where(r => r.BillNumber == number))
            {
                row.GoneAt = now;
            }
        }

        // A bill the site does not know is created from its line, with no e-seimas link; a bill the import made meanwhile stands.
        var created = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in parse.Lines.Where(l => !known.Contains(l.Number)))
        {
            var id = Ids.New(clock);
            var inserted = await db.Database.ExecuteSqlAsync(
                $"INSERT INTO parliament.bill (id, number, title, e_seimas_url) VALUES ({id}, {line.Number}, {line.Title}, NULL) ON CONFLICT (number) DO NOTHING",
                cancellationToken);
            if (inserted == 1)
            {
                created.Add(line.Number);
            }
        }

        db.AgendaEntries.Add(new AgendaEntry
        {
            Id = Ids.New(clock),
            PlannedDate = day,
            SourceUrl = source,
            Body = lines,
            EnteredAt = now,
            EnteredBy = staff.UserId,
            Rows = parse.Lines.Count,
            Added = diff.Count(AgendaEntryChanges.Added),
            Changed = diff.Count(AgendaEntryChanges.StageChanged),
            Removed = diff.Removed.Count,
            CreatedBills = created.Count,
        });
        await db.SaveChangesAsync(cancellationToken);

        var outcome = Outcome(day, source, saved: true, diff, known, []);
        outcome = outcome with { Bills = outcome.Bills.Select(b => b with { Created = created.Contains(b.Number) }).ToList() };

        // The day, the document and the counts; nothing about any person.
        await audit.WriteAsync(
            new AuditEntry("agenda.entered", staff.UserId, staff.Role, "agenda:" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                JsonSerializer.SerializeToElement(
                    new
                    {
                        day,
                        sourceUrl = source,
                        rows = parse.Lines.Count,
                        added = outcome.Count(AgendaEntryChanges.Added),
                        stageChanged = outcome.Count(AgendaEntryChanges.StageChanged),
                        unchanged = outcome.Count(AgendaEntryChanges.Unchanged),
                        removed = diff.Removed.Count,
                        createdBills = created.Count,
                    },
                    JsonSerializerOptions.Web),
                staff.IpHash),
            transaction.GetDbTransaction(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    /// <summary>The checks of the day, the address and the text's length, before anything is read.</summary>
    private Error? Check(DateOnly day, string? sourceUrl, string? lines)
    {
        if (!AgendaEntryRules.DayAccepted(day, VilniusTime.Today(clock)))
        {
            return Error.Validation(AgendaEntryProblems.Day, "day");
        }

        if (AgendaEntryRules.Source(sourceUrl) is null)
        {
            return Error.Validation(AgendaEntryProblems.Source, "sourceUrl");
        }

        return (lines ?? "").Length > AgendaEntryRules.TextMaxLength ? Error.Validation(AgendaEntryProblems.TooLong, "lines") : null;
    }

    /// <summary>The day's entered rows in force.</summary>
    private static Task<List<PlannedItem>> EnteredAsync(ParliamentDb db, DateOnly day, CancellationToken cancellationToken, bool tracked = false)
    {
        var rows = db.PlannedItems.Where(p => p.Source == PlannedSources.Entry && p.PlannedDate == day && p.GoneAt == null);
        return (tracked ? rows : rows.AsNoTracking()).ToListAsync(cancellationToken);
    }

    /// <summary>The listed numbers the site knows.</summary>
    private static async Task<HashSet<string>> KnownAsync(ParliamentDb db, IReadOnlyList<EntryLine> lines, CancellationToken cancellationToken)
    {
        var numbers = lines.Select(l => l.Number).ToList();
        var known = await db.Bills.AsNoTracking().Where(b => numbers.Contains(b.Number)).Select(b => b.Number).ToListAsync(cancellationToken);
        return known.ToHashSet(StringComparer.Ordinal);
    }

    private static AgendaEntryOutcome Outcome(
        DateOnly day, string source, bool saved, EntryDiff diff, HashSet<string> known, IReadOnlyList<EntryRefusal> refused) =>
        new(
            day,
            source,
            saved,
            diff.Bills.Select(b => new EnteredBill(b.Line.Line, b.Line.Number, b.Line.Stage, b.Line.Title, !known.Contains(b.Line.Number), b.Change)).ToList(),
            diff.Removed,
            refused.Select(r => new EnteredLineRefusal(r.Line, r.Reason)).ToList());
}
