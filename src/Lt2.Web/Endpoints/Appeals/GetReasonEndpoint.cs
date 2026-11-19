using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/reasons/{id}</c>: one published argument and its bill, public (design 8.12.1): the bill's page shows a linked
/// argument (<c>?argumentas=</c>) first in its side's column, and the island the one just published, wherever their pages
/// would put them. 404 <c>reason.not-found</c>, the same for a waiting, hidden, merged, withdrawn and unknown argument and for one
/// on a hidden record, so nothing tells them apart.
/// </summary>
internal static class GetReasonEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Reason), HandleAsync)
            .WithName("GetReason")
            .WithTags("Appeals")
            .AllowAnonymous()
            .Produces<ReasonOnBillResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        HttpContext http, IAppealsModule appeals, ErrorMapper errors, [Microsoft.AspNetCore.Mvc.FromRoute(Name = "id")] Guid reasonId, CancellationToken cancellationToken)
    {
        var result = await appeals.ReasonAsync(reasonId, cancellationToken);
        return result.IsSuccess
            ? PublicJson.From(http, errors, Lt2.SharedKernel.Result<ReasonOnBillResponse>.Success(AppealMapping.Map(result.Value)), maxAgeSeconds: 0)
            : errors.ToResult(result.Error!);
    }
}
