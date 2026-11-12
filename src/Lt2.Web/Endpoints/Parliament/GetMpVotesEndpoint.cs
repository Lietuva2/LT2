using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/mps/{slug}/votes?cursor=: the MP's votes, newest first</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetMpVotesEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.MpVotes), HandleAsync)
            .WithName("GetMpVotes")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<MpVotesResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, string slug, string? cursor, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.MpVotesAsync(slug, cursor, ct));
}
