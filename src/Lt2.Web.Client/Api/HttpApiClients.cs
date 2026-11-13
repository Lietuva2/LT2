using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;

namespace Lt2.Web.Client.Api;

internal sealed class HttpAuthApi(ApiHttp http) : IAuthApi
{
    public Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.Register, request, cancellationToken);

    public Task<ApiResult> RequestCodeAsync(RequestCodeRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.Code, request, cancellationToken);

    public Task<ApiResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.SignIn, request, cancellationToken);

    public Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.SignOut, null, cancellationToken);

    public Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default) =>
        http.SendForJsonTextAsync(HttpMethod.Post, ApiRoutes.Auth.PasskeyCreationOptions, cancellationToken);

    public Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.Passkeys, request, cancellationToken);

    public Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) =>
        http.SendForJsonTextAsync(HttpMethod.Post, ApiRoutes.Auth.PasskeyRequestOptions, cancellationToken);

    public Task<ApiResult> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.PasskeySignIn, request, cancellationToken);
}

internal sealed class HttpMeApi(ApiHttp http) : IMeApi
{
    public Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MeResponse>(HttpMethod.Get, ApiRoutes.Me.Root, null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<SessionResponse>>(HttpMethod.Get, ApiRoutes.Me.Sessions, null, cancellationToken);

    public Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, ApiRoutes.Me.SessionById(sessionId), null, cancellationToken);

    public Task<ApiResult> CompleteRegistrationAsync(CompleteRegistrationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, request, cancellationToken);

    public Task<ApiResult> ActivateRoleAsync(RoleActivationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Me.RoleActivation, request, cancellationToken);

    public Task<ApiResult> GivePositionConsentAsync(PositionConsentRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, request, cancellationToken);

    public Task<ApiResult> WithdrawPositionConsentAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PositionConsent, null, cancellationToken);

    public Task<ApiResult<PartySupportResponse>> GetPartySupportAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<PartySupportResponse>(HttpMethod.Get, ApiRoutes.Me.PartySupport, null, cancellationToken);

    public Task<ApiResult> SetPartySupportAsync(PartySupportRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ApiRoutes.Me.PartySupport, request, cancellationToken);

    public Task<ApiResult> DeleteAccountAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root, null, cancellationToken);

    public Task<ApiResult<MyInvitationResponse>> GetInvitationAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MyInvitationResponse>(HttpMethod.Get, ApiRoutes.Me.Invitation, null, cancellationToken);

    public Task<ApiResult<MyInvitationResponse>> ReplaceInvitationAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MyInvitationResponse>(HttpMethod.Post, ApiRoutes.Me.ReplaceInvitation, null, cancellationToken);
}

internal sealed class HttpAppealsApi(ApiHttp http) : IAppealsApi
{
    public Task<ApiResult<AppealRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<AppealRulesResponse>(HttpMethod.Get, AppealRoutes.Rules, null, cancellationToken);

    public Task<ApiResult<AppealListResponse>> GetAppealsAsync(string? sort = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        http.SendAsync<AppealListResponse>(HttpMethod.Get, AppealRoutes.AppealsList(sort, cursor), null, cancellationToken);

    public Task<ApiResult<AppealResponse>> GetAppealAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<AppealResponse>(HttpMethod.Get, AppealRoutes.AppealOf(number), null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<DescriptionRevisionResponse>>> GetHistoryAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<DescriptionRevisionResponse>>(HttpMethod.Get, AppealRoutes.HistoryOf(number), null, cancellationToken);

    public Task<ApiResult<MyAppealResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<MyAppealResponse>(HttpMethod.Get, AppealRoutes.MineOf(number), null, cancellationToken);

    public Task<ApiResult<ProposalsResponse>> GetProposalsAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<ProposalsResponse>(HttpMethod.Get, AppealRoutes.ProposalsOf(number), null, cancellationToken);

    public Task<ApiResult<AppealResponse>> OpenAsync(string number, OpenAppealRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<AppealResponse>(HttpMethod.Post, AppealRoutes.AppealOf(number), request, cancellationToken);

    public Task<ApiResult> EditAsync(string number, EditAppealRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.AppealOf(number), request, cancellationToken);

    public Task<ApiResult> ProposeDescriptionAsync(string number, EditAppealRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.DescriptionChangeOf(number), request, cancellationToken);

    public Task<ApiResult> ChangeThresholdAsync(string number, ThresholdRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.ThresholdOf(number), request, cancellationToken);

    public Task<ApiResult> TakePositionAsync(string number, PositionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.PositionOf(number), request, cancellationToken);

    public Task<ApiResult> ClearPositionAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(number), null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<ReasonResponse>>> FindSimilarAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<ReasonResponse>>(HttpMethod.Post, AppealRoutes.SimilarReasonsOf(number), request, cancellationToken);

    public Task<ApiResult<ReasonCreatedResponse>> WriteReasonAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<ReasonCreatedResponse>(HttpMethod.Post, AppealRoutes.ReasonsOf(number), request, cancellationToken);

    public Task<ApiResult> EndorseAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId), null, cancellationToken);

    public Task<ApiResult> RemoveEndorsementAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, AppealRoutes.EndorsementOf(reasonId), null, cancellationToken);

