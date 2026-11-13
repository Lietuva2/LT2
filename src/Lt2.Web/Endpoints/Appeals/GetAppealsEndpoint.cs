using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>GET /api/v1/appeals?sort=&amp;cursor=</c>: published appeals with both sides' counts (design 8.8). Public.</summary>
internal static class GetAppealsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Appeals), HandleAsync)
            .WithName("GetAppeals")
            .WithTags("Appeals")
            .AllowAnonymous()
            .Produces<AppealListResponse>()
            .Produces(StatusCodes.Status304NotModified);

    private static async Task<IResult> HandleAsync(HttpContext http, IAppealsModule appeals, ErrorMapper errors, string? sort, string? cursor, CancellationToken cancellationToken) =>
        PublicJson.From(http, errors, Lt2.SharedKernel.Result<AppealListResponse>.Success(AppealMapping.Map(await appeals.ListAsync(sort, cursor, cancellationToken))), maxAgeSeconds: 0);
}
