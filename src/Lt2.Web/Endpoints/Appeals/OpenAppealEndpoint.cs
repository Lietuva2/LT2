using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>POST /api/v1/bills/{number}/appeal</c>: open the appeal on a bill with a side, the first argument, a neutral
/// description and a threshold (design 8.1). It waits for a moderator; <c>appeal.exists</c> when the bill has one.
/// </summary>
internal static class OpenAppealEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.Appeal), HandleAsync)
            .WithName("OpenAppeal")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<OpenAppealRequest>()
            .RequireRateLimiting(RateLimitPolicies.OpenAppeal)
            .Produces<AppealResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(string number, OpenAppealRequest request, ClaimsPrincipal user, IAppealsModule appeals, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await appeals.OpenAsync(number, AppealMapping.UserId(user)!.Value, AppealMapping.ToModule(request), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created(AppealRoutes.AppealOf(result.Value.Bill.Number), AppealMapping.Map(result.Value))
            : errors.ToResult(result.Error!);
    }
}
