using Lt2.Api.Contracts.Digests;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Digests;

/// <summary><c>GET /api/v1/digests/rules</c>: the rules that decide what MPs would receive, with their current values (design 8.4). Public.</summary>
internal static class GetDigestRulesEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.Rules), HandleAsync)
            .WithName("GetDigestRules")
            .WithTags("Digests")
            .AllowAnonymous()
            .Produces<DigestRulesResponse>();

    private static async Task<IResult> HandleAsync(HttpContext http, ISettings settings, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, Result<DigestRulesResponse>.Success(await ServerDigestsApi.RulesAsync(settings, ct)), maxAgeSeconds: 0);
}