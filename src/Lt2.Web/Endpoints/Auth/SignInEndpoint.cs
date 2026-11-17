using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary>
/// <c>POST /api/v1/auth/sign-in</c>: email and the six-digit code; sets the session cookie of a new session on success and
/// answers whether the browser was signed in to another account, whose session ended (design 6.2, owner 2026-11-17).
/// </summary>
internal static class SignInEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.SignIn), HandleAsync)
            .WithName("SignInWithCode")
            .WithTags("Auth")
            .WithValidation<SignInRequest>()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces<SignInResponse>();

    private static async Task<IResult> HandleAsync(
        SignInRequest request,
        HttpContext context,
        IIdentityModule identity,
        AnalyticsCopies analytics,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var before = context.User.UserId();
        var result = await identity.SignInWithCodeAsync(request.Email, request.Code, cancellationToken);
        return result.IsSuccess ? Answer(result.Value, before, analytics) : errors.ToResult(result.Error!);
    }

    /// <summary>
    /// The answer of a sign-in. The other account's session ended as by signing out, and its kept analytics comparison with it
    /// (design 8.26.6).
    /// </summary>
    internal static IResult Answer(SignInOutcome outcome, Guid? before, AnalyticsCopies analytics)
    {
        if (outcome.AccountChanged && before is { } previous)
        {
            analytics.Forget(previous);
        }

        return TypedResults.Ok(new SignInResponse(outcome.AccountChanged));
    }
}
