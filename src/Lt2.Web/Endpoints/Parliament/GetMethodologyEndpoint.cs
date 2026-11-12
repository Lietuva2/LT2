using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Meta;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary><c>GET /api/v1/meta/methodology: how the two statistics are counted, as built</c>. Public, cached with an ETag (design 7.4).</summary>
internal static class GetMethodologyEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.Methodology), HandleAsync)
            .WithName("GetMethodology")
            .WithTags("Meta")
            .AllowAnonymous()
            .Produces<MethodologyResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static IResult HandleAsync(HttpContext http, ParliamentReads reads, ErrorMapper errors) =>
        PublicJson.From(http, errors, reads.Methodology());
}
