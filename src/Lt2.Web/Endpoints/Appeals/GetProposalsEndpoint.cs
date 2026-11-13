using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Appeals;

/// <summary>
/// <c>GET /api/v1/bills/{number}/appeal/proposals</c>: how many proposals wait for a moderator on the bill (design 8.1).
/// The number only, the same for everyone and for no one in particular, so it is public and cacheable; nothing about the
/// person asking goes into it.
/// </summary>
internal static class GetProposalsEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Proposals), HandleAsync)
            .WithName("GetProposals")
            .WithTags("Appeals")
            .AllowAnonymous()
            .Produces<ProposalsResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(HttpContext http, string number, IAppealsModule appeals, ErrorMapper errors, CancellationToken ct)
    {
        var result = await appeals.ProposalsWaitingAsync(number, ct);
        return result.IsSuccess
            ? PublicJson.From(http, errors, Result<ProposalsResponse>.Success(new ProposalsResponse(result.Value)), maxAgeSeconds: 0)
            : errors.ToResult(result.Error!);
    }
}
