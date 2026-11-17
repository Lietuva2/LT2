using System.Security.Claims;
using Lt2.Api.Contracts.Mps;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Mps;

/// <summary>
/// <c>POST /api/v1/me/mp/takeover</c> (design 8.5.2, step 3): the link's token (from the page's <c>#</c>, in the body) and
/// the passkey made for it. One transaction links the account, keeps only the new passkey and withdraws the account's
/// sides, so the front page's copy of the agenda is marked out of date as for any withdrawn side.
/// </summary>
internal static class TakeOverProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(MpRoutes.Takeover), HandleAsync)
            .WithName("TakeOverMpProfile")
            .WithTags("Mps")
            .RequireAuthorization(Policies.Member)
            .WithValidation<MpTakeoverRequest>()
            .RequireRateLimiting(RateLimitPolicies.OneTimeCode)
            .Produces<MpLinkResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        MpTakeoverRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IpHasher ipHasher,
        IPoliticians politicians,
        FrontPageFreshness frontPage,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await politicians.TakeOverAsync(user, request.Token, request.Credential, ipHasher.Hash(context), cancellationToken);
        if (!result.IsSuccess)
        {
            return errors.ToResult(result.Error!);
        }

        frontPage.MarkStale();
        return TypedResults.Ok(MpMapping.Map(result.Value));
    }
}
