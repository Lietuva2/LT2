using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>GET /api/v1/moderation/undescribed</c>: "Projektai be aprašymo", active bills people pay attention to that still want a
/// description, the most attention first (design 8.3). Only the counts the bills' pages show; nobody's position.
/// </summary>
internal static class GetUndescribedEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Undescribed), HandleAsync)
            .WithName("GetUndescribed")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<UndescribedBillsResponse>();

    private static async Task<IResult> HandleAsync(IAppealsModeration moderation, CancellationToken cancellationToken) =>
        TypedResults.Ok(AppealMapping.Map(await moderation.UndescribedAsync(cancellationToken)));
}
