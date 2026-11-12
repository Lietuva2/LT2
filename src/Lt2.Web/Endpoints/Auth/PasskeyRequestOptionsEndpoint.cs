using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary><c>POST /api/v1/auth/passkeys/request-options</c>: WebAuthn request options for passkey sign-in.</summary>
internal static class PasskeyRequestOptionsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.PasskeyRequestOptions), HandleAsync)
            .WithName("GetPasskeyRequestOptions")
            .WithTags("Passkeys")
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces<string>(StatusCodes.Status200OK, "application/json");

    private static async Task<IResult> HandleAsync(IIdentityModule identity, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.GetPasskeyRequestOptionsAsync(cancellationToken);
        return result.IsSuccess ? TypedResults.Content(result.Value, "application/json") : errors.ToResult(result.Error!);
    }
}
