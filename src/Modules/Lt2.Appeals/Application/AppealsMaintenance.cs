using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// The daily aggregate (design 5.3 <c>daily_stat</c>): per appeal and Vilnius day, the counts per side and for accounts
/// older than 30 days at the end of the day, and the share of that day's new positions taken by accounts younger than
/// 30 days. The only source of trends: no per-person history exists. Re-running a day overwrites its row.
/// </summary>
internal sealed class AppealsMaintenance(IDbContextFactory<AppealsDb> contexts, TimeProvider clock) : IAppealsMaintenance
{
    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    public async Task<int> AggregateDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Vilnius.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        var next = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var end = new DateTimeOffset(next, Vilnius.GetUtcOffset(next)).ToUniversalTime();
        var now = clock.GetUtcNow();
        var asOf = end < now ? end : now;
        var established = CountRules.EstablishedBefore(asOf);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var appeals = await db.Appeals.AsNoTracking()
            .Where(a => (a.Status == AppealStatuses.Open || a.Status == AppealStatuses.Closed)
                && a.PublishedAt != null && a.PublishedAt < end && (a.ClosedAt == null || a.ClosedAt >= start))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        foreach (var appealId in appeals)
        {
            var rows = await db.Positions.AsNoTracking()
                .Where(p => p.AppealId == appealId && p.CreatedAt < asOf)
                .GroupBy(p => p.Side)
                .Select(g => new
                {
                    Side = g.Key,
                    Total = g.Count(),
                    Older = g.Count(p => p.MemberSince <= established),
                    New = g.Count(p => p.CreatedAt >= start),
                    NewYoung = g.Count(p => p.CreatedAt >= start && p.MemberSince > established),
                })
                .ToListAsync(cancellationToken);
            var forRow = rows.FirstOrDefault(r => r.Side == Sides.For);
            var againstRow = rows.FirstOrDefault(r => r.Side == Sides.Against);
            var share = SurgeRules.NewAccountShare(rows.Sum(r => r.New), rows.Sum(r => r.NewYoung));
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO appeals.daily_stat (appeal_id, day, for_count, against_count, for_older_than30days, against_older_than30days, new_account_share)
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})
                ON CONFLICT (appeal_id, day) DO UPDATE SET
                  for_count = EXCLUDED.for_count, against_count = EXCLUDED.against_count,
                  for_older_than30days = EXCLUDED.for_older_than30days, against_older_than30days = EXCLUDED.against_older_than30days,
                  new_account_share = EXCLUDED.new_account_share
                """,
                [appealId, day, forRow?.Total ?? 0, againstRow?.Total ?? 0, forRow?.Older ?? 0, againstRow?.Older ?? 0, share],
                cancellationToken);
        }

        return appeals.Count;
    }

    public static DateOnly VilniusToday(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Vilnius).DateTime);
}
