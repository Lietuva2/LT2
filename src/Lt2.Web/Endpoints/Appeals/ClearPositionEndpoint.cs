using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>DELETE /api/v1/bills/{number}/appeal/position</c>: withdraw one's side on this bill, with one's "Man svarbu" marks on it.</summary>
internal static class ClearPositionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete(ApiEndpoints.Relative(AppealRoutes.Position), HandleAsync)
            .WithName("ClearPosition")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        string number, ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = user.UserId()!.Value;
        var result = await appeals.ClearPositionAsync(number, id, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}