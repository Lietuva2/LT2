using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;

namespace Lt2.Web.Client.Api;

/// <summary>The browser's <see cref="ICommentsApi"/> (design 8.23.11): plain calls to <c>/api/v1</c>.</summary>
internal sealed class HttpCommentsApi(ApiHttp http) : ICommentsApi
{
    public Task<ApiResult<CommentThreadResponse>> GetThreadAsync(string kind, Guid targetId, int? shown = null, CancellationToken cancellationToken = default) =>
        http.SendAsync<CommentThreadResponse>(HttpMethod.Get, CommentRoutes.ThreadOf(kind, targetId, shown), null, cancellationToken);

    public Task<ApiResult<CommentPlaceResponse>> GetPlaceAsync(Guid commentId, CancellationToken cancellationToken = default) =>
        http.SendAsync<CommentPlaceResponse>(HttpMethod.Get, CommentRoutes.PlaceOf(commentId), null, cancellationToken);

    public Task<ApiResult<MyCommentsResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<MyCommentsResponse>(HttpMethod.Get, CommentRoutes.MineOf(number), null, cancellationToken);

    public Task<ApiResult<CommentCreatedResponse>> WriteAsync(string kind, Guid targetId, CommentRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<CommentCreatedResponse>(HttpMethod.Post, CommentRoutes.ThreadOf(kind, targetId), request, cancellationToken);

    public Task<ApiResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, CommentRoutes.OneOf(id), null, cancellationToken);

    public Task<ApiResult> EditAsync(Guid id, CommentRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, CommentRoutes.OneOf(id), request, cancellationToken);

    public Task<ApiResult> AgreeAsync(Guid id, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, CommentRoutes.AgreementOf(id), null, cancellationToken);

    public Task<ApiResult> RemoveAgreementAsync(Guid id, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, CommentRoutes.AgreementOf(id), null, cancellationToken);
}

/// <summary>The browser's <see cref="ICommentModerationApi"/>: moderators only; the server checks the staff session.</summary>
internal sealed class HttpCommentModerationApi(ApiHttp http) : ICommentModerationApi
{
    public Task<ApiResult<CommentModerationResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<CommentModerationResponse>(HttpMethod.Get, CommentRoutes.Moderation, null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<QueueCommentResponse>>> GetOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<QueueCommentResponse>>(HttpMethod.Get, CommentRoutes.OnBillOf(number), null, cancellationToken);

    public Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, CommentRoutes.With(CommentRoutes.Hide, id), request, cancellationToken);

    public Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, CommentRoutes.With(CommentRoutes.Restore, id), request, cancellationToken);

    public Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, CommentRoutes.With(CommentRoutes.Keep, id), request, cancellationToken);

    public Task<ApiResult<AuthorViewResponse>> GetWriterAsync(Guid id, CancellationToken cancellationToken = default) =>
        http.SendAsync<AuthorViewResponse>(HttpMethod.Get, CommentRoutes.With(CommentRoutes.Writer, id), null, cancellationToken);

    public Task<ApiResult<RestrictionCreatedResponse>> RestrictWriterAsync(Guid id, RestrictionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<RestrictionCreatedResponse>(HttpMethod.Post, CommentRoutes.With(CommentRoutes.RestrictWriter, id), request, cancellationToken);
}
