using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>GET /api/v1/me/party-support</c>: the caller's own party (or none), the parties to choose from, and whether it is shown on
/// the public profile (design 8.25.9).
/// </summary>
internal static class GetPartySupportEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Me.PartySupport), HandleAsync)
            .WithName("GetPartySupport")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .Produces<PartySupportResponse>();

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, IIdentityModule identity, IMpDirectory mps, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.GetPartySupportAsync(user, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(Mapping.ToResponse(result.Value!, await mps.PartiesAsync(cancellationToken)))
            : errors.ToResult(result.Error!);
    }
}