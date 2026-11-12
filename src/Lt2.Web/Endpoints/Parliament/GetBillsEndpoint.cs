using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/bills?q=&amp;cursor=: bills with votes, latest vote first</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetBillsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Bills), HandleAsync)
            .WithName("GetBills")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<BillListResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, string? q, string? cursor, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.BillsAsync(q, cursor, ct));
}
