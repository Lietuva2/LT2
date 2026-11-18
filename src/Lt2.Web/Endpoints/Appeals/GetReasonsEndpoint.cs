using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/bills/{number}/record/reasons?side=&amp;cursor=&amp;limit=</c>: one page of a side's published arguments
/// (design 8.12.1, owner 2026-11-19), public and the same for everyone, in the page's order (the counted "Man svarbu" marks, most
/// first, then the oldest first). The bill's answer carries each side's first two; "Rodyti daugiau" reads the rest from here.
/// 400 <c>side.invalid</c> for another side, 404 <c>record.not-found</c> for a bill without a shown record.
/// </summary>
internal static class GetReasonsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Reasons), HandleAsync)
            .WithName("GetReasons")
            .WithTags("Appeals")
            .AllowAnonymous()
            .Produces<ReasonPageResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        HttpContext http, IAppealsModule appeals, ErrorMapper errors, string number, string? side, string? cursor, string? limit, CancellationToken cancellationToken)
    {
        // A limit the API would not take falls back to the default or the nearest one allowed (ReasonLimits), never to an error.
        var asked = int.TryParse(limit, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
        var result = await appeals.ReasonsAsync(number, side ?? "", cursor, asked, cancellationToken);
        return result.IsSuccess
            ? PublicJson.From(http, errors, Lt2.SharedKernel.Result<ReasonPageResponse>.Success(AppealMapping.Map(result.Value)), maxAgeSeconds: 0)
            : errors.ToResult(result.Error!);
    }
}
