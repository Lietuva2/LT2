using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Digests;

namespace Lt2.Web.Client.Api;

/// <summary>The browser's <see cref="IDigestsApi"/> (design 8.8, 8.19): plain calls to <c>/api/v1</c>.</summary>
internal sealed class HttpDigestsApi(ApiHttp http) : IDigestsApi
{
    public Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<DigestRunResponse>>(HttpMethod.Get, DigestRoutes.Digests, null, cancellationToken);

    public Task<ApiResult<DigestRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<DigestRulesResponse>(HttpMethod.Get, DigestRoutes.Rules, null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<DigestRunResponse>>> GetDigestAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<DigestRunResponse>>(HttpMethod.Get, DigestRoutes.DigestOn(date), null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<RecordDigestResponse>>> GetRecordDigestsAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<RecordDigestResponse>>(HttpMethod.Get, DigestRoutes.OfRecordOn(number), null, cancellationToken);

    public Task<ApiResult<RecordNextDigestResponse>> GetRecordNextDigestAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<RecordNextDigestResponse>(HttpMethod.Get, DigestRoutes.NextOfRecordOn(number), null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<IntroductionLetterResponse>>> GetIntroductionAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<IntroductionLetterResponse>>(HttpMethod.Get, DigestRoutes.Introduction, null, cancellationToken);

    public Task<ApiResult<DigestModerationResponse>> GetModerationAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<DigestModerationResponse>(HttpMethod.Get, DigestRoutes.Moderation, null, cancellationToken);

    public Task<ApiResult<DigestRunResponse>> ApproveAsync(Guid runId, CancellationToken cancellationToken = default) =>
        http.SendAsync<DigestRunResponse>(HttpMethod.Post, DigestRoutes.ApproveOf(runId), new { }, cancellationToken);

    public Task<ApiResult<DigestRunResponse>> CancelAsync(Guid runId, CancelDigestRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<DigestRunResponse>(HttpMethod.Post, DigestRoutes.CancelOf(runId), request, cancellationToken);
}

/// <summary>The browser's <see cref="IMpMailApi"/>: the linked account's choice (design 8.19.4); the token pages are server-rendered.</summary>
internal sealed class HttpMpMailApi(ApiHttp http) : IMpMailApi
{
    public Task<ApiResult<MpMailChoiceResponse>> GetAsync(string token, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpMailChoiceResponse>(HttpMethod.Get, MpMailRoutes.TokenOf(token), null, cancellationToken);

    public Task<ApiResult<MpMailChoiceResponse>> SetFrequencyAsync(string token, MpMailFrequencyRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpMailChoiceResponse>(HttpMethod.Put, MpMailRoutes.TokenOf(token), request, cancellationToken);

    public Task<ApiResult> RenewAsync(string token, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, MpMailRoutes.RenewOf(token), new { }, cancellationToken);

    public Task<ApiResult<MpMailChoiceResponse>> AskExtraAsync(string token, MpMailExtraRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpMailChoiceResponse>(HttpMethod.Put, MpMailRoutes.ExtraOf(token), request, cancellationToken);

    public Task<ApiResult<MpMailChoiceResponse>> RemoveExtraAsync(string token, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpMailChoiceResponse>(HttpMethod.Delete, MpMailRoutes.ExtraOf(token), null, cancellationToken);

    public Task<ApiResult<MpMailChoiceResponse>> ConfirmExtraAsync(MpMailTokenRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpMailChoiceResponse>(HttpMethod.Post, MpMailRoutes.ConfirmExtra, request, cancellationToken);

    public Task<ApiResult> StopCopiesAsync(MpMailTokenRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, MpMailRoutes.StopCopies, request, cancellationToken);

    public Task<ApiResult<MpMailChoiceResponse>> GetMineAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MpMailChoiceResponse>(HttpMethod.Get, MpMailRoutes.Mine, null, cancellationToken);

    public Task<ApiResult<MpMailChoiceResponse>> SetMineAsync(MpMailFrequencyRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpMailChoiceResponse>(HttpMethod.Put, MpMailRoutes.Mine, request, cancellationToken);

    public Task<ApiResult<MpOfficeCopiesResponse>> GetOfficeCopiesAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MpOfficeCopiesResponse>(HttpMethod.Get, MpMailRoutes.OfficeCopies, null, cancellationToken);

    public Task<ApiResult<MpOfficeCopiesResponse>> SetOfficeCopiesAsync(MpMailFrequencyRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpOfficeCopiesResponse>(HttpMethod.Put, MpMailRoutes.OfficeCopies, request, cancellationToken);
}

/// <summary>The browser's <see cref="IMpSendingAdminApi"/> (design 8.19.5, 8.19.6).</summary>
internal sealed class HttpMpSendingAdminApi(ApiHttp http) : IMpSendingAdminApi
{
    public Task<ApiResult<IntroductionResponse>> GetIntroductionAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IntroductionResponse>(HttpMethod.Get, MpSendingAdminRoutes.Introduction, null, cancellationToken);

    public Task<ApiResult<IntroductionVersionResponse>> SaveIntroductionAsync(IntroductionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<IntroductionVersionResponse>(HttpMethod.Put, MpSendingAdminRoutes.Introduction, request, cancellationToken);

    public Task<ApiResult> SendIntroductionTestAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, MpSendingAdminRoutes.IntroductionTest, new { }, cancellationToken);

    public Task<ApiResult<IntroductionSentResponse>> SendIntroductionAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IntroductionSentResponse>(HttpMethod.Post, MpSendingAdminRoutes.IntroductionSend, new { }, cancellationToken);

    public Task<ApiResult<SuppressionResponse>> GetSuppressionAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<SuppressionResponse>(HttpMethod.Get, MpSendingAdminRoutes.Suppression, null, cancellationToken);

    public Task<ApiResult> SuppressAsync(SuppressRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, MpSendingAdminRoutes.Suppression, request, cancellationToken);

    public Task<ApiResult> UnsuppressAsync(UnsuppressRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, MpSendingAdminRoutes.Suppression, request, cancellationToken);
}
