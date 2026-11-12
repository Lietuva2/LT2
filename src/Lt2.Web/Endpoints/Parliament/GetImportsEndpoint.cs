using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Meta;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/meta/imports: the last successful import per source</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetImportsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Imports), HandleAsync)
            .WithName("GetImports")
            .WithTags("Meta")
            .AllowAnonymous()
            .Produces<ImportsResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, await reads.ImportsAsync(ct));
}
