using System.Security.Claims;
using Lt2.Api.Contracts.Mps;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Mps;

/// <summary>
/// <c>POST /api/v1/mps/{slug}/claim</c> (design 8.5.2, step 1): "Perimti profilį". The take-over link goes only to the
/// account's own address, and only when that is the MP's official address; at most three a day per MP. Also replaces a
/// lost passkey (step 4).
/// </summary>
internal static class ClaimProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(MpRoutes.Claim), HandleAsync)
            .WithName("ClaimMpProfile")
            .WithTags("Mps")
            .RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.CodeRequest)
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        string slug, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IPoliticians politicians, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await politicians.ClaimAsync(user, slug, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.Accepted((string?)null) : errors.ToResult(result.Error!);
    }
}
