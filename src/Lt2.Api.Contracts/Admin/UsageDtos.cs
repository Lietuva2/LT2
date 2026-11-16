namespace Lt2.Api.Contracts.Admin;

/// <summary>
/// Design 8.27: "Lankomumas", the site's own usage statistics for administrators. Numbers only: how many visitors and
/// members, how many pages, which paths and which referring sites; never anything about a person.
/// </summary>
/// <param name="OnlineVisitors">Distinct visitors without a signed-in session with a page in the last five minutes.</param>
/// <param name="OnlineMembers">Distinct accounts with a page in the last five minutes.</param>
/// <param name="Days">Vilnius days from the first counted, at most the last 30, oldest first, today included.</param>
/// <param name="Months">Months from the first counted, at most the last 12, oldest first, this month included.</param>
/// <param name="Years">Every year from the first counted, oldest first, this year included.</param>
/// <param name="RangeDays">The days <see cref="Pages"/> and <see cref="Referrers"/> cover, today included.</param>
public sealed record UsageResponse(
    int OnlineVisitors,
    int OnlineMembers,
    IReadOnlyList<UsagePeriodResponse> Days,
    IReadOnlyList<UsagePeriodResponse> Months,
    IReadOnlyList<UsagePeriodResponse> Years,
    int RangeDays,
    IReadOnlyList<UsageItemResponse> Pages,
    IReadOnlyList<UsageItemResponse> Referrers);

/// <summary>A day, month or year by its first day. Visitors and members are approximate distinct counts; views are exact.</summary>
public sealed record UsagePeriodResponse(DateOnly Starts, int Visitors, int Members, int VisitorViews, int MemberViews, int CrawlerViews);

/// <summary>A path or a referring site's host, with its page views.</summary>
public sealed record UsageItemResponse(string Name, int Views);

/// <summary>The screen's addresses.</summary>
public static class UsageRoutes
{
    public const string Page = "/administravimas/lankomumas";

    /// <summary>The ranges for pages and referring sites; any other value reads as <see cref="DefaultRange"/>.</summary>
    public static readonly int[] Ranges = [1, 7, 30];

    public const int DefaultRange = 7;

    public static string Api(int days) => $"{ApiRoutes.Admin.Usage}?days={days}";
}

/// <summary>"Lankomumas" (design 8.27): administrators in a staff passkey session.</summary>
public interface IUsageApi
{
    Task<ApiResult<UsageResponse>> GetAsync(int days = UsageRoutes.DefaultRange, CancellationToken cancellationToken = default);
}
