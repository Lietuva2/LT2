using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

internal static class PasskeyCreationOptionsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.PasskeyCreationOptions), HandleAsync)
            .WithName("GetPasskeyCreationOptions")
            .WithTags("Passkeys")
            .RequireAuthorization(Policies.Member)
            .Produces<string>(StatusCodes.Status200OK, "application/json");

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, IIdentityModule identity, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.GetPasskeyCreationOptionsAsync(user, cancellationToken);
        return result.IsSuccess ? TypedResults.Content(result.Value, "application/json") : errors.ToResult(result.Error!);
    }
}
