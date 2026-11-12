using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary><c>GET /api/v1/me/sessions</c>: where the person is signed in ("my sessions", design 6.2).</summary>
internal static class GetSessionsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Me.Sessions), HandleAsync)
            .WithName("GetMySessions")
            .WithTags("Me")
            .RequireAuthorization()
            .Produces<IReadOnlyList<SessionResponse>>();

    private static async Task<Ok<List<SessionResponse>>> HandleAsync(ClaimsPrincipal user, IIdentityModule identity, CancellationToken cancellationToken)
    {
        var sessions = await identity.GetSessionsAsync(user, cancellationToken);
        return TypedResults.Ok(sessions.Select(Mapping.ToResponse).ToList());
    }
}
