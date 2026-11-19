using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;

namespace Lt2.Web.Client.Api;

/// <summary><see cref="IProfilesApi"/> over HTTP (design 8.25): the caller's profile, a member's page, reports, moderation.</summary>
internal sealed class HttpProfilesApi(ApiHttp http) : IProfilesApi
{
    public Task<ApiResult<MyProfileResponse>> GetMineAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MyProfileResponse>(HttpMethod.Get, ProfileRoutes.Mine, null, cancellationToken);

    public Task<ApiResult> AcceptConsentAsync(ProfileConsentRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ProfileRoutes.MineConsent, request, cancellationToken);

    public Task<ApiResult<MyProfileResponse>> SaveAsync(ProfileRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MyProfileResponse>(HttpMethod.Put, ProfileRoutes.Mine, request, cancellationToken);

    public Task<ApiResult<MemberProfileResponse>> GetMemberAsync(string id, CancellationToken cancellationToken = default) =>
        http.SendAsync<MemberProfileResponse>(HttpMethod.Get, ProfileRoutes.MemberOf(id), null, cancellationToken);

    public Task<ApiResult<MemberTextsResponse>> GetTextsAsync(string id, int shown, CancellationToken cancellationToken = default) =>
        http.SendAsync<MemberTextsResponse>(HttpMethod.Get, ProfileRoutes.TextsOf(id, shown), null, cancellationToken);

    public Task<ApiResult> ReportAsync(string id, MemberReportRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ProfileRoutes.ReportOf(id), request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<ProfileModerationResponse>>> GetModerationAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<ProfileModerationResponse>>(HttpMethod.Get, ProfileRoutes.Moderation, null, cancellationToken);

    public Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ProfileRoutes.ActionOf(id, "hide"), request, cancellationToken);

    public Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ProfileRoutes.ActionOf(id, "keep"), request, cancellationToken);

    public Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ProfileRoutes.ActionOf(id, "restore"), request, cancellationToken);
}
