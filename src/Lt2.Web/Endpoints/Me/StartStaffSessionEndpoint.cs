using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>POST /api/v1/me/staff-session</c> (design 6.2, owner 2026-11-16): a code session on a staff account becomes a staff
/// session by an assertion with the account's own staff passkey, without signing out; the session gets a new key and the old
/// one stops working. Any other passkey is refused with <c>passkey.staff-required</c>. The challenge comes from
/// <c>POST /api/v1/auth/passkeys/request-options</c>. Like the confirmation, it is not exempt from the terms of use.
/// </summary>
internal static class StartStaffSessionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.StaffSession), HandleAsync)
            .WithName("StartStaffSession")
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
        var result = await identity.StartStaffSessionAsync(user, request.Credential, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
