using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// Known browsers (design 8.20.2), the person's own: <c>GET /api/v1/me/known-browsers</c> lists them beside the sessions, and
/// <c>DELETE /api/v1/me/known-browsers/{id}</c> ("Pamiršti") makes one a stranger again. Never the cookie or its hash.
/// </summary>
internal static class KnownBrowserEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Me.KnownBrowsers), ListAsync)
            .WithName("GetMyKnownBrowsers")
            .WithTags("Me")
            .RequireAuthorization()
            .Produces<IReadOnlyList<KnownBrowserResponse>>();
        api.MapDelete(ApiEndpoints.Relative(ApiRoutes.Me.KnownBrowser), ForgetAsync)
            .WithName("ForgetMyKnownBrowser")
            .WithTags("Me")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, IIdentityModule identity, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.GetKnownBrowsersAsync(user, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(Mapping.ToKnownBrowsers(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> ForgetAsync(Guid id, ClaimsPrincipal user, IIdentityModule identity, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.ForgetKnownBrowserAsync(user, id, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
