using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>PUT /api/v1/reasons/{id}/endorsement</c>: "Man svarbu" on an argument of one's own side, at most three per bill.</summary>
internal static class EndorseEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.Endorsement), HandleAsync)
            .WithName("Endorse")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        [Microsoft.AspNetCore.Mvc.FromRoute(Name = "id")] Guid reasonId, ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = AppealMapping.UserId(user)!.Value;
        var result = await appeals.EndorseAsync(reasonId, id, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
