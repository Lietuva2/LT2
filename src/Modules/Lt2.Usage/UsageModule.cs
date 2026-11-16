using System.Net;
using Lt2.SharedKernel;
using Lt2.SharedKernel.Persistence;
using Lt2.Usage.Counting;
using Lt2.Usage.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.Usage;

/// <summary>
/// Registration of the Usage module (design 8.27): the site's own usage statistics, counted on the server in aggregate. No
/// cookie, no script, nothing sent anywhere; a visitor is a keyed hash of the network and the user agent with a salt of the
/// period, kept only while the period lasts.
/// </summary>
public static class UsageModule
{
    public const string Schema = "usage";

    /// <summary>The tables and the report: for every host that reads them (the migrator, the web host).</summary>
    public static IServiceCollection AddUsageModule(this IServiceCollection services)
    {
        services.AddDbContext<UsageDb>((sp, options) =>
            options.UseLt2Npgsql(sp.GetRequiredService<NpgsqlDataSource>(), Schema));
        services.AddSingleton<IModuleDatabase, UsageModuleDatabase>();
        services.AddScoped<IUsageReport, UsageReports>();
        return services;
    }

    /// <summary>The counting itself, in the web host only: the counter in memory and its writing to the database every minute.</summary>
    public static IServiceCollection AddUsageCounting(this IServiceCollection services)
    {
        services.AddSingleton<UsageCounter>();
        services.AddSingleton<IUsageCounter>(sp => sp.GetRequiredService<UsageCounter>());
        services.AddHostedService<UsageFlushService>();
        return services;
    }
}

/// <summary>
/// One finished request as the web host saw it. The module decides whether it is a page view (<see cref="IUsageCounter.Count"/>);
/// the address and the user agent are hashed at once and not kept.
/// </summary>
/// <param name="Path">The path asked for, only to tell a page from the API and the like; it is never stored.</param>
/// <param name="Template">The route of the page that answered (<c>/seimas/{Number}</c>), null without one; a page is counted under it.</param>
/// <param name="RouteValues">The route's values; only a public object's (a bill's number, an address) are kept in the counted path.</param>
/// <param name="AccountId">The account of a signed-in session, else null.</param>
/// <param name="Referrer">The <c>Referer</c> header as sent; only its host is counted.</param>
/// <param name="OwnHost">This site's host, so moving between its own pages is no referral.</param>
public sealed record PageView(
    string Method,
    string Path,
    string? Template,
    IReadOnlyDictionary<string, string?> RouteValues,
    int Status,
    string? ContentType,
    bool Prefetch,
    IPAddress? Address,
    string? UserAgent,
    Guid? AccountId,
    string? Referrer,
    string? OwnHost);

/// <summary>Counts page views in memory and writes the counts every minute (design 8.27).</summary>
public interface IUsageCounter
{
    /// <summary>Counts the request if it is a page view; cheap, never touches the database.</summary>
    void Count(PageView view);

    /// <summary>Distinct visitors and accounts with a page in the last five minutes.</summary>
    UsageOnline Online();

    /// <summary>Writes what was counted since the last time and finishes the periods that have ended.</summary>
    Task FlushAsync(CancellationToken cancellationToken);
}

public sealed record UsageOnline(int Visitors, int Members);

/// <summary>The administrators' screen "Lankomumas" (design 8.27).</summary>
public interface IUsageReport
{
    /// <summary>Writes what is counted so far, then reads: 30 days, 12 months, the years, and the pages and referring sites of the last <paramref name="days"/> days.</summary>
    Task<UsageReport> ReportAsync(int days, CancellationToken cancellationToken);
}

public sealed record UsageReport(
    UsageOnline Online,
    IReadOnlyList<UsageTotal> Days,
    IReadOnlyList<UsageTotal> Months,
    IReadOnlyList<UsageTotal> Years,
    int RangeDays,
    IReadOnlyList<UsageItem> Pages,
    IReadOnlyList<UsageItem> Referrers);

/// <summary>One day, month or year (by its first day). Visitors and members are approximate distinct counts.</summary>
public sealed record UsageTotal(DateOnly Starts, int Visitors, int Members, int VisitorViews, int MemberViews, int CrawlerViews);

/// <summary>A path or a referring site with its page views.</summary>
public sealed record UsageItem(string Name, int Views);
