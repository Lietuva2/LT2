using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>POST /api/v1/bills/{number}/appeal/reasons</c>: a new argument on one's own side, at most two per bill. Published at
/// once unless pre-screening flags it for a moderator.
/// </summary>
internal static class WriteReasonEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.Reasons), HandleAsync)
            .WithName("WriteReason")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<ReasonRequest>()
            .RequireRateLimiting(RateLimitPolicies.WriteReason)
            .Produces<ReasonCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(string number, ReasonRequest request, ClaimsPrincipal user, IAppealsModule appeals, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await appeals.WriteReasonAsync(number, AppealMapping.UserId(user)!.Value, AppealMapping.ToModule(request), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created(AppealRoutes.AppealOf(number), new ReasonCreatedResponse(result.Value.Id, result.Value.Status))
            : errors.ToResult(result.Error!);
    }
}
