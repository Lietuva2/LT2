using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>POST /api/v1/moderation/records/{id}/close</c>: "Uždaryti: projektas atmestas ar atsiimtas" (design 8.1 step 6, owner
/// 2026-11-19). A moderator closes an open record because the Seimas rejected the bill or its initiators withdrew it, with the
/// day, the source on lrs.lt and a statement: the counts freeze, the page shows the reason and the link, followers are told.
/// Audited <c>record.closed</c>; 409 <c>moderation.wrong-state</c> on a record that is not open.
/// </summary>
internal static class CloseRecordEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.CloseRecord), HandleAsync)
            .WithName("CloseRecord")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<CloseRecordRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static Task<IResult> HandleAsync(Guid id, CloseRecordRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.CloseRecordAsync(id, AppealMapping.ToModule(request), m, cancellationToken));
}
