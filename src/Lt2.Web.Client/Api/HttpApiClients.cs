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

    public Task<ApiResult<SignInResponse>> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<SignInResponse>(HttpMethod.Post, ApiRoutes.Auth.SignIn, request, cancellationToken);

    public Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.SignOut, null, cancellationToken);

    public Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default) =>
        http.SendForJsonTextAsync(HttpMethod.Post, ApiRoutes.Auth.PasskeyCreationOptions, cancellationToken);

    public Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.Passkeys, request, cancellationToken);

    public Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) =>
        http.SendForJsonTextAsync(HttpMethod.Post, ApiRoutes.Auth.PasskeyRequestOptions, cancellationToken);

    public Task<ApiResult<SignInResponse>> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<SignInResponse>(HttpMethod.Post, ApiRoutes.Auth.PasskeySignIn, request, cancellationToken);
}

internal sealed class HttpMeApi(ApiHttp http) : IMeApi
{
    public Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MeResponse>(HttpMethod.Get, ApiRoutes.Me.Root, null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<SessionResponse>>(HttpMethod.Get, ApiRoutes.Me.Sessions, null, cancellationToken);

    public Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, ApiRoutes.Me.SessionById(sessionId), null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<KnownBrowserResponse>>> GetKnownBrowsersAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<KnownBrowserResponse>>(HttpMethod.Get, ApiRoutes.Me.KnownBrowsers, null, cancellationToken);

    public Task<ApiResult> ForgetKnownBrowserAsync(Guid id, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, ApiRoutes.Me.KnownBrowserById(id), null, cancellationToken);

    public Task<ApiResult> CompleteRegistrationAsync(CompleteRegistrationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, request, cancellationToken);

    public Task<ApiResult> ActivateRoleAsync(RoleActivationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Me.RoleActivation, request, cancellationToken);

    public Task<ApiResult> ConfirmPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Me.PasskeyConfirmation, request, cancellationToken);

