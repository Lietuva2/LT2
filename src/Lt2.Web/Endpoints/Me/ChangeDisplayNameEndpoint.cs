using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>PUT /api/v1/me/display-name</c> (design 6.2): the member changes the name shown beside what they write in public, as
/// often as they like (owner, 2026-11-16). The Identity module checks the length and a writing restriction
/// (<c>writing.restricted</c>) and audits that it changed; the shared per-account limit
/// (<see cref="RateLimitPolicies.Participation"/>) only guards against a runaway script.
/// </summary>
internal static class ChangeDisplayNameEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(ApiRoutes.Me.DisplayName), HandleAsync)
            .WithName("ChangeDisplayName")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .WithValidation<DisplayNameRequest>()
            .RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        DisplayNameRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IpHasher ipHasher,
        IIdentityModule identity,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await identity.ChangeDisplayNameAsync(user, request.DisplayName, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
