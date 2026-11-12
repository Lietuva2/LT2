using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary><c>GET /api/v1/appeals/rules</c>: the threshold offered and the minimum, and the limits (design 8.4). Public.</summary>
internal static class GetAppealRulesEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Rules), HandleAsync)
            .WithName("GetAppealRules")
            .WithTags("Appeals")
            .AllowAnonymous()
            .Produces<AppealRulesResponse>();

    private static async Task<IResult> HandleAsync(HttpContext http, IAppealsModule appeals, ErrorMapper errors, CancellationToken cancellationToken) =>
        PublicJson.From(http, errors, Lt2.SharedKernel.Result<AppealRulesResponse>.Success(AppealMapping.Map(await appeals.RulesAsync(cancellationToken))), maxAgeSeconds: 0);
}
