using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/bills/{number}/appeal</c>: the appeal with both sides' counts, the threshold and the published
/// arguments (design 8.8). A published appeal is public and the same for everyone; one waiting for a moderator is shown
/// only to its initiator, and never cached.
/// </summary>
internal static class GetAppealEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Appeal), HandleAsync)
            .WithName("GetAppeal")
            .WithTags("Appeals")
            .AllowAnonymous()
            .Produces<AppealResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ClaimsPrincipal user, IAppealsModule appeals, ErrorMapper errors, string number, CancellationToken cancellationToken)
    {
        var result = await appeals.GetAsync(number, AppealMapping.UserId(user), cancellationToken);
        if (!result.IsSuccess)
        {
            return errors.ToResult(result.Error!);
        }

        var response = AppealMapping.Map(result.Value);
        return result.Value.Status is "open" or "closed"
            ? PublicJson.From(http, errors, Lt2.SharedKernel.Result<AppealResponse>.Success(response), maxAgeSeconds: 0)
            : TypedResults.Ok(response);
    }
}
