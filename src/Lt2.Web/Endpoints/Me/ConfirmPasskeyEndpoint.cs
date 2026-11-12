using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>POST /api/v1/me/passkey-confirmation</c>: a fresh assertion with the staff passkey, for the ten-minute rule on
/// passkey changes and sensitive actions (design 6.2). The challenge comes from
/// <c>POST /api/v1/auth/passkeys/request-options</c>.
/// </summary>
internal static class ConfirmPasskeyEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.PasskeyConfirmation), HandleAsync)
            .WithName("ConfirmPasskey")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .WithValidation<PasskeyCredentialRequest>()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        PasskeyCredentialRequest request,
        ClaimsPrincipal user,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.ConfirmPasskeyAsync(user, request.Credential, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
