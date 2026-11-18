using System.Globalization;
using Lt2.Api.Contracts.Analytics;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Analytics;

/// <summary>
/// <c>GET /api/v1/analytics</c> (design 8.26.6): the public analytics, the same for everyone, cached with an ETag. A
/// <c>year</c> outside the term, or not a number, is the whole term; <c>accounts=older</c> counts only accounts older
/// than 30 days, anything else every account. The list of closed bills takes its filters, order and length
/// (<c>from</c>, <c>to</c>, <c>q</c>, <c>result</c>, <c>majority</c>, <c>sort</c>, <c>limit</c>; <see cref="AnalyticsBillQuery"/>),
/// which never fail: a value the API would not make is the default.
/// </summary>
internal static class GetAnalyticsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AnalyticsRoutes.Analytics), HandleAsync)
            .WithName("GetAnalytics")
            .WithTags("Analytics")
            .AllowAnonymous()
            .Produces<AnalyticsResponse>()
            .Produces(StatusCodes.Status304NotModified);

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        AnalyticsReads reads,
        ErrorMapper errors,
        string? year,
        string? accounts,
        string? from,
        string? to,
        string? q,
        string? result,
        string? majority,
        string? sort,
        string? limit,
        CancellationToken ct)
    {
        int? asked = int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : null;
        var older = string.Equals(accounts, AnalyticsRoutes.OlderAccounts, StringComparison.Ordinal);
        var bills = AnalyticsBillQuery.Read(from, to, q, result, majority, sort, limit);
        return PublicJson.From(http, errors, Result<AnalyticsResponse>.Success(await reads.ReadAsync(asked, older, bills, ct)), maxAgeSeconds: 0);
    }
}
