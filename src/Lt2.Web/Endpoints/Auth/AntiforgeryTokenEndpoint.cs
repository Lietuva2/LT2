using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace Lt2.Web.Endpoints.Auth;

/// <summary>
/// <c>GET /api/v1/auth/antiforgery</c>: sets the antiforgery cookie and returns the matching request token, which the
/// browser client sends in the <c>X-XSRF-TOKEN</c> header. Readable only by this site's own pages (no CORS).
/// </summary>
internal static class AntiforgeryTokenEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Auth.Antiforgery), Handle)
            .WithName("GetAntiforgeryToken")
            .WithTags("Auth");

    private static Ok<AntiforgeryTokenResponse> Handle(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return TypedResults.Ok(new AntiforgeryTokenResponse { Token = tokens.RequestToken ?? "" });
    }
}
