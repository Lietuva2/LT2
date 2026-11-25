using System.Data.Common;
using Lt2.Parliament.Domain;
using Lt2.Parliament.Import.Agenda;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.Parliament.Infrastructure;

/// <summary><see cref="IPlannedAgenda"/>: the current planned date of bills, the moderator's row first.</summary>
internal sealed class PlannedAgenda(IDbContextFactory<ParliamentDb> contexts, TimeProvider clock) : IPlannedAgenda
{
    public async Task<IReadOnlyDictionary<string, PlannedDate>> ForBillsAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken)
    {
        var list = numbers.ToList();
        var today = Today();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.PlannedItems.AsNoTracking()
            .Where(p => list.Contains(p.BillNumber) && p.GoneAt == null && p.PlannedDate >= today)
            .ToListAsync(cancellationToken);
        return rows.GroupBy(p => p.BillNumber)
            .ToDictionary(g => g.Key, g => ToDate(Current(g)), StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<PlannedDate>> BetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        // The same order as ForBillsAsync: rows outside the window are left out first, then each bill's current row is
        // chosen, so a moderator's row whose date has passed does not hide a newer date from the agenda page.
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.PlannedItems.AsNoTracking()
            .Where(p => p.GoneAt == null && p.PlannedDate >= from && p.PlannedDate <= to)
            .ToListAsync(cancellationToken);
        return rows.GroupBy(p => p.BillNumber)
            .Select(g => ToDate(Current(g)))
            .OrderBy(p => p.Date).ThenBy(p => p.BillNumber, StringComparer.Ordinal)
            .ToList();
    }

    public async Task CorrectAsync(string number, DateOnly date, string? stage, Guid moderatorUserId, string note, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("A correction needs a PostgreSQL transaction.", nameof(transaction));
        }

        var now = clock.GetUtcNow();
        await using (var gone = new NpgsqlCommand(
            "UPDATE parliament.planned_item SET gone_at = @now WHERE bill_number = @bill AND source = 'moderator' AND gone_at IS NULL", connection, npgsql))
        {
            gone.Parameters.AddWithValue("now", now);
            gone.Parameters.AddWithValue("bill", number);
            await gone.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source_url, source, first_seen, last_seen, confirmed_by, note)
            VALUES (@id, @bill, @date, @stage, NULL, 'moderator', @now, @now, @by, @note)
            """,
            connection,
            npgsql);
        insert.Parameters.AddWithValue("id", Ids.New(clock));
        insert.Parameters.AddWithValue("bill", number);
        insert.Parameters.AddWithValue("date", date);
        insert.Parameters.AddWithValue("stage", (object?)stage ?? DBNull.Value);
        insert.Parameters.AddWithValue("now", now);
        insert.Parameters.AddWithValue("by", moderatorUserId);
        insert.Parameters.AddWithValue("note", note);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The moderator's row if there is one, otherwise the row seen most recently, the watcher's or an entered one (design 8.4:
    /// between them the one seen last wins).
    /// </summary>
    internal static PlannedItem Current(IEnumerable<PlannedItem> rows) =>
        rows.OrderByDescending(p => p.Source == PlannedSources.Moderator).ThenByDescending(p => p.LastSeen).ThenBy(p => p.PlannedDate).First();

    private static PlannedDate ToDate(PlannedItem p) => new(p.BillNumber, p.PlannedDate, p.Stage, p.SourceUrl, p.Source == PlannedSources.Moderator, p.Source == PlannedSources.Entry);

    private DateOnly Today() => VilniusTime.Today(clock);
}
