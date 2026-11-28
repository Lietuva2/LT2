using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Digests;

/// <summary>
/// <c>GET /api/v1/bills/{number}/record/digests/next</c>: the Vilnius day the next summary holding the bill's record goes, a
/// prepared summary's own or the one the rules give now, or none known (design 8.12.2, owner 2026-11-18: the bill's page says
/// it once the threshold is reached). Not found like the record itself.
/// </summary>
internal static class GetRecordNextDigestEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.NextOfRecord), HandleAsync)
            .WithName("GetRecordNextDigest")
            .WithTags("Digests")
            .AllowAnonymous()
            .Produces<RecordNextDigestResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, string number, RecordOfRequest records, IDigests digests, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, await DigestMapping.NextForRecordAsync(records, digests, number, ct), maxAgeSeconds: 0);
}