    public Task<ApiResult> WithdrawReasonAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, AppealRoutes.ReasonOf(reasonId), null, cancellationToken);

    public Task<ApiResult<MyRestrictionResponse>> GetMyRestrictionAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MyRestrictionResponse>(HttpMethod.Get, AppealRoutes.MyRestriction, null, cancellationToken);

    public Task<ApiResult> ReportAsync(ReportRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.Reports, request, cancellationToken);

    public Task<ApiResult> FlagDateAsync(string number, FlagRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.FlagsOf(number), request, cancellationToken);
}

internal sealed class HttpModerationApi(ApiHttp http) : IModerationApi
{
    public Task<ApiResult<ModerationQueueResponse>> GetQueueAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<ModerationQueueResponse>(HttpMethod.Get, AppealRoutes.ModerationQueue, null, cancellationToken);

    public Task<ApiResult<AuthorViewResponse>> GetAuthorOfReasonAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        http.SendAsync<AuthorViewResponse>(HttpMethod.Get, AppealRoutes.With(AppealRoutes.ReasonAuthor, reasonId), null, cancellationToken);

    public Task<ApiResult<AuthorViewResponse>> GetInitiatorOfAppealAsync(Guid appealId, CancellationToken cancellationToken = default) =>
        http.SendAsync<AuthorViewResponse>(HttpMethod.Get, AppealRoutes.With(AppealRoutes.AppealInitiator, appealId), null, cancellationToken);

    public Task<ApiResult<RestrictionCreatedResponse>> RestrictAuthorOfReasonAsync(Guid reasonId, RestrictionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<RestrictionCreatedResponse>(HttpMethod.Post, AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), request, cancellationToken);

    public Task<ApiResult<RestrictionCreatedResponse>> RestrictInitiatorOfAppealAsync(Guid appealId, RestrictionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<RestrictionCreatedResponse>(HttpMethod.Post, AppealRoutes.With(AppealRoutes.RestrictAppealInitiator, appealId), request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<RestrictionResponse>>> GetRestrictionsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<RestrictionResponse>>(HttpMethod.Get, AppealRoutes.Restrictions, null, cancellationToken);

    public Task<ApiResult> LiftRestrictionAsync(Guid restrictionId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.LiftRestriction, restrictionId), request, cancellationToken);

    public Task<ApiResult> PublishAppealAsync(Guid appealId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.PublishAppeal, appealId), request, cancellationToken);

    public Task<ApiResult> ApproveDescriptionAsync(Guid changeId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.ApproveDescription, changeId), request, cancellationToken);

    public Task<ApiResult> ReturnDescriptionAsync(Guid changeId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.ReturnDescription, changeId), request, cancellationToken);

    public Task<ApiResult<HiddenItemsResponse>> GetHiddenAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<HiddenItemsResponse>(HttpMethod.Get, AppealRoutes.Hidden, null, cancellationToken);

    public Task<ApiResult> RestoreAppealAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.RestoreAppeal, appealId), request, cancellationToken);

    public Task<ApiResult> RestoreReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.RestoreReason, reasonId), request, cancellationToken);

    public Task<ApiResult> ReturnAppealAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.ReturnAppeal, appealId), request, cancellationToken);

    public Task<ApiResult> HideAppealAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.HideAppeal, appealId), request, cancellationToken);

    public Task<ApiResult> SetThresholdAsync(Guid appealId, ModeratorThresholdRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.AppealThreshold, appealId), request, cancellationToken);

    public Task<ApiResult> PublishReasonAsync(Guid reasonId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.PublishReason, reasonId), request, cancellationToken);

    public Task<ApiResult> HideReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.HideReason, reasonId), request, cancellationToken);

    public Task<ApiResult> MergeReasonAsync(Guid reasonId, MergeReasonRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.MergeReason, reasonId), request, cancellationToken);

    public Task<ApiResult> SetPlannedAsync(Guid appealId, ModeratorPlannedRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.AppealPlanned, appealId), request, cancellationToken);

    public Task<ApiResult> HoldAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.HoldAppeal, appealId), request, cancellationToken);

    public Task<ApiResult> ReleaseAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.ReleaseAppeal, appealId), request, cancellationToken);

    public Task<ApiResult> UrgentAsync(Guid appealId, Lt2.Api.Contracts.Digests.UrgentDigestRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(Lt2.Api.Contracts.Digests.DigestRoutes.Urgent, appealId), request, cancellationToken);
}