    public Task<ApiResult> StartStaffSessionAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Me.StaffSession, request, cancellationToken);

    public Task<ApiResult> StartPasskeySessionAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Me.PasskeySession, request, cancellationToken);

    public Task<ApiResult> SendEmailProofCodeAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Me.EmailProofCode, null, cancellationToken);

    public Task<ApiResult> ChangeEmailAsync(EmailChangeRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Me.EmailChange, request, cancellationToken);

    public Task<ApiResult<EmailChangedResponse>> ConfirmEmailChangeAsync(EmailConfirmRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<EmailChangedResponse>(HttpMethod.Post, ApiRoutes.Me.EmailConfirm, request, cancellationToken);

    public Task<ApiResult> RestoreEmailAsync(EmailRestoreRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Me.EmailRestore, request, cancellationToken);

    public Task<ApiResult> GivePositionConsentAsync(PositionConsentRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ApiRoutes.Me.PositionConsent, request, cancellationToken);

    public Task<ApiResult> AcceptTermsAsync(TermsAcceptanceRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ApiRoutes.Me.Terms, request, cancellationToken);

    public Task<ApiResult> WithdrawPositionConsentAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PositionConsent, null, cancellationToken);

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

    public Task<ApiResult<BillListResponse>> GetAppealsAsync(string? search = null, bool planned = false, bool closed = false, string? cursor = null, string? sort = null, int? limit = null, CancellationToken cancellationToken = default) =>
        http.SendAsync<BillListResponse>(HttpMethod.Get, AppealRoutes.AppealsList(search, planned, closed, cursor, sort, limit), null, cancellationToken);

    public Task<ApiResult<BillRecordResponse>> GetRecordAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<BillRecordResponse>(HttpMethod.Get, AppealRoutes.RecordOf(number), null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<DescriptionRevisionResponse>>> GetHistoryAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<DescriptionRevisionResponse>>(HttpMethod.Get, AppealRoutes.HistoryOf(number), null, cancellationToken);

    public Task<ApiResult<ReasonPageResponse>> GetReasonsAsync(string number, string side, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        http.SendAsync<ReasonPageResponse>(HttpMethod.Get, AppealRoutes.ReasonPageOf(number, side, cursor, limit), null, cancellationToken);

    public Task<ApiResult<ReasonOnBillResponse>> GetReasonAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        http.SendAsync<ReasonOnBillResponse>(HttpMethod.Get, AppealRoutes.ReasonOf(reasonId), null, cancellationToken);

    public Task<ApiResult<MyBillRecordResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<MyBillRecordResponse>(HttpMethod.Get, AppealRoutes.MineOf(number), null, cancellationToken);

    public Task<ApiResult> TakePositionAsync(string number, PositionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.PositionOf(number), request, cancellationToken);

    public Task<ApiResult> ClearPositionAsync(string number, PositionRequest? request = null, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(number), request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<ReasonResponse>>> FindSimilarAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<ReasonResponse>>(HttpMethod.Post, AppealRoutes.SimilarReasonsOf(number), request, cancellationToken);

    public Task<ApiResult<ReasonCreatedResponse>> WriteReasonAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<ReasonCreatedResponse>(HttpMethod.Post, AppealRoutes.ReasonsOf(number), request, cancellationToken);

    public Task<ApiResult> EndorseAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId), null, cancellationToken);

    public Task<ApiResult> RemoveEndorsementAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, AppealRoutes.EndorsementOf(reasonId), null, cancellationToken);

    public Task<ApiResult<ReasonCreatedResponse>> EditReasonAsync(Guid reasonId, ReasonRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<ReasonCreatedResponse>(HttpMethod.Put, AppealRoutes.ReasonOf(reasonId), request, cancellationToken);

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

    public Task<ApiResult<RestrictionCreatedResponse>> RestrictAuthorOfReasonAsync(Guid reasonId, RestrictionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<RestrictionCreatedResponse>(HttpMethod.Post, AppealRoutes.With(AppealRoutes.RestrictReasonAuthor, reasonId), request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<RestrictionResponse>>> GetRestrictionsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<RestrictionResponse>>(HttpMethod.Get, AppealRoutes.Restrictions, null, cancellationToken);

    public Task<ApiResult> LiftRestrictionAsync(Guid restrictionId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.LiftRestriction, restrictionId), request, cancellationToken);

    public Task<ApiResult> KeepRecordAsync(Guid recordId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.KeepRecord, recordId), request, cancellationToken);

    public Task<ApiResult<HiddenItemsResponse>> GetHiddenAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<HiddenItemsResponse>(HttpMethod.Get, AppealRoutes.Hidden, null, cancellationToken);

    public Task<ApiResult> RestoreRecordAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.RestoreRecord, recordId), request, cancellationToken);

    public Task<ApiResult> RestoreReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.RestoreReason, reasonId), request, cancellationToken);

    public Task<ApiResult> HideRecordAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.HideRecord, recordId), request, cancellationToken);

    public Task<ApiResult> CloseRecordAsync(Guid recordId, CloseRecordRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.CloseRecord, recordId), request, cancellationToken);

    public Task<ApiResult> ReopenRecordAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.ReopenRecord, recordId), request, cancellationToken);

    public Task<ApiResult> SetThresholdAsync(Guid recordId, ModeratorThresholdRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordThreshold, recordId), request, cancellationToken);

    public Task<ApiResult<ModerationBillResponse>> GetBillAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<ModerationBillResponse>(HttpMethod.Get, AppealRoutes.ModerationBillOf(number), null, cancellationToken);

    public Task<ApiResult<BillRecordIdResponse>> EnsureRecordAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<BillRecordIdResponse>(HttpMethod.Post, AppealRoutes.BillRecordOf(number), null, cancellationToken);

    public Task<ApiResult> WriteDescriptionAsync(string number, ModeratorDescriptionRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(number), request, cancellationToken);

    public Task<ApiResult> ConfirmDescriptionAsync(string number, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.ConfirmDescriptionOf(number), request, cancellationToken);

    public Task<ApiResult> RemoveDescriptionAsync(string number, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.RemoveDescriptionOf(number), request, cancellationToken);

    public Task<ApiResult> PublishReasonAsync(Guid reasonId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.PublishReason, reasonId), request, cancellationToken);

    public Task<ApiResult> HideReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.HideReason, reasonId), request, cancellationToken);

    public Task<ApiResult> MergeReasonAsync(Guid reasonId, MergeReasonRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.MergeReason, reasonId), request, cancellationToken);

    public Task<ApiResult> SetPlannedAsync(Guid recordId, ModeratorPlannedRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordPlanned, recordId), request, cancellationToken);

    public Task<ApiResult> HoldAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.HoldRecord, recordId), request, cancellationToken);

    public Task<ApiResult> ReleaseAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(AppealRoutes.ReleaseRecord, recordId), request, cancellationToken);

    public Task<ApiResult> UrgentAsync(Guid recordId, Lt2.Api.Contracts.Digests.UrgentDigestRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, AppealRoutes.With(Lt2.Api.Contracts.Digests.DigestRoutes.Urgent, recordId), request, cancellationToken);

    public Task<ApiResult<AiStatusResponse>> GetAiAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<AiStatusResponse>(HttpMethod.Get, AppealRoutes.Ai, null, cancellationToken);

    public Task<ApiResult<DescriptionDraftResponse>> DraftDescriptionAsync(string number, DescriptionDraftRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync<DescriptionDraftResponse>(HttpMethod.Post, AppealRoutes.DescriptionDraftOf(number), request, cancellationToken);
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

    public Task<ApiResult<IReadOnlyList<Lt2.Api.Contracts.Mps.AdminMpLinkResponse>>> GetMpLinksAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<Lt2.Api.Contracts.Mps.AdminMpLinkResponse>>(HttpMethod.Get, Lt2.Api.Contracts.Mps.MpRoutes.AdminLinks, null, cancellationToken);

    public Task<ApiResult> EndMpLinkAsync(string slug, StatementRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, Lt2.Api.Contracts.Mps.MpRoutes.EndLinkOf(slug), request, cancellationToken);

    public Task<ApiResult> LockAccountAsync(Lt2.Api.Contracts.Admin.AccountLockRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Admin.LockAccount, request, cancellationToken);

    public Task<ApiResult> UnlockAccountAsync(Lt2.Api.Contracts.Admin.AccountLockRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Admin.UnlockAccount, request, cancellationToken);
}


