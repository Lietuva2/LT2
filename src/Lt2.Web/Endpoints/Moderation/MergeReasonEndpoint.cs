using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary><c>POST /api/v1/moderation/reasons/{id}/merge</c>: merge an argument into a similar one; its "Man svarbu" marks move with it (design 8.3). Audited; the author is told why by email.</summary>
internal static class MergeReasonEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.MergeReason), HandleAsync)
            .WithName("MergeReason")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<MergeReasonRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static Task<IResult> HandleAsync(Guid id, MergeReasonRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.MergeReasonAsync(id, request.IntoReasonId, request.Statement, m, cancellationToken));
}
