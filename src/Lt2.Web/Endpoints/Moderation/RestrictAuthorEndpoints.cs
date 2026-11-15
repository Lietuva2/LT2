using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>POST /api/v1/moderation/reasons/{id}/restrict-author</c>: a moderator or an administrator restricts the argument's
/// author from writing until a day or until lifted (design 8.3), in a staff-passkey session; the statement is required
/// and emailed with the contact address; audited.
/// </summary>
internal static class RestrictAuthorEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.RestrictReasonAuthor), HandleAsync)
            .WithName("RestrictReasonAuthor")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<RestrictionRequest>()
            .Produces<RestrictionCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static Task<IResult> HandleAsync(Guid id, RestrictionRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.RestrictAsync(context, (m, contact) => moderation.RestrictAuthorOfReasonAsync(id, request.Until, request.Statement, m, contact, cancellationToken));
}
