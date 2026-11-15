using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>DELETE /api/v1/reasons/{id}</c>: the author withdraws their own argument while the record is open (design 8.2). It
/// goes with its "Man svarbu" marks; nobody else can withdraw it (403); on a record that is not open, 409.
/// </summary>
internal static class WithdrawReasonEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete(ApiEndpoints.Relative(AppealRoutes.Reason), HandleAsync)
            .WithName("WithdrawReason")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        [Microsoft.AspNetCore.Mvc.FromRoute(Name = "id")] Guid reasonId, ClaimsPrincipal user,
        IAppealsModule appeals,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var id = user.UserId()!.Value;
        var result = await appeals.WithdrawReasonAsync(reasonId, id, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}