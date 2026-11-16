using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Follows;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>GET|PUT /api/v1/me/notices/frequency</c> (design 8.24): how often the member's emails come, "Iš karto", "Kartą per dieną"
/// or "Kartą per savaitę ir artėjant balsavimui". The Notifications module checks the value (<c>notices.frequency-invalid</c>)
/// and audits the change without the choice; the shared per-account limit (<see cref="RateLimitPolicies.Participation"/>) only
/// guards against a runaway script. The terms gate covers the write like every other.
/// </summary>
internal static class NoticeFrequencyEndpoint
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(NoticeFrequencyRoutes.Mine), GetAsync)
            .WithName("GetNoticeFrequency").WithTags("Me").RequireAuthorization(Policies.Member).Produces<NoticeFrequencyResponse>();
        api.MapPut(ApiEndpoints.Relative(NoticeFrequencyRoutes.Mine), SetAsync)
            .WithName("SetNoticeFrequency").WithTags("Me").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent).ProducesValidationProblem();
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal user, IFollows follows, CancellationToken ct) =>
        TypedResults.Ok(new NoticeFrequencyResponse(await follows.FrequencyAsync(user.UserId()!.Value, ct)));

    private static async Task<IResult> SetAsync(
        NoticeFrequencyRequest request, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IFollows follows, ErrorMapper errors, CancellationToken ct)
    {
        var result = await follows.SetFrequencyAsync(user.UserId()!.Value, request.Frequency ?? "", ipHasher.Hash(context), ct);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
