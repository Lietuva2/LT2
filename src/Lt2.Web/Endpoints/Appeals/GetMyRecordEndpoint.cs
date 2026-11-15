using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/bills/{number}/record/mine</c>: the caller's own side, "Man svarbu" marks and arguments on this bill.
/// The only answer that holds a side, and only the caller's; never cached.
/// </summary>
internal static class GetMyRecordEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Mine), HandleAsync)
            .WithName("GetMyRecord")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .Produces<MyBillRecordResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(string number, ClaimsPrincipal user, IAppealsModule appeals, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await appeals.MineAsync(number, user.UserId()!.Value, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AppealMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }
}