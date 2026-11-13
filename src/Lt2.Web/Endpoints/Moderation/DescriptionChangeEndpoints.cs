using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// A proposed description change (design 8.1): <c>POST /api/v1/moderation/description-changes/{id}/approve</c> (optional
/// note) puts it on the page and in the history; <c>…/return</c> (required note) keeps the approved text. Audited; the
/// initiator is told.
/// </summary>
internal static class DescriptionChangeEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.ApproveDescription), ApproveAsync)
            .WithName("ApproveDescription")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.ReturnDescription), ReturnAsync)
            .WithName("ReturnDescription")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id, ModerationRequest request, ClaimsPrincipal user, HttpContext context, Lt2.Identity.IIdentityModule identity,
        IAppealsModeration moderation, IpHasher ipHasher, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await moderation.ApproveDescriptionAsync(id, request.Statement, moderator, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> ReturnAsync(
        Guid id, StatementRequest request, ClaimsPrincipal user, HttpContext context, Lt2.Identity.IIdentityModule identity,
        IAppealsModeration moderation, IpHasher ipHasher, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await moderation.ReturnDescriptionAsync(id, request.Statement, moderator, cancellationToken);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
