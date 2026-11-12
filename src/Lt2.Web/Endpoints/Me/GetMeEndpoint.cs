using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary><c>GET /api/v1/me</c>: the signed-in person's account.</summary>
internal static class GetMeEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Me.Root), HandleAsync)
            .WithName("GetMe")
            .WithTags("Me")
            .RequireAuthorization()
            .Produces<MeResponse>();

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, IIdentityModule identity, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.GetAccountAsync(user, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(Mapping.ToResponse(result.Value)) : errors.ToResult(result.Error!);
    }
}
