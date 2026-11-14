using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Organisations;

namespace Lt2.Web.Client.Api;

/// <summary>The browser's <see cref="IOrganisationsApi"/>: plain HTTP calls to <c>/api/v1</c> (design 8.14.14).</summary>
internal sealed class HttpOrganisationsApi(ApiHttp http) : IOrganisationsApi
{
    public Task<ApiResult<OrganisationListResponse>> GetOrganisationsAsync(string? search = null, CancellationToken cancellationToken = default) =>
        http.SendAsync<OrganisationListResponse>(HttpMethod.Get, OrganisationRoutes.List(search), null, cancellationToken);

    public Task<ApiResult<OrganisationProfileResponse>> GetProfileAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync<OrganisationProfileResponse>(HttpMethod.Get, OrganisationRoutes.Of(slug), null, cancellationToken);

    public Task<ApiResult<BillStatementsResponse>> GetOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<BillStatementsResponse>(HttpMethod.Get, OrganisationRoutes.OnBillOf(number), null, cancellationToken);

    public Task<ApiResult<OrganisationCreatedResponse>> RegisterAsync(OrganisationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<OrganisationCreatedResponse>(HttpMethod.Post, OrganisationRoutes.Organisations, request, cancellationToken);

    public Task<ApiResult> ChangeAsync(string slug, OrganisationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, OrganisationRoutes.Of(slug), request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<MyOrganisationResponse>>> GetMineAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<MyOrganisationResponse>>(HttpMethod.Get, OrganisationRoutes.Mine, null, cancellationToken);

    public Task<ApiResult<OrganisationManagementResponse>> GetManagementAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync<OrganisationManagementResponse>(HttpMethod.Get, OrganisationRoutes.ManagementOf(slug), null, cancellationToken);

    public Task<ApiResult> SendCodeAsync(string slug, ConfirmationCodeRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.ConfirmationCodeOf(slug), request, cancellationToken);

    public Task<ApiResult> ConfirmAsync(string slug, ConfirmationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.ConfirmationOf(slug), request, cancellationToken);

    public Task<ApiResult<InviteCreatedResponse>> CreateInviteAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync<InviteCreatedResponse>(HttpMethod.Post, OrganisationRoutes.InvitesOf(slug), new { }, cancellationToken);

    public Task<ApiResult<InviteResponse>> GetInviteAsync(string token, CancellationToken cancellationToken = default) =>
        http.SendAsync<InviteResponse>(HttpMethod.Get, OrganisationRoutes.InviteOf(token), null, cancellationToken);

    public Task<ApiResult<OrganisationCreatedResponse>> AcceptInviteAsync(string token, CancellationToken cancellationToken = default) =>
        http.SendAsync<OrganisationCreatedResponse>(HttpMethod.Post, OrganisationRoutes.AcceptInviteOf(token), new { }, cancellationToken);

    public Task<ApiResult> RemoveRepresentativeAsync(string slug, Guid representativeId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, OrganisationRoutes.RepresentativeOf(slug, representativeId), null, cancellationToken);

    public Task<ApiResult> LeaveAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.LeaveOf(slug), new { }, cancellationToken);

    public Task<ApiResult> CloseAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.CloseOf(slug), new { }, cancellationToken);

    public Task<ApiResult<IReadOnlyList<MyBillOrganisationResponse>>> GetMineOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<MyBillOrganisationResponse>>(HttpMethod.Get, OrganisationRoutes.MineOnBillOf(number), null, cancellationToken);

    public Task<ApiResult<StatementSavedResponse>> SaveStatementAsync(string number, string slug, OrganisationStatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<StatementSavedResponse>(HttpMethod.Put, OrganisationRoutes.StatementOf(number, slug), request, cancellationToken);

    public Task<ApiResult> WithdrawStatementAsync(string number, string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, OrganisationRoutes.StatementOf(number, slug), null, cancellationToken);

    public Task<ApiResult<OrganisationInvitationResponse>> GetInvitationAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync<OrganisationInvitationResponse>(HttpMethod.Get, OrganisationRoutes.InvitationOf(slug), null, cancellationToken);

    public Task<ApiResult<OrganisationInvitationResponse>> ReplaceInvitationAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync<OrganisationInvitationResponse>(HttpMethod.Post, OrganisationRoutes.ReplaceInvitationOf(slug), new { }, cancellationToken);
}

/// <summary>The browser's <see cref="IOrganisationModerationApi"/>.</summary>
internal sealed class HttpOrganisationModerationApi(ApiHttp http) : IOrganisationModerationApi
{
    public Task<ApiResult<OrganisationModerationResponse>> GetQueueAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<OrganisationModerationResponse>(HttpMethod.Get, OrganisationRoutes.ModerationQueue, null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<QueueStatementResponse>>> GetOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<QueueStatementResponse>>(HttpMethod.Get, OrganisationRoutes.ModerationBillOf(number), null, cancellationToken);

    public Task<ApiResult<HiddenOrganisationsResponse>> GetHiddenAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<HiddenOrganisationsResponse>(HttpMethod.Get, OrganisationRoutes.ModerationHidden, null, cancellationToken);

    public Task<ApiResult> KeepAsync(Guid organisationId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.Keep, organisationId), request, cancellationToken);

    public Task<ApiResult> BlockAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.Block, organisationId), request, cancellationToken);

    public Task<ApiResult> UnblockAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.Unblock, organisationId), request, cancellationToken);

    public Task<ApiResult> ConfirmAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.Confirm, organisationId), request, cancellationToken);

    public Task<ApiResult> UnconfirmAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.Unconfirm, organisationId), request, cancellationToken);

    public Task<ApiResult> PublishStatementAsync(Guid statementId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.PublishStatement, statementId), request, cancellationToken);

    public Task<ApiResult> HideStatementAsync(Guid statementId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.HideStatement, statementId), request, cancellationToken);

    public Task<ApiResult> RestoreStatementAsync(Guid statementId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.RestoreStatement, statementId), request, cancellationToken);

    public Task<ApiResult<AuthorViewResponse>> GetWriterAsync(Guid statementId, CancellationToken cancellationToken = default) =>
        http.SendAsync<AuthorViewResponse>(HttpMethod.Get, OrganisationRoutes.With(OrganisationRoutes.StatementWriter, statementId), null, cancellationToken);

    public Task<ApiResult<RestrictionCreatedResponse>> RestrictWriterAsync(Guid statementId, RestrictionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<RestrictionCreatedResponse>(HttpMethod.Post, OrganisationRoutes.With(OrganisationRoutes.RestrictStatementWriter, statementId), request, cancellationToken);
}
