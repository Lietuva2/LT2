using System.Security.Claims;
using Lt2.Api.Contracts.Analytics;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Analytics;

/// <summary>
/// <c>GET /api/v1/me/analytics/agreement</c> (design 8.26.4): the caller's own comparison with MPs and factions. A member's
/// own answer: never cached (as every <c>/api</c> answer that does not say otherwise), nothing about anyone else, limited
/// per account like the other own reads.
/// </summary>
internal static class GetMyAgreementEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AnalyticsRoutes.MyAgreement), HandleAsync)
            .WithName("GetMyAgreement")
            .WithTags("Analytics")
            .RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.OwnRead)
            .Produces<MyAgreementResponse>();

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, MyAgreementReads reads, CancellationToken ct) =>
        TypedResults.Ok(await reads.ReadAsync(user.UserId()!.Value, ct));
}
