using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary><c>GET /api/v1/moderation/appeals/{id}/initiator</c>: the appeal's initiator as a moderator sees them before restricting (design 8.3).</summary>
internal static class GetAppealInitiatorEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.AppealInitiator), HandleAsync)
            .WithName("GetAppealInitiator")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<AuthorViewResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid id, IAppealsModeration moderation, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await moderation.InitiatorOfAppealAsync(id, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AppealMapping.Map(result.Value!)) : errors.ToResult(result.Error!);
    }
}
