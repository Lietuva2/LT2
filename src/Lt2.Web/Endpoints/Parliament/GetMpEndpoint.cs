using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/mps/{slug}: one MP's profile with the two statistics</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetMpEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Mp), HandleAsync)
            .WithName("GetMp")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<MpProfileResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, string slug, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.MpAsync(slug, ct));
}
