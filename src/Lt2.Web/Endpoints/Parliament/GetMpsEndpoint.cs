using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/mps?faction=&amp;committee=&amp;q=&amp;cursor=: MPs of the current term, alphabetical</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetMpsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Mps), HandleAsync)
            .WithName("GetMps")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<MpListResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, string? faction, string? committee, string? q, string? cursor, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.MpsAsync(faction, committee, q, cursor, ct));
}
