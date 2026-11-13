using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// Hidden appeals and reasons (design 8.3): <c>GET /api/v1/moderation/hidden</c> lists them with who hid them, when and
/// why; <c>POST /api/v1/moderation/appeals/{id}/restore</c> and <c>POST /api/v1/moderation/reasons/{id}/restore</c>
/// ("Atkurti") bring one back, with a required statement, audited, the author told.
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

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.RestoreAppeal), RestoreAppealAsync)
            .WithName("RestoreAppeal")
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

    private static Task<IResult> RestoreAppealAsync(
        Guid id, StatementRequest request, ClaimsPrincipal user, HttpContext context, Lt2.Identity.IIdentityModule identity,
        IAppealsModeration moderation, IpHasher ipHasher, ErrorMapper errors, CancellationToken cancellationToken) =>
        ActAsync(identity, user, context, ipHasher, errors, m => moderation.RestoreAppealAsync(id, request.Statement, m, cancellationToken), cancellationToken);

    private static Task<IResult> RestoreReasonAsync(
        Guid id, StatementRequest request, ClaimsPrincipal user, HttpContext context, Lt2.Identity.IIdentityModule identity,
        IAppealsModeration moderation, IpHasher ipHasher, ErrorMapper errors, CancellationToken cancellationToken) =>
        ActAsync(identity, user, context, ipHasher, errors, m => moderation.RestoreReasonAsync(id, request.Statement, m, cancellationToken), cancellationToken);

    private static async Task<IResult> ActAsync(
        Lt2.Identity.IIdentityModule identity, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, ErrorMapper errors,
        Func<ModeratorActor, Task<Result<Unit>>> action, CancellationToken cancellationToken)
    {
        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await action(moderator);
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
