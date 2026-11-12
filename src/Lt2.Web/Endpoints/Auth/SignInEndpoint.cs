using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary><c>POST /api/v1/auth/sign-in</c>: email and the six-digit code; sets the session cookie on success.</summary>
internal static class SignInEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.SignIn), HandleAsync)
            .WithName("SignInWithCode")
            .WithTags("Auth")
            .WithValidation<SignInRequest>()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        SignInRequest request,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.SignInWithCodeAsync(request.Email, request.Code, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
