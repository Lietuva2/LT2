using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>GET /api/v1/moderation/restrictions</c> (the restrictions that hold now) and
/// <c>POST /api/v1/moderation/restrictions/{id}/lift</c> (lift early, with a statement emailed to the person): a moderator
/// or an administrator in a staff-passkey session (design 8.3).
/// </summary>
internal static class RestrictionsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Restrictions), async (IAppealsModeration moderation, CancellationToken cancellationToken) =>
                TypedResults.Ok((await moderation.RestrictionsAsync(cancellationToken)).Select(AppealMapping.Map).ToList()))
            .WithName("GetRestrictions")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<IReadOnlyList<RestrictionResponse>>();

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.LiftRestriction), LiftAsync)
            .WithName("LiftRestriction")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static Task<IResult> LiftAsync(Guid id, StatementRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.LiftRestrictionAsync(id, request.Statement, m, ModeratorEndpoint.Contact(context), cancellationToken));
}
