using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>PUT /api/v1/moderation/appeals/{id}/planned</c>: a moderator corrects the planned date or stage of the appeal's bill
/// (design 8.4); it takes precedence over the agenda pages and closes the open date flags. Audited.
/// </summary>
internal static class SetPlannedEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.AppealPlanned), HandleAsync)
            .WithName("SetPlanned")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<ModeratorPlannedRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id,
        ModeratorPlannedRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        Lt2.Identity.IIdentityModule identity,
        IAppealsModeration moderation,
        IpHasher ipHasher,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await moderation.SetPlannedAsync(id, request.Date, request.Stage, request.Statement, moderator, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}