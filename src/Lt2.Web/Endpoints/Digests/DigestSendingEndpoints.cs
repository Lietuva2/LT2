using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Digests;

/// <summary>
/// Design 8.19.2, 8.19.5: <c>GET /api/v1/moderation/digests</c> (the moderators' summaries screen), <c>POST …/{id}/send</c>
/// ("Siųsti") and <c>POST …/{id}/cancel</c> ("Nesiųsti", with a statement), each needing a moderator's staff-passkey session and
/// audited with the moderator, whom no public answer names; and <c>GET /api/v1/digests/introduction</c>, the introduction
/// letter as sent (public).
/// </summary>
internal static class DigestSendingEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.Moderation), ListAsync)
            .WithName("GetModerationDigests").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .Produces<DigestModerationResponse>();

        api.MapPost(ApiEndpoints.Relative(DigestRoutes.Send), SendAsync)
            .WithName("SendDigest").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .Produces<DigestRunResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(DigestRoutes.Cancel), CancelAsync)
            .WithName("CancelDigest").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .WithValidation<CancelDigestRequest>()
            .Produces<DigestRunResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(DigestRoutes.Introduction), IntroductionAsync)
            .WithName("GetIntroduction").WithTags("Digests").AllowAnonymous()
            .Produces<IReadOnlyList<IntroductionLetterResponse>>();
    }

    private static async Task<IResult> ListAsync(IDigests digests, CancellationToken ct) =>
        TypedResults.Ok(await DigestMapping.ModerationAsync(digests, ct));

    private static async Task<IResult> SendAsync(
        Guid id, HttpContext context, IDigests digests, Lt2.Identity.IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        // Design 6.2, 8.19.2: approving a send asks for the passkey again (the last ten minutes); declining does not.
        if (await Admin.FreshConfirmation.RequireAsync(identity, context.User, errors, ct) is { } refused)
        {
            return refused;
        }

        var result = await digests.SendAsync(id, await ModeratorEndpoint.ModeratorAsync(context), ct);
        return result.IsSuccess ? TypedResults.Ok(DigestMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> CancelAsync(Guid id, CancelDigestRequest request, HttpContext context, IDigests digests, ErrorMapper errors, CancellationToken ct)
    {
        var result = await digests.CancelAsync(id, request.Statement, await ModeratorEndpoint.ModeratorAsync(context), ct);
        return result.IsSuccess ? TypedResults.Ok(DigestMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> IntroductionAsync(HttpContext http, IMpMailAdministration mpMail, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(
            http,
            errors,
            Result<List<IntroductionLetterResponse>>.Success((await mpMail.SentIntroductionsAsync(ct)).Select(DigestMapping.Public).ToList()),
            maxAgeSeconds: 0);
}
