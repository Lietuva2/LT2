using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/terms/current: the current Seimas term</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetCurrentTermEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.CurrentTerm), HandleAsync)
            .WithName("GetCurrentTerm")
            .WithTags("Parliament")
            .AllowAnonymous()
            .Produces<TermResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.CurrentTermAsync(ct));
}
