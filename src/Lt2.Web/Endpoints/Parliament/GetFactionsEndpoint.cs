using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/factions: the factions of the current term</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetFactionsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Factions), HandleAsync)
            .WithName("GetFactions")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<IReadOnlyList<BodyListItem>>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.BodiesAsync(BodyKinds.Faction, ct));
}
