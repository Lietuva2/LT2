using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/bills/{number}: a bill and its votes</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetBillEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Bill), HandleAsync)
            .WithName("GetBill")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<BillResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, string number, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.BillAsync(number, ct));
}
