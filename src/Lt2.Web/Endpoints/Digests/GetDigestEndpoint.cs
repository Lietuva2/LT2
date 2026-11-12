using System.Globalization;
using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Digests;

/// <summary><c>GET /api/v1/digests/{date}</c>: the summaries made that day (yyyy-MM-dd), frozen, without MP-specific figures (design 8.7).</summary>
internal static class GetDigestEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(DigestRoutes.Digest), HandleAsync)
            .WithName("GetDigest")
            .WithTags("Digests")
            .AllowAnonymous()
            .Produces<IReadOnlyList<DigestRunResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, string date, IDigests digests, ErrorMapper errors, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return errors.ToResult(Error.NotFound("digest.not-found"));
        }

        var runs = await digests.OnDateAsync(day, ct);
        return runs.Count == 0
            ? errors.ToResult(Error.NotFound("digest.not-found"))
            : PublicJson.From(http, errors, Result<List<DigestRunResponse>>.Success(runs.Select(DigestMapping.Map).ToList()), maxAgeSeconds: 0);
    }
}