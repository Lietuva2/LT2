using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// The daily aggregate (design 5.3 <c>daily_stat</c>): per record and Vilnius day, the counts per side and for accounts
/// older than 30 days at the end of the day, and the share of that day's new positions taken by accounts younger than
/// 30 days. The only source of trends: no per-person history exists. Today's row is rewritten on every run; a finished
/// day's row is written once after the day ends (<see cref="DailyStatRules.ShouldWrite"/>).
/// </summary>
internal sealed class AppealsMaintenance(
    IDbContextFactory<AppealsDb> contexts, AppealReads reads, IBillCatalogue bills, IAuditLog audit, TimeProvider clock) : IAppealsMaintenance
{
    public async Task<int> AggregateDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var start = VilniusTime.StartOf(day);
        var end = VilniusTime.StartOf(day.AddDays(1));
        var now = clock.GetUtcNow();
        var asOf = end < now ? end : now;
        var established = CountRules.EstablishedBefore(asOf);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeals = await db.BillRecords.AsNoTracking()
            .Where(a => (a.Status == BillRecordStatuses.Open || a.Status == BillRecordStatuses.Closed)
                && a.PublishedAt != null && a.PublishedAt < end && (a.ClosedAt == null || a.ClosedAt >= start))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        var computed = await db.DailyStats.AsNoTracking()
            .Where(d => d.Day == day && appeals.Contains(d.RecordId))
            .ToDictionaryAsync(d => d.RecordId, d => d.ComputedAt, cancellationToken);
        var due = appeals.Where(id => DailyStatRules.ShouldWrite(end, now, computed.GetValueOrDefault(id))).ToList();
        if (due.Count == 0)
        {
            return 0;
        }

        // One query for every record due, not one per record.
        var rows = await db.Positions.AsNoTracking()
            .Where(p => due.Contains(p.RecordId) && p.CreatedAt < asOf)
            .GroupBy(p => new { p.RecordId, p.Side })
            .Select(g => new
            {
                g.Key.RecordId,
                g.Key.Side,
                Total = g.Count(),
                Older = g.Count(p => p.MemberSince <= established),
                New = g.Count(p => p.CreatedAt >= start),
                NewYoung = g.Count(p => p.CreatedAt >= start && p.MemberSince > established),
            })
            .ToListAsync(cancellationToken);
        foreach (var recordId in due)
        {
            var forRow = rows.FirstOrDefault(r => r.RecordId == recordId && r.Side == Sides.For);
            var againstRow = rows.FirstOrDefault(r => r.RecordId == recordId && r.Side == Sides.Against);
            var mine = rows.Where(r => r.RecordId == recordId).ToList();
            var share = SurgeRules.NewAccountShare(mine.Sum(r => r.New), mine.Sum(r => r.NewYoung));
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO appeals.daily_stat (record_id, day, for_count, against_count, for_older_than30days, against_older_than30days, new_account_share, computed_at)
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})
                ON CONFLICT (record_id, day) DO UPDATE SET
                  for_count = EXCLUDED.for_count, against_count = EXCLUDED.against_count,
                  for_older_than30days = EXCLUDED.for_older_than30days, against_older_than30days = EXCLUDED.against_older_than30days,
                  new_account_share = EXCLUDED.new_account_share, computed_at = EXCLUDED.computed_at
                """,
                [recordId, day, forRow?.Total ?? 0, againstRow?.Total ?? 0, forRow?.Older ?? 0, againstRow?.Older ?? 0, share, now],
                cancellationToken);
        }

        return due.Count;
    }

    public async Task<int> CloseFinishedAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var open = await db.BillRecords.AsNoTracking().Where(a => a.Status == BillRecordStatuses.Open).Select(a => new { a.Id, a.BillNumber }).ToListAsync(cancellationToken);
        var closed = 0;
        foreach (var candidate in open)
        {
            var final = await bills.FinalVoteAsync(candidate.BillNumber, cancellationToken);
            if (!ClosingRules.ShouldClose(BillRecordStatuses.Open, final?.VoteId))
            {
                continue;
            }

            // Under the bill's lock, as every write to a record (design 6.8).
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await AdvisoryLocks.BillAsync(db, candidate.BillNumber, cancellationToken);
            var record = await db.BillRecords.FirstAsync(a => a.Id == candidate.Id, cancellationToken);
            if (record.Status != BillRecordStatuses.Open)
            {
                continue;
            }

            // Frozen now: once the status is closed no side can be taken or changed, and the page shows these numbers.
            var counts = (await reads.CountsAsync(db, [record.Id], cancellationToken))[record.Id];
            var now = clock.GetUtcNow();
            record.Status = BillRecordStatuses.Closed;
            record.ClosedAt = now;
            record.ChangedAt = now;
            record.ClosingVoteId = final!.VoteId;
            record.ClosedFor = counts.For;
            record.ClosedForOlderThan30Days = counts.ForOlderThan30Days;
            record.ClosedAgainst = counts.Against;
            record.ClosedAgainstOlderThan30Days = counts.AgainstOlderThan30Days;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(
                new AuditEntry("record.closed", null, "system", "record:" + record.Id,
                    System.Text.Json.JsonSerializer.SerializeToElement(new { bill = record.BillNumber, vote = final.VoteId }, System.Text.Json.JsonSerializerOptions.Web)),
                Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(transaction),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            closed++;
        }

        return closed;
    }
}
