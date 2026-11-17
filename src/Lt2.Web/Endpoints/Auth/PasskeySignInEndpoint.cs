using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary>
/// <c>POST /api/v1/auth/passkeys/sign-in</c>: verifies the assertion and sets the session cookie of a new session; answers
/// whether the browser was signed in to another account, whose session ended (design 6.2, owner 2026-11-17).
/// </summary>
internal static class PasskeySignInEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.PasskeySignIn), HandleAsync)
            .WithName("SignInWithPasskey")
            .WithTags("Passkeys")
            .WithValidation<PasskeyCredentialRequest>()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces<SignInResponse>();

    private static async Task<IResult> HandleAsync(
        PasskeyCredentialRequest request,
        HttpContext context,
        IIdentityModule identity,
        AnalyticsCopies analytics,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var before = context.User.UserId();
        var result = await identity.SignInWithPasskeyAsync(request.Credential, cancellationToken);
        return result.IsSuccess ? SignInEndpoint.Answer(result.Value, before, analytics) : errors.ToResult(result.Error!);
    }
}
