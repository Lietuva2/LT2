using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary><c>POST /api/v1/auth/passkeys/sign-in</c>: verifies the assertion and sets the session cookie.</summary>
internal static class PasskeySignInEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.PasskeySignIn), HandleAsync)
            .WithName("SignInWithPasskey")
            .WithTags("Passkeys")
            .WithValidation<PasskeyCredentialRequest>()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        PasskeyCredentialRequest request,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.SignInWithPasskeyAsync(request.Credential, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
