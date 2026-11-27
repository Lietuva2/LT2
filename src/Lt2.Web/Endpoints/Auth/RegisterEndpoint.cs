using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary>
/// <c>POST /api/v1/auth/register</c>: email and invitation code; creates a pending account (design 6.2). Answers 202
/// whether the address is new, pending or registered; in every case a sign-in code is emailed. The proof of work comes first:
/// without a good one, 400 <c>bot-check.failed</c> (design 6.4), whatever the address. On a trial site without email
/// (<c>Registration:WithoutEmail</c>, design 6.2) a new address with a working invitation code is signed in instead, and the
/// answer says so (<see cref="RegisterResponse.SignedIn"/>).
/// </summary>
internal static class RegisterEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.Register), HandleAsync)
            .WithName("Register")
            .WithTags("Auth")
            .WithValidation<RegisterRequest>()
            .RequireRateLimiting(RateLimitPolicies.Registration)
            .Produces<RegisterResponse>(StatusCodes.Status202Accepted);

    private static async Task<IResult> HandleAsync(
        RegisterRequest request,
        HttpContext context,
        IIdentityModule identity,
        AnalyticsCopies analytics,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var before = context.User.UserId();
        var result = await identity.RegisterAsync(Mapping.ToRegistration(request), cancellationToken);
        if (!result.IsSuccess)
        {
            return errors.ToResult(result.Error!);
        }

        // As after a sign-in: another account's session ended, and its kept analytics comparison with it (design 8.26.6).
        if (result.Value!.AccountChanged && before is { } previous)
        {
            analytics.Forget(previous);
        }

        return TypedResults.Json(new RegisterResponse(result.Value.SignedIn, result.Value.AccountChanged), statusCode: StatusCodes.Status202Accepted);
    }
}
