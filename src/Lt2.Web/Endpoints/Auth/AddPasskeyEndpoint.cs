using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Auth;

/// <summary><c>POST /api/v1/auth/passkeys</c> (signed in): verifies the attestation and stores the passkey.</summary>
internal static class AddPasskeyEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Auth.Passkeys), HandleAsync)
            .WithName("AddPasskey")
            .WithTags("Passkeys")
            .RequireAuthorization(Policies.Member)
            .WithValidation<PasskeyCredentialRequest>()
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        PasskeyCredentialRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IpHasher ipHasher,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.AddPasskeyAsync(user, request.Credential, request.Name, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
