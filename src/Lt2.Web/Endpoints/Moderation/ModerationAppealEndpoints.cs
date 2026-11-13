using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// A moderator's controls on the appeal page (design 8.3): <c>GET /api/v1/moderation/appeals/{id}</c> is the state that
/// decides which actions apply; <c>PUT …/description</c> corrects an open appeal's description directly (design 8.1), with
/// a required statement that is audited and emailed to the initiator.
/// </summary>
internal static class ModerationAppealEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.ModerationAppeal), GetAsync)
            .WithName("GetModerationAppeal")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<ModerationAppealResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut(ApiEndpoints.Relative(AppealRoutes.AppealDescription), CorrectAsync)
            .WithName("CorrectAppealDescription")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<ModeratorDescriptionRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetAsync(Guid id, IAppealsModeration moderation, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await moderation.AppealStateAsync(id, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AppealMapping.Map(result.Value!)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> CorrectAsync(
        Guid id, ModeratorDescriptionRequest request, ClaimsPrincipal user, HttpContext context, Lt2.Identity.IIdentityModule identity,
        IAppealsModeration moderation, IpHasher ipHasher, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await moderation.CorrectDescriptionAsync(id, request.Description, request.Statement, moderator, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
