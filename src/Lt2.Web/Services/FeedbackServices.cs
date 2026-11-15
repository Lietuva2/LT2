using System.Threading.RateLimiting;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Feedback;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>
/// Ten feedback items a day per account (design 8.10), counted in this process's memory only: no table holds the count,
/// so an unlinked item gets no link to its author through it. Shared by the API endpoint and the server-rendered form.
/// </summary>
internal sealed class FeedbackLimiter : IDisposable
{
    public const int PerDay = 10;

    private readonly PartitionedRateLimiter<Guid> _limiter = PartitionedRateLimiter.Create<Guid, Guid>(user =>
        RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PerDay,
            Window = TimeSpan.FromDays(1),
            QueueLimit = 0,
        }));

    public bool TryTake(Guid userId)
    {
        using var lease = _limiter.AttemptAcquire(userId);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}

/// <summary>Server-side <see cref="IFeedbackApi"/>: the server-rendered form and the prerendered staff page call the module in-process.</summary>
internal sealed class ServerFeedbackApi(IFeedback feedback, FeedbackLimiter limiter, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors) : IFeedbackApi
{
    public async Task<ApiResult> SendAsync(FeedbackRequest request, CancellationToken cancellationToken = default) =>
        accessor.HttpContext?.User.UserId() is { } userId
            ? errors.ToApiResult(await SendAsync(feedback, limiter, userId, request, cancellationToken))
            : ApiResult.Fail(errors.ToProblem(Error.Unauthorized("auth.required")));

    public async Task<ApiResult<IReadOnlyList<FeedbackItemResponse>>> ListAsync(string? kind = null, string? status = null, CancellationToken cancellationToken = default) =>
        await IsStaffAsync()
            ? ApiResult<IReadOnlyList<FeedbackItemResponse>>.Ok((await feedback.ListAsync(kind, status, cancellationToken)).Select(Map).ToList())
            : ApiResult<IReadOnlyList<FeedbackItemResponse>>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> UpdateAsync(Guid id, FeedbackUpdateRequest request, CancellationToken cancellationToken = default) =>
        ServerStaff.ActAsync(accessor, Identity, ipHasher, errors, staff => feedback.UpdateAsync(id, request.Status, request.Note, staff, cancellationToken));

    public Task<ApiResult> ReplyAsync(Guid id, FeedbackReplyRequest request, CancellationToken cancellationToken = default) =>
        ServerStaff.ActAsync(accessor, Identity, ipHasher, errors, staff => feedback.ReplyAsync(id, request.Text, staff, cancellationToken));

    /// <summary>The daily limit, then the module. Used by the endpoint and the form alike.</summary>
    public static async Task<Result<Unit>> SendAsync(IFeedback feedback, FeedbackLimiter limiter, Guid userId, FeedbackRequest request, CancellationToken ct)
    {
        if (!limiter.TryTake(userId))
        {
            return Error.Conflict("feedback.limit");
        }

        return await feedback.SendAsync(userId, new NewFeedback(request.Text, request.Kind, request.PagePath, request.Browser, request.MayReply), ct);
    }

    public static FeedbackItemResponse Map(FeedbackInfo f) => new(
        f.Id, f.Kind, f.Text, f.Status, f.Note, f.PagePath, f.Browser, f.CreatedOn, f.Linked, f.DisplayName,
        f.Replies.Select(r => new FeedbackReplyResponse(r.At, r.Text)).ToList());

    /// <summary>Null without a request; <see cref="ServerStaff.ActAsync"/> refuses then before using it.</summary>
    private IIdentityModule Identity => accessor.HttpContext?.RequestServices.GetRequiredService<IIdentityModule>()!;

    private async Task<bool> IsStaffAsync()
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            return false;
        }

        var authorization = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, http, Policies.Moderator)).Succeeded;
    }
}
