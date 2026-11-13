using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/bills/{number}/appeal/history</c>: the public edit history of the appeal's neutral description (design
/// 8.2). For a published appeal; who edited is not shown.
/// </summary>
internal static class GetAppealHistoryEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.History), HandleAsync)
            .WithName("GetAppealHistory")
            .WithTags("Appeals")
            .AllowAnonymous()
            .Produces<IReadOnlyList<DescriptionRevisionResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, string number, IAppealsModule appeals, ErrorMapper errors, CancellationToken ct)
    {
        var result = await appeals.HistoryAsync(number, ct);
        return result.IsSuccess
            ? PublicJson.From(http, errors, Result<List<DescriptionRevisionResponse>>.Success(result.Value.Select(r => new DescriptionRevisionResponse(r.ChangedAt, r.OldText, r.NewText)).ToList()), maxAgeSeconds: 0)
            : errors.ToResult(result.Error!);
    }
}