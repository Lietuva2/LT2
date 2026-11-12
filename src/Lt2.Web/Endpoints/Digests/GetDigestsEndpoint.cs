using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Digests;

/// <summary><c>GET /api/v1/digests</c>: every summary made, newest first, as its public copy (design 8.7, 8.8). In M2a all are dry runs.</summary>
internal static class GetDigestsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.Digests), HandleAsync)
            .WithName("GetDigests")
            .WithTags("Digests")
            .AllowAnonymous()
            .Produces<IReadOnlyList<DigestRunResponse>>();

    private static async Task<IResult> HandleAsync(HttpContext http, IDigests digests, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, Lt2.SharedKernel.Result<List<DigestRunResponse>>.Success((await digests.ListAsync(ct)).Select(DigestMapping.Map).ToList()), maxAgeSeconds: 0);
}