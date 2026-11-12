using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary>
/// <c>POST /api/v1/auth/register</c>: email, invitation code and the 18+ confirmation. Answers 202 whether or not the
/// address was already registered; either way a sign-in code is emailed.
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
