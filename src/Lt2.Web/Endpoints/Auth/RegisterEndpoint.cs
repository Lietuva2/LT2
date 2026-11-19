using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary>
/// <c>POST /api/v1/auth/register</c>: email and invitation code; creates a pending account (design 6.2). Answers 202
/// whether the address is new, pending or registered; in every case a sign-in code is emailed. The proof of work comes first:
/// without a good one, 400 <c>bot-check.failed</c> (design 6.4), whatever the address.
/// </summary>
internal static class RegisterEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.Register), HandleAsync)
            .WithName("Register")
            .WithTags("Auth")
            .WithValidation<RegisterRequest>()
            .RequireRateLimiting(RateLimitPolicies.Registration)
            .Produces(StatusCodes.Status202Accepted);

    private static async Task<IResult> HandleAsync(
        RegisterRequest request,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.RegisterAsync(Mapping.ToRegistration(request), cancellationToken);
        return result.IsSuccess ? TypedResults.StatusCode(StatusCodes.Status202Accepted) : errors.ToResult(result.Error!);
    }
}
