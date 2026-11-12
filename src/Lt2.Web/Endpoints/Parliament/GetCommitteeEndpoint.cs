using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/committees/{slug}: a committee and its members</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetCommitteeEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Committee), HandleAsync)
            .WithName("GetCommittee")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<BodyResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, string slug, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.BodyAsync("committee", slug, ct));
}