internal sealed class HttpFollowsApi(ApiHttp http) : Lt2.Api.Contracts.Follows.IFollowsApi
{
    public Task<ApiResult<Lt2.Api.Contracts.Follows.MyFollowsResponse>> GetMineAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<Lt2.Api.Contracts.Follows.MyFollowsResponse>(HttpMethod.Get, Lt2.Api.Contracts.Follows.FollowRoutes.Mine, null, cancellationToken);

    public Task<ApiResult> FollowBillAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.BillOf(number), null, cancellationToken);

    public Task<ApiResult> UnfollowBillAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.Follows.FollowRoutes.BillOf(number), null, cancellationToken);

    public Task<ApiResult> FollowMpAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.MpOf(slug), null, cancellationToken);

    public Task<ApiResult> UnfollowMpAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.Follows.FollowRoutes.MpOf(slug), null, cancellationToken);

    public Task<ApiResult> FollowOrganisationAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.OrganisationOf(slug), null, cancellationToken);

    public Task<ApiResult> UnfollowOrganisationAsync(string slug, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.Follows.FollowRoutes.OrganisationOf(slug), null, cancellationToken);

    public Task<ApiResult> FollowMemberAsync(string profileId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Me.ProfileRoutes.FollowOf(profileId), null, cancellationToken);

    public Task<ApiResult> UnfollowMemberAsync(string profileId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.Me.ProfileRoutes.FollowOf(profileId), null, cancellationToken);

    public Task<ApiResult<Lt2.Api.Contracts.Me.FollowersResponse>> GetBillFollowersAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<Lt2.Api.Contracts.Me.FollowersResponse>(HttpMethod.Get, Lt2.Api.Contracts.Follows.FollowRoutes.BillFollowersOf(number), null, cancellationToken);

    public Task<ApiResult<Lt2.Api.Contracts.Follows.NoticeSettingsResponse>> GetNoticesAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<Lt2.Api.Contracts.Follows.NoticeSettingsResponse>(HttpMethod.Get, Lt2.Api.Contracts.Follows.FollowRoutes.Notices, null, cancellationToken);

    public Task<ApiResult> SetNoticesAsync(Lt2.Api.Contracts.Follows.NoticeSettingsRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.Notices, request, cancellationToken);
}

