using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/factions/{slug}: a faction, its members and recent positions</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetFactionEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Faction), HandleAsync)
            .WithName("GetFaction")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<BodyResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, string slug, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.BodyAsync("faction", slug, ct));
}
