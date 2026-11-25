using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>GET /api/v1/moderation/queue</c>: what waits for a moderator (design 8.3). <c>?list=records</c> or <c>?list=reasons</c>
/// reads only that list, as its tab does (the other is empty with a total of 0); without it, both. Holds nobody's position.
/// <c>GET /api/v1/moderation/counts</c>: how many items each list of the tabs holds, numbers only.
/// </summary>
internal static class GetModerationQueueEndpoint
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.ModerationQueue), HandleAsync)
            .WithName("GetModerationQueue")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<ModerationQueueResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        api.MapGet(ApiEndpoints.Relative(AppealRoutes.ModerationCounts), CountsAsync)
            .WithName("GetModerationCounts")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<ModerationCountsResponse>();
    }

    private static async Task<IResult> HandleAsync([FromQuery] string? list, IAppealsModeration moderation, ErrorMapper errors, CancellationToken cancellationToken) =>
        list is null or QueueLists.Records or QueueLists.Reasons
            ? TypedResults.Ok(AppealMapping.Map(await moderation.QueueAsync(list is null or QueueLists.Records, list is null or QueueLists.Reasons, cancellationToken)))
            : errors.ToResult(Lt2.SharedKernel.Error.Validation("queue.list-invalid", "list"));

    private static async Task<IResult> CountsAsync(ModerationCountReads counts, CancellationToken cancellationToken) =>
        TypedResults.Ok(await counts.CountsAsync(cancellationToken));
}
