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

    public async Task<UsageReport> ReportAsync(int days, CancellationToken cancellationToken)
    {
        await counter.FlushAsync(cancellationToken);
        var today = VilniusTime.Today(clock);
        var range = Math.Clamp(days, 1, 90);

        var firstDay = today.AddDays(-(Days - 1));
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstMonth = thisMonth.AddMonths(-(Months - 1));
        var totals = await db.Totals.AsNoTracking()
            .Where(t => (t.Period == UsageRules.Day && t.Starts >= firstDay)
                || (t.Period == UsageRules.Month && t.Starts >= firstMonth)
                || t.Period == UsageRules.Year)
            .ToListAsync(cancellationToken);

        var dayList = Enumerable.Range(0, Days).Select(i => Of(totals, UsageRules.Day, firstDay.AddDays(i))).ToList();
        var monthList = Enumerable.Range(0, Months).Select(i => Of(totals, UsageRules.Month, firstMonth.AddMonths(i))).ToList();
        var years = totals.Where(t => t.Period == UsageRules.Year).Select(t => t.Starts)
            .Append(new DateOnly(today.Year, 1, 1)).Distinct().Order()
            .Select(y => Of(totals, UsageRules.Year, y)).ToList();

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

    private static UsageTotal Of(List<TotalRow> totals, string period, DateOnly starts) =>
        totals.FirstOrDefault(t => t.Period == period && t.Starts == starts) is { } t
            ? new UsageTotal(starts, t.Visitors, t.Members, t.VisitorViews, t.MemberViews, t.CrawlerViews)
            : new UsageTotal(starts, 0, 0, 0, 0, 0);
}
