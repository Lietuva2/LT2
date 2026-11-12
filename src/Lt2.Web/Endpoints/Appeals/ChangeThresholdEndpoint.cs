using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>PUT /api/v1/bills/{number}/appeal/threshold</c>: the initiator changes the threshold, until the appeal first enters a summary or a moderator changed it (design 8.4).</summary>
internal static class ChangeThresholdEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.Threshold), HandleAsync)
            .WithName("ChangeThreshold")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<ThresholdRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        string number, ThresholdRequest request,
        ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = AppealMapping.UserId(user)!.Value;
        var result = await appeals.ChangeThresholdAsync(number, id, request.Threshold, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
