using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Feedback;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Feedback;

/// <summary>
/// Design 8.10: <c>POST /api/v1/feedback</c> (a member; ten a day, counted in memory) and, for moderators and
/// administrators in a staff-passkey session, <c>GET /api/v1/moderation/feedback</c>, <c>PUT …/{id}</c> (status, note)
/// and <c>POST …/{id}/reply</c> (linked items only, through the outbox). No item text is logged.
/// </summary>
internal static class FeedbackEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost(ApiEndpoints.Relative(FeedbackRoutes.Send), async (FeedbackRequest request, ClaimsPrincipal user, IFeedback feedback, FeedbackLimiter limiter, ErrorMapper errors, CancellationToken cancellationToken) =>
            {
                var result = await ServerFeedbackApi.SendAsync(feedback, limiter, user.UserId()!.Value, request, cancellationToken);
                return result.IsSuccess ? Results.NoContent() : errors.ToResult(result.Error!);
            })
            .WithName("SendFeedback")
            .WithTags("Feedback")
            .RequireAuthorization(Policies.Member)
            .WithValidation<FeedbackRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(FeedbackRoutes.Staff), async (string? kind, string? status, IFeedback feedback, CancellationToken cancellationToken) =>
                TypedResults.Ok((await feedback.ListAsync(kind, status, cancellationToken)).Select(ServerFeedbackApi.Map).ToList()))
            .WithName("ListFeedback")
            .WithTags("Feedback")
            .RequireAuthorization(Policies.Moderator)
            .Produces<IReadOnlyList<FeedbackItemResponse>>();

        api.MapPut(ApiEndpoints.Relative(FeedbackRoutes.Item), async (Guid id, FeedbackUpdateRequest request, IFeedback feedback, ErrorMapper errors, CancellationToken cancellationToken) =>
            {
                var result = await feedback.UpdateAsync(id, request.Status, request.Note, cancellationToken);
                return result.IsSuccess ? Results.NoContent() : errors.ToResult(result.Error!);
            })
            .WithName("UpdateFeedback")
            .WithTags("Feedback")
            .RequireAuthorization(Policies.Moderator)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(FeedbackRoutes.Reply), async (Guid id, FeedbackReplyRequest request, ClaimsPrincipal user, HttpContext context, IIdentityModule identity, IFeedback feedback, IpHasher ipHasher, ErrorMapper errors, CancellationToken cancellationToken) =>
            {
                var staff = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
                var result = await feedback.ReplyAsync(id, request.Text, staff.UserId, staff.Role, staff.IpHash, cancellationToken);
                return result.IsSuccess ? Results.NoContent() : errors.ToResult(result.Error!);
            })
            .WithName("ReplyToFeedback")
            .WithTags("Feedback")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<FeedbackReplyRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}