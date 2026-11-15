using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/me/writing-restriction</c>: the caller's own writing restriction, if one holds (design 8.3), so the pages
/// can say so in place of the writing box. Only ever the caller's; never cached.
/// </summary>
internal static class GetMyRestrictionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.MyRestriction), HandleAsync)
            .WithName("GetMyWritingRestriction")
            .WithTags("Appeals")
            .RequireAuthorization(Policies.Member)
            .Produces<MyRestrictionResponse>();

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, IAppealsModule appeals, CancellationToken cancellationToken) =>
        TypedResults.Ok(AppealMapping.Map(await appeals.MyRestrictionAsync(user.UserId()!.Value, cancellationToken)));
}