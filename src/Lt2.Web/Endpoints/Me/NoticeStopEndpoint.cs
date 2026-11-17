using Lt2.Api.Contracts.Follows;
using Lt2.Notifications;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>POST /api/v1/notices/{token}/stop</c> (design 8.24.1): a member's stop link, the one-click stop of RFC 8058 that mail programs
/// send for <c>List-Unsubscribe-Post: List-Unsubscribe=One-Click</c>, and the page "Nebegauti laiškų"'s button. Like an MP's
/// one-click stop (design 8.19.4) it is mapped on the application, outside the API group's same-origin and antiforgery filter,
/// because a mail program has neither; it reads no body and can do nothing but turn the token's account's email switches off.
/// Limited per client address and per token (<see cref="RateLimitPolicies.MpMail"/>, <see cref="MpMailTokens"/>), on
/// <c>TermsExemptions.Routes</c>, and answers 204 for an unknown, a malformed and a valid token alike.
/// </summary>
internal static class NoticeStopEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost(NoticeStopRoutes.Stop, HandleAsync)
            .WithName("StopNoticesByLink").WithTags("Me").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.MpMail)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(string token, HttpContext context, IpHasher ipHasher, IFollows follows, CancellationToken ct)
    {
        await follows.StopByLinkAsync(token, ipHasher.Hash(context), ct);
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.NoContent();
    }
}
