using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Audit;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary><c>GET /api/v1/admin/settings</c>: every runtime setting with its current value (design 5.6).</summary>
internal static class GetSettingsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Admin.Settings), HandleAsync)
            .WithName("GetSettings")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .Produces<IReadOnlyList<SettingResponse>>();

    private static async Task<Ok<List<SettingResponse>>> HandleAsync(ISettingsAdministration settings, CancellationToken cancellationToken)
    {
        var values = await settings.ListAsync(cancellationToken);
        return TypedResults.Ok(values.Select(SettingMapping.ToResponse).ToList());
    }
}
