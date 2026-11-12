using Lt2.Api.Contracts;
using Lt2.Identity;

namespace Lt2.Web.Endpoints.Auth;

/// <summary><c>POST /api/v1/auth/sign-out</c>: ends the current session (its row is deleted) and clears the cookie.</summary>
internal static class SignOutEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.SignOut), HandleAsync)
            .WithName("SignOut")
            .WithTags("Auth")
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<NoContent> HandleAsync(IIdentityModule identity)
    {
        await identity.SignOutAsync();
        return TypedResults.NoContent();
    }
}
