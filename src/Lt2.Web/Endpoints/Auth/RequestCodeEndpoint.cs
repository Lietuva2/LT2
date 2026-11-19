using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary>
/// <c>POST /api/v1/auth/code</c>: emails a sign-in code if the address has an account. 202 whether it has or not; the one refusal is
/// the proof of work's, 400 <c>bot-check.failed</c> (design 6.4), which says nothing about the address.
/// </summary>
internal static class RequestCodeEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.Code), HandleAsync)
            .WithName("RequestSignInCode")
            .WithTags("Auth")
            .WithValidation<RequestCodeRequest>()
            .RequireRateLimiting(RateLimitPolicies.CodeRequest)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> HandleAsync(
        RequestCodeRequest request,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.RequestSignInCodeAsync(request.Email, request.BotCheck, cancellationToken);
        return result.IsSuccess ? TypedResults.StatusCode(StatusCodes.Status202Accepted) : errors.ToResult(result.Error!);
    }
}
