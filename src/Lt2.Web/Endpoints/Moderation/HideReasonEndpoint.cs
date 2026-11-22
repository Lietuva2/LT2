using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary><c>POST /api/v1/moderation/reasons/{id}/hide</c>: hide an argument (design 8.3). Audited; the author is told why by email.</summary>
internal static class HideReasonEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.HideReason), HandleAsync)
            .WithName("HideReason")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<StatementRequest, StatementToAuthorRequestValidator>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static Task<IResult> HandleAsync(Guid id, StatementRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.HideReasonAsync(id, request.Statement, m, cancellationToken));
}
