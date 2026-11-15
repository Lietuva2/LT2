using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// Hidden bill records and arguments (design 8.3, 8.12.3): <c>GET /api/v1/moderation/hidden</c> lists them with who hid
/// them, when and why; <c>POST /api/v1/moderation/records/{id}/restore</c> and
/// <c>POST /api/v1/moderation/reasons/{id}/restore</c> ("Atkurti") bring one back, with a required statement, audited. An
/// argument's author is told by email; a record has no author to tell.
/// </summary>
internal static class HiddenEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Hidden), ListAsync)
            .WithName("GetHidden")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<HiddenItemsResponse>();

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.RestoreRecord), RestoreRecordAsync)
            .WithName("RestoreRecord")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.RestoreReason), RestoreReasonAsync)
            .WithName("RestoreReason")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(IAppealsModeration moderation, CancellationToken cancellationToken) =>
        TypedResults.Ok(AppealMapping.Map(await moderation.HiddenAsync(cancellationToken)));

    private static Task<IResult> RestoreRecordAsync(Guid id, StatementRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.RestoreRecordAsync(id, request.Statement, m, cancellationToken));

    private static Task<IResult> RestoreReasonAsync(Guid id, StatementRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.RestoreReasonAsync(id, request.Statement, m, cancellationToken));
}
