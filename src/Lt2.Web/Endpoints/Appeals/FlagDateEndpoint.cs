using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>POST /api/v1/bills/{number}/appeal/flags</c>: "the date is wrong" or "this is being voted sooner", with a link to
/// the source (design 8.4). Only a participant (a member with a side on the open bill) who may write; one open flag per
/// person and bill, a second changes nothing. A moderator corrects the date.
/// </summary>
internal static class FlagDateEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.Flags), HandleAsync)
            .WithName("FlagDate")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .WithValidation<FlagRequest>()
            .RequireRateLimiting(RateLimitPolicies.Report)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(string number, FlagRequest request, ClaimsPrincipal user, IAppealsModule appeals, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await appeals.FlagDateAsync(number, user.UserId()!.Value, new NewFlag(request.Kind, request.Link, request.Note), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}