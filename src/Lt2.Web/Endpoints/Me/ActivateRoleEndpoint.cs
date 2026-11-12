using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>POST /api/v1/me/role-activation</c>, in a passkey session: the one-time code an administrator passed on activates
/// the granted staff role; this session's passkey becomes the staff passkey and every other passkey on the account is
/// removed (design 6.2).
/// </summary>
internal static class ActivateRoleEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.RoleActivation), HandleAsync)
            .WithName("ActivateRole")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .WithValidation<RoleActivationRequest>()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        RoleActivationRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IpHasher ipHasher,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.ActivateRoleAsync(user, request.Code, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
