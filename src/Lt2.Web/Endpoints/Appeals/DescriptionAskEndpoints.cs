using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// "Prašau aprašymo" (design 8.13.2, owner 2026-11-27) on any bill that shows no description, open or finished:
/// <c>GET /api/v1/bills/{number}/description-ask</c>, public: how many asked, whether the caller did (null for a visitor),
/// and whether asking is open (never cached, as every <c>/api</c> answer that does not say otherwise: it holds the caller's
/// own ask); <c>PUT</c> asks and <c>DELETE</c> takes the ask back, a signed-in member, within the per-account request limit
/// <see cref="RateLimitPolicies.Participation"/>. No answer says who asked.
/// </summary>
internal static class DescriptionAskEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.DescriptionAsk), GetAsync)
            .WithName("GetDescriptionAsk").WithTags("Appeals").AllowAnonymous()
            .Produces<DescriptionAskResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.DescriptionAsk), (string number, ClaimsPrincipal user, IDescriptionAsks asks, ErrorMapper e, CancellationToken ct) =>
                RunAsync(e, asks.AskAsync(number, user.UserId()!.Value, ct)))
            .WithName("AskDescription").WithTags("Appeals").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        api.MapDelete(ApiEndpoints.Relative(AppealRoutes.DescriptionAsk), (string number, ClaimsPrincipal user, IDescriptionAsks asks, ErrorMapper e, CancellationToken ct) =>
                RunAsync(e, asks.WithdrawAsync(number, user.UserId()!.Value, ct)))
            .WithName("WithdrawDescriptionAsk").WithTags("Appeals").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> GetAsync(string number, ClaimsPrincipal user, IDescriptionAsks asks, ErrorMapper errors, CancellationToken ct)
    {
        var result = await asks.StateAsync(number, user.UserId(), ct);
        return result.IsSuccess ? TypedResults.Ok(AppealMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> RunAsync(ErrorMapper errors, Task<Result<Unit>> action)
    {
        var result = await action;
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
