using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>POST /api/v1/me/passkey-session</c> (design 6.2, owner 2026-11-17: "Patvirtinti prieigos raktu" in place): the signed-in
/// account's session becomes a passkey session by an assertion with one of its own passkeys, without signing out; with the
/// staff passkey a staff session too. The session gets a new key and the old one stops working. Another account's passkey is
/// refused with <c>passkey.other-account</c> and changes nothing. The challenge comes from
/// <c>POST /api/v1/auth/passkeys/request-options</c>. Like the confirmation, it is not exempt from the terms of use.
/// </summary>
internal static class StartPasskeySessionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.PasskeySession), HandleAsync)
            .WithName("StartPasskeySession")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .WithValidation<PasskeyCredentialRequest>()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
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
        var result = await identity.StartPasskeySessionAsync(user, request.Credential, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
