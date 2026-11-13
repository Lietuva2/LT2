using Lt2.Api.Contracts.Digests;
using Lt2.Appeals;
using Lt2.Notifications;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Digests;

/// <summary>
/// <c>GET /api/v1/bills/{number}/appeal/digests</c>: the summaries that held the published appeal on a bill, newest first,
/// each with the appeal as it was frozen (design 8.7, 8.8). Empty until the first; not found like the appeal itself.
/// </summary>
internal static class GetAppealDigestsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.OfAppeal), HandleAsync)
            .WithName("GetAppealDigests")
            .WithTags("Digests")
            .AllowAnonymous()
            .Produces<IReadOnlyList<AppealDigestResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, string number, IAppealsModule appeals, IDigests digests, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, await DigestMapping.ForAppealAsync(appeals, digests, number, ct), maxAgeSeconds: 0);
}