using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Usage;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// <c>GET /api/v1/admin/usage?days=</c>: the site's own usage statistics (design 8.27), for an administrator in a
/// staff-passkey session. Writes what is counted so far, then answers with numbers only.
/// </summary>
internal static class UsageEndpoint
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Admin.Usage), async (int? days, IUsageReport report, CancellationToken cancellationToken) =>
                TypedResults.Ok(UsageMapping.Map(await report.ReportAsync(days ?? UsageRoutes.DefaultRange, cancellationToken))))
            .WithName("GetUsage")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .Produces<UsageResponse>();
    }
}
