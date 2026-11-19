using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>POST /api/v1/moderation/records/{id}/reopen</c>: "Atšaukti uždarymą" (design 8.1 step 6). A moderator undoes a close made
/// by a moderator (rejected or withdrawn), never one made by the Seimas's vote: the record is open again with its live counts.
/// A statement is required; audited <c>record.reopened</c>; nobody is told.
/// </summary>
internal static class ReopenRecordEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.ReopenRecord), HandleAsync)
            .WithName("ReopenRecord")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static Task<IResult> HandleAsync(Guid id, StatementRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.ReopenRecordAsync(id, request.Statement, m, cancellationToken));
}
