using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary>
/// <c>GET /api/v1/votes?q=&amp;kind=&amp;until=&amp;limit=&amp;cursor=: the history of votes, newest first</c> (design 8.18).
/// Public, cached with an ETag (design 7.4), nothing about the caller. The values are taken as text and read by
/// <see cref="VoteListQuery"/>, so one the API would not make falls back to its default instead of an error.
/// </summary>
internal static class GetVotesEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Votes), HandleAsync)
            .WithName("GetVotes")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<VoteListResponse>()
            .Produces(StatusCodes.Status304NotModified);

    private static async Task<IResult> HandleAsync(
        HttpContext http, ParliamentReads reads, ErrorMapper errors, string? q, string? kind, string? until, string? limit, string? cursor, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.VotesAsync(q, kind, VoteListQuery.Until(until), VoteListQuery.Limit(limit), cursor, ct));
}
