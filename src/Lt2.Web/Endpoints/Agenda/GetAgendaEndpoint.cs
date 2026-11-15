using Lt2.Api.Contracts.Agenda;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Agenda;

/// <summary>
/// <c>GET /api/v1/agenda</c> (design 8.11.1): the coming planned items by day and the last seven days' stage results, each
/// with its bill's public record numbers or how many ask for one. Public and cacheable with an ETag: nothing about the
/// caller is in it.
/// </summary>
internal static class GetAgendaEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AgendaRoutes.Agenda), HandleAsync)
            .WithName("GetAgenda")
            .WithTags("Agenda")
            .AllowAnonymous()
            .Produces<AgendaResponse>()
            .Produces(StatusCodes.Status304NotModified);

    private static async Task<IResult> HandleAsync(HttpContext http, AgendaReads reads, ErrorMapper errors, CancellationToken ct) =>
        PublicJson.From(http, errors, Result<AgendaResponse>.Success(await reads.ReadAsync(ct)), maxAgeSeconds: 0);
}
