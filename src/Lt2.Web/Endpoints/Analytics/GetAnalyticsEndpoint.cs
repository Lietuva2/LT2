using System.Globalization;
using Lt2.Api.Contracts.Analytics;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Analytics;

/// <summary>
/// <c>GET /api/v1/analytics</c> (design 8.26.6): the public analytics, the same for everyone, cached with an ETag. A
/// <c>year</c> outside the term, or not a number, is the whole term; <c>accounts=older</c> counts only accounts older
/// than 30 days, anything else every account.
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

    private static async Task<IResult> HandleAsync(HttpContext http, AnalyticsReads reads, ErrorMapper errors, string? year, string? accounts, CancellationToken ct)
    {
        int? asked = int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : null;
        var older = string.Equals(accounts, AnalyticsRoutes.OlderAccounts, StringComparison.Ordinal);
        return PublicJson.From(http, errors, Result<AnalyticsResponse>.Success(await reads.ReadAsync(asked, older, ct)), maxAgeSeconds: 0);
    }
}
