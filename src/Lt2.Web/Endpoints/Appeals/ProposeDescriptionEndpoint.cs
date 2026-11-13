using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>POST /api/v1/bills/{number}/appeal/description-change</c>: the initiator of an open appeal proposes a new neutral
/// description (design 8.1). The page keeps the approved text until a moderator approves this one; one pending at a time.
/// </summary>
internal static class ProposeDescriptionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.DescriptionChange), HandleAsync)
            .WithName("ProposeDescription")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<EditAppealRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        string number, EditAppealRequest request, ClaimsPrincipal user, IAppealsModule appeals, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await appeals.ProposeDescriptionAsync(number, AppealMapping.UserId(user)!.Value, AppealMapping.ToModule(request), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
