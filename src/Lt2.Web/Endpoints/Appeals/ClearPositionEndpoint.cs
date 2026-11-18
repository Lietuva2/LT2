using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>DELETE /api/v1/bills/{number}/record/position</c>: withdraw one's side on this bill (design 8.7, owner 2026-11-18:
/// pressing the pressed button again); one's "Man svarbu" marks stay and stop counting. An optional body
/// (<see cref="PositionRequest"/>) names the side being withdrawn: when the caller no longer has that side, nothing changes
/// (a late second press, another tab). The side travels in the body, never in the address, which logs keep. It also marks
/// the front page's agenda out of date (<see cref="TakingSides"/>).
/// </summary>
internal static class ClearPositionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete(ApiEndpoints.Relative(AppealRoutes.Position), HandleAsync)
            .WithName("ClearPosition")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.Participation)
            .Accepts<PositionRequest>(isOptional: true, "application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        string number, [FromBody] PositionRequest? request, ClaimsPrincipal user,
        TakingSides sides,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = user.UserId()!.Value;
        var result = await sides.ClearAsync(number, id, request?.Side, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
