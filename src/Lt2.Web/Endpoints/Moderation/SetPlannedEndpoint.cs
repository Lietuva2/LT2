using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary><c>PUT /api/v1/moderation/records/{id}/planned</c>: a moderator corrects the planned date or stage of the bill (design 8.4); it takes precedence over the agenda pages and closes the open date flags. Audited.</summary>
internal static class SetPlannedEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(AppealRoutes.RecordPlanned), HandleAsync)
            .WithName("SetPlanned")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<ModeratorPlannedRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static Task<IResult> HandleAsync(Guid id, ModeratorPlannedRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.SetPlannedAsync(id, request.Date, request.Stage, request.Statement, m, cancellationToken));
}
