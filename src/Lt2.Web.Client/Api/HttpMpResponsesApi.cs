using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Mps;

namespace Lt2.Web.Client.Api;

/// <summary>The browser's <see cref="IMpResponsesApi"/> (design 8.5.5 to 8.5.7): plain calls to <c>/api/v1</c>.</summary>
internal sealed class HttpMpResponsesApi(ApiHttp http) : IMpResponsesApi
{
    public Task<ApiResult<BillMpResponsesResponse>> GetOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<BillMpResponsesResponse>(HttpMethod.Get, MpResponseRoutes.OnBillOf(number), null, cancellationToken);

    public Task<ApiResult<MpAnswersResponse>> GetOfMpAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpAnswersResponse>(HttpMethod.Get, MpResponseRoutes.OfMpOf(slug), null, cancellationToken);

    public Task<ApiResult<MyMpResponseResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<MyMpResponseResponse>(HttpMethod.Get, MpResponseRoutes.MineOf(number), null, cancellationToken);

    public Task<ApiResult<MpResponseSavedResponse>> SaveAsync(string number, MpResponseRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpResponseSavedResponse>(HttpMethod.Put, MpResponseRoutes.MineOf(number), request, cancellationToken);

    public Task<ApiResult> WithdrawAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, MpResponseRoutes.MineOf(number), null, cancellationToken);

    public Task<ApiResult<MpBillsResponse>> GetMyBillsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MpBillsResponse>(HttpMethod.Get, MpResponseRoutes.MyBills, null, cancellationToken);
}

/// <summary>The browser's <see cref="IMpResponseModerationApi"/>: moderators only; the server checks the staff session.</summary>
internal sealed class HttpMpResponseModerationApi(ApiHttp http) : IMpResponseModerationApi
{
    public Task<ApiResult<MpResponseModerationResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MpResponseModerationResponse>(HttpMethod.Get, MpResponseRoutes.Moderation, null, cancellationToken);

    public Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, MpResponseRoutes.With(MpResponseRoutes.Keep, id), request, cancellationToken);

    public Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, MpResponseRoutes.With(MpResponseRoutes.Hide, id), request, cancellationToken);

    public Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, MpResponseRoutes.With(MpResponseRoutes.Restore, id), request, cancellationToken);
}
