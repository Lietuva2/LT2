using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Digests;

/// <summary><c>GET /api/v1/moderation/digests/{id}/mps</c>: the MPs of a summary and what would have happened for each. Moderators.</summary>
internal static class GetDigestMpsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.Mps), HandleAsync)
            .WithName("GetDigestMps")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<IReadOnlyList<DigestMpResponse>>();

    private static async Task<IResult> HandleAsync(Guid id, IDigests digests, CancellationToken ct) =>
        TypedResults.Ok((await digests.MpsAsync(id, ct)).Select(m => new DigestMpResponse(m.MpSlug, m.Status)).ToList());
}