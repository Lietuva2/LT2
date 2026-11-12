using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary><c>GET /api/v1/moderation/queue</c>: what waits for a moderator (design 8.3). Holds nobody's position.</summary>
internal static class GetModerationQueueEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.ModerationQueue), HandleAsync)
            .WithName("GetModerationQueue")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<ModerationQueueResponse>();

    private static async Task<IResult> HandleAsync(IAppealsModeration moderation, CancellationToken cancellationToken) =>
        TypedResults.Ok(AppealMapping.Map(await moderation.QueueAsync(cancellationToken)));
}
