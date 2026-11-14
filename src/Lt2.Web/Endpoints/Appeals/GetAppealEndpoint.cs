using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/bills/{number}/appeal</c>: the bill as people take part on it, with both sides' counts, the threshold and
/// the published arguments (design 8.8, 8.12.1). Public and the same for everyone: an active bill nobody took part in
/// answers the empty open state, not "not found".
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

    private static async Task<IResult> HandleAsync(HttpContext http, IAppealsModule appeals, ErrorMapper errors, string number, CancellationToken cancellationToken)
    {
        var result = await appeals.GetAsync(number, cancellationToken);
        return result.IsSuccess
            ? PublicJson.From(http, errors, Lt2.SharedKernel.Result<AppealResponse>.Success(AppealMapping.Map(result.Value)), maxAgeSeconds: 0)
            : errors.ToResult(result.Error!);
    }
}
