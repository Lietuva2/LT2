using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Auth;

/// <summary>
/// <c>GET /api/v1/bot-check/challenge</c>: a proof-of-work challenge in the ALTCHA format for the site's own script
/// (design 6.4). No session needed, never stored by anything; 204 while <c>BotCheck:Enabled</c> is off, which the script
/// takes as "nothing to solve".
/// </summary>
internal static class BotChallengeEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.BotCheck.Challenge), HandleAsync)
            .WithName("GetBotChallenge")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimitPolicies.BotChallenge)
            .Produces<BotChallengeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(IIdentityModule identity, CancellationToken cancellationToken)
    {
        var challenge = await identity.NewBotChallengeAsync(cancellationToken);
        return challenge is null
            ? TypedResults.NoContent()
            : TypedResults.Ok(new BotChallengeResponse
            {
                Algorithm = challenge.Algorithm,
                Challenge = challenge.Challenge,
                MaxNumber = challenge.MaxNumber,
                Salt = challenge.Salt,
                Signature = challenge.Signature,
            });
    }
}
