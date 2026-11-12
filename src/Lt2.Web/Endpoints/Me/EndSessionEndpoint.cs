using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary><c>DELETE /api/v1/me/sessions/{id}</c>: ends one of the person's sessions at once.</summary>
internal static class EndSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapDelete(ApiEndpoints.Relative(ApiRoutes.Me.Session), HandleAsync)
            .WithName("EndMySession")
            .WithTags("Me")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id,
        ClaimsPrincipal user,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.EndSessionAsync(user, id, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