internal sealed class HttpAdminApi(ApiHttp http) : IAdminApi
{
    public Task<ApiResult<IReadOnlyList<SettingResponse>>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<SettingResponse>>(HttpMethod.Get, ApiRoutes.Admin.Settings, null, cancellationToken);

    public Task<ApiResult<SettingResponse>> UpdateSettingAsync(string key, UpdateSettingRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<SettingResponse>(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(key), request, cancellationToken);

    public Task<ApiResult<InvitationStatsResponse>> GetInvitationStatsAsync(string? code = null, CancellationToken cancellationToken = default) =>
        http.SendAsync<InvitationStatsResponse>(
            HttpMethod.Get, ApiRoutes.Admin.Invitations + (string.IsNullOrWhiteSpace(code) ? "" : "?code=" + Uri.EscapeDataString(code)), null, cancellationToken);

    public Task<ApiResult> SwitchInvitationAsync(Guid invitationId, bool on, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Admin.SwitchInvitation(invitationId, on), request, cancellationToken);
}

internal sealed class HttpDigestsApi(ApiHttp http) : Lt2.Api.Contracts.Digests.IDigestsApi
{
    public Task<ApiResult<IReadOnlyList<Lt2.Api.Contracts.Digests.DigestRunResponse>>> GetDigestsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<Lt2.Api.Contracts.Digests.DigestRunResponse>>(HttpMethod.Get, Lt2.Api.Contracts.Digests.DigestRoutes.Digests, null, cancellationToken);

    public Task<ApiResult<Lt2.Api.Contracts.Digests.DigestRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<Lt2.Api.Contracts.Digests.DigestRulesResponse>(HttpMethod.Get, Lt2.Api.Contracts.Digests.DigestRoutes.Rules, null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<Lt2.Api.Contracts.Digests.DigestRunResponse>>> GetDigestAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<Lt2.Api.Contracts.Digests.DigestRunResponse>>(HttpMethod.Get, Lt2.Api.Contracts.Digests.DigestRoutes.DigestOn(date), null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<Lt2.Api.Contracts.Digests.DigestMpResponse>>> GetMpsAsync(Guid runId, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<Lt2.Api.Contracts.Digests.DigestMpResponse>>(HttpMethod.Get, Lt2.Api.Contracts.Digests.DigestRoutes.MpsOf(runId), null, cancellationToken);

    public Task<ApiResult<Lt2.Api.Contracts.Digests.DigestPreviewResponse>> GetPreviewAsync(Guid runId, string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync<Lt2.Api.Contracts.Digests.DigestPreviewResponse>(HttpMethod.Get, Lt2.Api.Contracts.Digests.DigestRoutes.PreviewOf(runId, slug), null, cancellationToken);
}

internal sealed class HttpFeedbackApi(ApiHttp http) : Lt2.Api.Contracts.Feedback.IFeedbackApi
{
    public Task<ApiResult> SendAsync(Lt2.Api.Contracts.Feedback.FeedbackRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, Lt2.Api.Contracts.Feedback.FeedbackRoutes.Send, request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<Lt2.Api.Contracts.Feedback.FeedbackItemResponse>>> ListAsync(string? kind = null, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = string.Join("&", new[] { kind is null ? null : "kind=" + Uri.EscapeDataString(kind), status is null ? null : "status=" + Uri.EscapeDataString(status) }.Where(q => q is not null));
        return http.SendAsync<IReadOnlyList<Lt2.Api.Contracts.Feedback.FeedbackItemResponse>>(
            HttpMethod.Get, Lt2.Api.Contracts.Feedback.FeedbackRoutes.Staff + (query.Length > 0 ? "?" + query : ""), null, cancellationToken);
    }

    public Task<ApiResult> UpdateAsync(Guid id, Lt2.Api.Contracts.Feedback.FeedbackUpdateRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Feedback.FeedbackRoutes.ItemOf(id), request, cancellationToken);

    public Task<ApiResult> ReplyAsync(Guid id, Lt2.Api.Contracts.Feedback.FeedbackReplyRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, Lt2.Api.Contracts.Feedback.FeedbackRoutes.ReplyOf(id), request, cancellationToken);
}
