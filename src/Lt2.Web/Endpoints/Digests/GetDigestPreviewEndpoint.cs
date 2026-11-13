using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Digests;

/// <summary>
/// <c>GET /api/v1/moderation/digests/{id}/mps/{slug}</c>: the summary as that MP would read it, with the MP-specific
/// numbers (design 8.9: a moderator can read any MP's summary before a real send). Moderators only.
/// </summary>
internal static class GetDigestPreviewEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.Preview), HandleAsync)
            .WithName("GetDigestPreview")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<DigestPreviewResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(Guid id, string slug, IDigests digests, ErrorMapper errors, CancellationToken ct)
    {
        var result = await digests.PreviewAsync(id, slug, ct);
        return result.IsSuccess ? TypedResults.Ok(new DigestPreviewResponse(result.Value.Subject, result.Value.Text)) : errors.ToResult(result.Error!);
    }
}