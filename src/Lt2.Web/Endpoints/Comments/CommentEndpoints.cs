using System.Security.Claims;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Comments;

/// <summary>
/// Comments (design 8.23.11): <c>GET|POST /api/v1/comments/{kind}/{targetId}</c> (reading is public; writing a member's, under
/// <c>WriteComment</c>), <c>PUT|DELETE /api/v1/comments/{id}</c> (editing and deleting, under <c>WriteComment</c> too, 8.23.4), <c>PUT|DELETE
/// /api/v1/comments/{id}/agreement</c> ("Sutinku", the participation limit), <c>GET /api/v1/bills/{number}/comments/mine</c>
/// (never cached), and the moderators' <c>GET /api/v1/moderation/comments</c>, <c>…/{id}/hide|restore|keep</c>,
/// <c>…/{id}/writer</c>, <c>…/{id}/restrict-writer</c> and <c>GET /api/v1/moderation/bills/{number}/comments</c>. The terms
/// gate covers every write (8.22.2); none is exempt.
/// </summary>
internal static class CommentEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(CommentRoutes.Thread), async (HttpContext http, string kind, Guid targetId, int? limit, string? cursor, CommentReads reads, ErrorMapper errors, CancellationToken ct) =>
                PublicJson.From(http, errors, await reads.ThreadAsync(kind, targetId, limit, cursor, ct), maxAgeSeconds: 0))
            .WithName("GetComments").WithTags("Comments").AllowAnonymous()
            .Produces<CommentThreadResponse>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status404NotFound);

        // Where a shown comment stands, for a link to it (design 8.23.6): public, the same 404 for a hidden and an unknown one.
        api.MapGet(ApiEndpoints.Relative(CommentRoutes.Place), async (HttpContext http, Guid id, CommentReads reads, ErrorMapper errors, CancellationToken ct) =>
                PublicJson.From(http, errors, await reads.PlaceAsync(id, ct), maxAgeSeconds: 0))
            .WithName("GetCommentPlace").WithTags("Comments").AllowAnonymous()
            .Produces<CommentPlaceResponse>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(CommentRoutes.Thread), async (string kind, Guid targetId, CommentRequest request, ClaimsPrincipal user, CommentReads reads, ErrorMapper errors, CancellationToken ct) =>
            {
                var result = await reads.WriteAsync(user, kind, targetId, request, ct);
                return result.IsSuccess ? TypedResults.Created(CommentRoutes.OneOf(result.Value!.Id), result.Value) : errors.ToResult(result.Error!);
            })
            .WithName("WriteComment").WithTags("Comments").RequireAuthorization(Policies.Member)
            .WithValidation<CommentRequest>().RequireRateLimiting(RateLimitPolicies.WriteComment)
            .Produces<CommentCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete(ApiEndpoints.Relative(CommentRoutes.One), async (Guid id, ClaimsPrincipal user, CommentReads reads, ErrorMapper errors, CancellationToken ct) =>
                Done(await reads.DeleteAsync(user, id, ct), errors))
            .WithName("DeleteComment").WithTags("Comments").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.WriteComment)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        // The author edits it until someone says "Sutinku" (design 8.23.3, owner 2026-11-17), under WriteComment as writing is.
        api.MapPut(ApiEndpoints.Relative(CommentRoutes.One), async (Guid id, CommentRequest request, ClaimsPrincipal user, CommentReads reads, ErrorMapper errors, CancellationToken ct) =>
                Done(await reads.EditAsync(user, id, request, ct), errors))
            .WithName("EditComment").WithTags("Comments").RequireAuthorization(Policies.Member)
            .WithValidation<CommentRequest>().RequireRateLimiting(RateLimitPolicies.WriteComment)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPut(ApiEndpoints.Relative(CommentRoutes.Agreement), async (Guid id, ClaimsPrincipal user, IComments comments, ErrorMapper errors, CancellationToken ct) =>
                Done(await comments.AgreeAsync(user.UserId()!.Value, id, ct), errors))
            .WithName("AgreeWithComment").WithTags("Comments").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete(ApiEndpoints.Relative(CommentRoutes.Agreement), async (Guid id, ClaimsPrincipal user, IComments comments, ErrorMapper errors, CancellationToken ct) =>
                Done(await comments.RemoveAgreementAsync(user.UserId()!.Value, id, ct), errors))
            .WithName("RemoveCommentAgreement").WithTags("Comments").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.Participation)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(CommentRoutes.Mine), async (string number, ClaimsPrincipal user, CommentReads reads, ErrorMapper errors, CancellationToken ct) =>
                Ok(await reads.MineAsync(user, number, ct), errors))
            .WithName("GetMyComments").WithTags("Comments").RequireAuthorization(Policies.Member).RequireRateLimiting(RateLimitPolicies.OwnRead)
            .Produces<MyCommentsResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(CommentRoutes.Moderation), async (CommentReads reads, CancellationToken ct) => TypedResults.Ok(await reads.ModerationAsync(ct)))
            .WithName("GetCommentModeration").WithTags("Moderation").RequireAuthorization(Policies.Moderator).Produces<CommentModerationResponse>();

        api.MapGet(ApiEndpoints.Relative(CommentRoutes.OnBill), async (string number, CommentReads reads, ErrorMapper errors, CancellationToken ct) => Ok(await reads.OnBillAsync(number, ct), errors))
            .WithName("GetBillCommentModeration").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .Produces<IReadOnlyList<QueueCommentResponse>>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(CommentRoutes.Writer), async (Guid id, ICommentModeration moderation, ErrorMapper errors, CancellationToken ct) =>
            {
                var result = await moderation.WriterAsync(id, ct);
                return result.IsSuccess ? TypedResults.Ok(AppealMapping.Map(result.Value!)) : errors.ToResult(result.Error!);
            })
            .WithName("GetCommentWriter").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .Produces<AuthorViewResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(CommentRoutes.RestrictWriter), (Guid id, RestrictionRequest request, HttpContext context, ICommentModeration moderation, CancellationToken ct) =>
                ModeratorEndpoint.RestrictAsync(context, (m, contact) => moderation.RestrictWriterAsync(id, request.Until, request.Statement, m, contact, ct)))
            .WithName("RestrictCommentWriter").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .WithValidation<RestrictionRequest>()
            .Produces<RestrictionCreatedResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        Act<StatementRequest>(api, CommentRoutes.Hide, "HideComment", (m, id, r, a, c, ct) => m.HideAsync(id, r.Statement, a, c, ct));
        Act<StatementRequest>(api, CommentRoutes.Restore, "RestoreComment", (m, id, r, a, c, ct) => m.RestoreAsync(id, r.Statement, a, c, ct));
        Act<ModerationRequest>(api, CommentRoutes.Keep, "KeepComment", (m, id, r, a, _, ct) => m.KeepAsync(id, r.Statement, a, ct));
    }

    private delegate Task<Result<Unit>> Action<in T>(ICommentModeration moderation, Guid id, T request, StaffActor moderator, string? contact, CancellationToken ct);

    private static void Act<T>(IEndpointRouteBuilder api, string route, string name, Action<T> action) =>
        api.MapPost(ApiEndpoints.Relative(route), (Guid id, T request, HttpContext context, ICommentModeration moderation, CancellationToken ct) =>
                ModeratorEndpoint.ActAsync(context, m => action(moderation, id, request, m, ModeratorEndpoint.Contact(context), ct)))
            .WithName(name).WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .WithValidation<T>()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static IResult Ok<T>(Result<T> result, ErrorMapper errors) => result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);

    private static IResult Done(Result<Unit> result, ErrorMapper errors) => result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
}
