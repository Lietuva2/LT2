using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>PUT /api/v1/bills/{number}/appeal/position</c>: take or change a side. The row is overwritten; no earlier side is kept (design 5.3).</summary>
internal static class TakePositionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.Position), HandleAsync)
            .WithName("TakePosition")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<PositionRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        string number, PositionRequest request,
        ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = user.UserId()!.Value;
        var result = await appeals.TakePositionAsync(number, id, request.Side, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}