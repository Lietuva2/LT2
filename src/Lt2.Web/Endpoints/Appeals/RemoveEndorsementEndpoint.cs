using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>DELETE /api/v1/reasons/{id}/endorsement</c>: take back one's "Man svarbu".</summary>
internal static class RemoveEndorsementEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete(ApiEndpoints.Relative(AppealRoutes.Endorsement), HandleAsync)
            .WithName("RemoveEndorsement")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        [Microsoft.AspNetCore.Mvc.FromRoute(Name = "id")] Guid reasonId, ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = user.UserId()!.Value;
        var result = await appeals.RemoveEndorsementAsync(reasonId, id, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}