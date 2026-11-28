using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Digests;

/// <summary>
/// <c>GET /api/v1/bills/{number}/record/digests</c>: the summaries that held the published record on a bill, newest first,
/// each with the record as it was frozen (design 8.7, 8.8). Empty until the first; not found like the record itself.
/// </summary>
internal static class GetRecordDigestsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.OfRecord), HandleAsync)
            .WithName("GetRecordDigests")
            .WithTags("Digests")
            .AllowAnonymous()
            .Produces<IReadOnlyList<RecordDigestResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, string number, RecordOfRequest records, IDigests digests, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, await DigestMapping.ForRecordAsync(records, digests, number, ct), maxAgeSeconds: 0);
}