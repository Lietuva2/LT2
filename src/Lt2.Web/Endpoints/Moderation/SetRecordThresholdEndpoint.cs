using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary><c>PUT /api/v1/moderation/records/{id}/threshold</c>: a moderator sets the bill's own threshold, shown with the statement as its reason (design 8.12.2). Audited.</summary>
internal static class SetRecordThresholdEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.RecordThreshold), HandleAsync)
            .WithName("SetRecordThreshold")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<ModeratorThresholdRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static Task<IResult> HandleAsync(Guid id, ModeratorThresholdRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.SetThresholdAsync(id, request.Threshold, request.Statement, m, cancellationToken));
}