/// <summary>Design 8.24: how often the caller's emails come.</summary>
internal sealed class HttpNoticeFrequencyApi(ApiHttp http) : Lt2.Api.Contracts.Follows.INoticeFrequencyApi
{
    public Task<ApiResult<Lt2.Api.Contracts.Follows.NoticeFrequencyResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<Lt2.Api.Contracts.Follows.NoticeFrequencyResponse>(HttpMethod.Get, Lt2.Api.Contracts.Follows.NoticeFrequencyRoutes.Mine, null, cancellationToken);

    public Task<ApiResult> SetAsync(Lt2.Api.Contracts.Follows.NoticeFrequencyRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.NoticeFrequencyRoutes.Mine, request, cancellationToken);
}

/// <summary>Design 8.24.1: a member's stop link; the page is server-rendered, so the browser calls this only for completeness.</summary>
internal sealed class HttpNoticeStopApi(ApiHttp http) : Lt2.Api.Contracts.Follows.INoticeStopApi
{
    public Task<ApiResult> StopAsync(string token, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, Lt2.Api.Contracts.Follows.NoticeStopRoutes.StopOf(token), new { }, cancellationToken);
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

internal sealed class HttpAnalyticsApi(ApiHttp http) : Lt2.Api.Contracts.Analytics.IAnalyticsApi
{
    public Task<ApiResult<Lt2.Api.Contracts.Analytics.AnalyticsResponse>> GetAsync(int? year, bool olderAccounts, Lt2.Api.Contracts.Analytics.AnalyticsBillQuery? bills = null, CancellationToken cancellationToken = default) =>
        http.SendAsync<Lt2.Api.Contracts.Analytics.AnalyticsResponse>(HttpMethod.Get, Lt2.Api.Contracts.Analytics.AnalyticsRoutes.Of(year, olderAccounts, bills), null, cancellationToken);

    public Task<ApiResult<Lt2.Api.Contracts.Analytics.MyAgreementResponse>> GetMyAgreementAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<Lt2.Api.Contracts.Analytics.MyAgreementResponse>(HttpMethod.Get, Lt2.Api.Contracts.Analytics.AnalyticsRoutes.MyAgreement, null, cancellationToken);
}

internal sealed class HttpMyBillsApi(ApiHttp http) : IMyBillsApi
{
    public Task<ApiResult<MyBillsResponse>> GetAsync(MyBillsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return http.SendAsync<MyBillsResponse>(HttpMethod.Get, ApiRoutes.Me.Bills + "?" + query.ToQueryString(), null, cancellationToken);
    }

    public Task<ApiResult<BillSeenResponse>> SeenAsync(string number, CancellationToken cancellationToken = default) =>
        http.SendAsync<BillSeenResponse>(HttpMethod.Post, ApiRoutes.Me.BillSeenOf(number), null, cancellationToken);
}

/// <summary>The browser's <see cref="IUsageApi"/> (design 8.27).</summary>
internal sealed class HttpUsageApi(ApiHttp http) : IUsageApi
{
    public Task<ApiResult<UsageResponse>> GetAsync(int days = UsageRoutes.DefaultRange, CancellationToken cancellationToken = default) =>
        http.SendAsync<UsageResponse>(HttpMethod.Get, UsageRoutes.Api(days), null, cancellationToken);
}
