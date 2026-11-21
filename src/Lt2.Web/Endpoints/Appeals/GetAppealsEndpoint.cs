using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/appeals?q=&amp;planned=&amp;tab=&amp;sort=&amp;cursor=&amp;limit=</c>: active bills with both sides' counts and their
/// arguments, by activity (the default), participants, arguments or planned date; a search by number or title, those
/// planned soon, or the closed tab (design 8.12.5); <c>limit</c> of them (20 by default, at most 200: <see cref="BillListLimits"/>).
/// Public and the same for everyone. A row whose threshold is reached carries the summary it names (design 8.12.2,
/// <see cref="RowSummaries"/>).
/// </summary>
internal static class GetAppealsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Appeals), HandleAsync)
            .WithName("GetAppeals")
            .WithTags("Appeals")
            .AllowAnonymous()
            .Produces<BillListResponse>()
            .Produces(StatusCodes.Status304NotModified);

    private static async Task<IResult> HandleAsync(
        HttpContext http, RowSummaries list, ErrorMapper errors, string? q, bool? planned, string? tab, string? sort, string? cursor, string? limit, CancellationToken cancellationToken) =>
        PublicJson.From(
            http,
            errors,
            Lt2.SharedKernel.Result<BillListResponse>.Success(await list.ListAsync(
                new BillListQuery(q, planned == true, tab == "closed", cursor, sort, BillListLimits.Of(limit)), cancellationToken)),
            maxAgeSeconds: 0);
}
