using Lt2.SharedKernel;
using Lt2.Usage.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Usage.Counting;

/// <summary>Reads the counts for the administrators' screen (design 8.27).</summary>
internal sealed class UsageReports(UsageDb db, IUsageCounter counter, TimeProvider clock) : IUsageReport
{
    public const int Days = 30;
    public const int Months = 12;
    public const int TopPages = 30;
    public const int TopReferrers = 20;

    /// <summary>The ranges of the pages and referring sites (1, 7 or 30 days); any other asks for the default.</summary>
    public static readonly int[] Ranges = [1, 7, 30];

    public const int DefaultRange = 7;

    public async Task<UsageReport> ReportAsync(int days, CancellationToken cancellationToken)
    {
        await counter.FlushAsync(cancellationToken);
        var today = VilniusTime.Today(clock);
        var range = Ranges.Contains(days) ? days : DefaultRange;

        // The lists begin at the first day counted (no rows of zeros from before counting began) and at most 30 days and
        // 12 months back; today, this month and this year are always there.
        var counted = await db.Totals.AsNoTracking().Where(t => t.Period == UsageRules.Day && t.Starts <= today)
            .Select(t => (DateOnly?)t.Starts).MinAsync(cancellationToken) ?? today;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstDay = Max(today.AddDays(-(Days - 1)), counted);
        var firstMonth = Max(thisMonth.AddMonths(-(Months - 1)), new DateOnly(counted.Year, counted.Month, 1));
        var firstYear = new DateOnly(counted.Year, 1, 1);
        var thisYear = new DateOnly(today.Year, 1, 1);
        var totals = await db.Totals.AsNoTracking()
            .Where(t => (t.Period == UsageRules.Day && t.Starts >= firstDay && t.Starts <= today)
                || (t.Period == UsageRules.Month && t.Starts >= firstMonth && t.Starts <= thisMonth)
                || (t.Period == UsageRules.Year && t.Starts >= firstYear && t.Starts <= thisYear))
            .ToListAsync(cancellationToken);

        var dayList = Enumerable.Range(0, today.DayNumber - firstDay.DayNumber + 1).Select(i => Of(totals, UsageRules.Day, firstDay.AddDays(i))).ToList();
        var monthList = Enumerable.Range(0, MonthsBetween(firstMonth, thisMonth) + 1).Select(i => Of(totals, UsageRules.Month, firstMonth.AddMonths(i))).ToList();
        var years = Enumerable.Range(firstYear.Year, today.Year - firstYear.Year + 1).Select(y => Of(totals, UsageRules.Year, new DateOnly(y, 1, 1))).ToList();

        var from = today.AddDays(-(range - 1));
        var pages = (await db.Pages.AsNoTracking()
                .Where(p => p.Day >= from)
                .GroupBy(p => p.Path)
                .Select(g => new { Name = g.Key, Views = g.Sum(p => p.Views) })
                .OrderByDescending(i => i.Views).ThenBy(i => i.Name)
                .Take(TopPages)
                .ToListAsync(cancellationToken))
            .Select(i => new UsageItem(i.Name, i.Views)).ToList();
        var referrers = (await db.Referrers.AsNoTracking()
                .Where(r => r.Day >= from)
                .GroupBy(r => r.Host)
                .Select(g => new { Name = g.Key, Views = g.Sum(r => r.Views) })
                .OrderByDescending(i => i.Views).ThenBy(i => i.Name)
                .Take(TopReferrers)
                .ToListAsync(cancellationToken))
            .Select(i => new UsageItem(i.Name, i.Views)).ToList();

        return new UsageReport(counter.Online(), dayList, monthList, years, range, pages, referrers);
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static int MonthsBetween(DateOnly from, DateOnly to) => (to.Year - from.Year) * 12 + to.Month - from.Month;

    private static UsageTotal Of(List<TotalRow> totals, string period, DateOnly starts) =>
        totals.FirstOrDefault(t => t.Period == period && t.Starts == starts) is { } t
            ? new UsageTotal(starts, t.Visitors, t.Members, t.VisitorViews, t.MemberViews, t.CrawlerViews)
            : new UsageTotal(starts, 0, 0, 0, 0, 0);
}
