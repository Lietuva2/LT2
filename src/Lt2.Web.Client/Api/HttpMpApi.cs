using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Mps;

namespace Lt2.Web.Client.Api;

/// <summary>The browser's <see cref="IMpApi"/> (design 8.5): plain calls to <c>/api/v1</c>; tokens go in request bodies.</summary>
internal sealed class HttpMpApi(ApiHttp http) : IMpApi
{
    public Task<ApiResult> ClaimAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, MpRoutes.ClaimOf(slug), new { }, cancellationToken);

    public Task<ApiResult<MpLinkResponse>> TakeOverAsync(MpTakeoverRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpLinkResponse>(HttpMethod.Post, MpRoutes.Takeover, request, cancellationToken);

    public Task<ApiResult> EndAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, MpRoutes.Mine, null, cancellationToken);

    public Task<ApiResult<MpOfficeResponse>> GetOfficeAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MpOfficeResponse>(HttpMethod.Get, MpRoutes.Office, null, cancellationToken);

    public Task<ApiResult<MpOfficeInvitationResponse>> InviteOfficeMemberAsync(MpOfficeInvitationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpOfficeInvitationResponse>(HttpMethod.Post, MpRoutes.OfficeInvitations, request, cancellationToken);

    public Task<ApiResult> RemoveOfficeMemberAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, MpRoutes.OfficeMemberOf(memberId), null, cancellationToken);

    public Task<ApiResult<MpLinkResponse>> AcceptOfficeInvitationAsync(MpOfficeAcceptanceRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<MpLinkResponse>(HttpMethod.Post, MpRoutes.OfficeAcceptance, request, cancellationToken);
}
