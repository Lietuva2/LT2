using Lt2.Api.Contracts.Admin;

namespace Lt2.Api.Contracts.Appeals;

/// <summary>Design 8.8: appeals, positions, arguments, endorsements and reports.</summary>
public interface IAppealsApi
{
    Task<ApiResult<AppealRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default);

    /// <param name="sort"><c>participants</c> (default) or <c>newest</c>.</param>
    Task<ApiResult<AppealListResponse>> GetAppealsAsync(string? sort = null, string? cursor = null, CancellationToken cancellationToken = default);

    Task<ApiResult<AppealResponse>> GetAppealAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>The caller's own side, marks and arguments on this appeal.</summary>
    Task<ApiResult<MyAppealResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>The public edit history of the appeal's neutral description.</summary>
    Task<ApiResult<IReadOnlyList<DescriptionRevisionResponse>>> GetHistoryAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<AppealResponse>> OpenAsync(string number, OpenAppealRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> EditAsync(string number, EditAppealRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ChangeThresholdAsync(string number, ThresholdRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> TakePositionAsync(string number, PositionRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ClearPositionAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<ReasonResponse>>> FindSimilarAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<ReasonCreatedResponse>> WriteReasonAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> EndorseAsync(Guid reasonId, CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveEndorsementAsync(Guid reasonId, CancellationToken cancellationToken = default);

    /// <summary>The author withdraws their own argument while the appeal is open (design 8.2).</summary>
    Task<ApiResult> WithdrawReasonAsync(Guid reasonId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own writing restriction, if one holds (design 8.3).</summary>
    Task<ApiResult<MyRestrictionResponse>> GetMyRestrictionAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> ReportAsync(ReportRequest request, CancellationToken cancellationToken = default);

    /// <summary>"The date is wrong" or "voted sooner", with a link (design 8.4).</summary>
    Task<ApiResult> FlagDateAsync(string number, FlagRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Design 8.3, 8.8: the moderators' queue and actions. Needs an active moderator role (a staff-passkey session).</summary>
public interface IModerationApi
{
    Task<ApiResult<ModerationQueueResponse>> GetQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>The author of an argument, as a moderator sees them before restricting (design 8.3).</summary>
    Task<ApiResult<AuthorViewResponse>> GetAuthorOfReasonAsync(Guid reasonId, CancellationToken cancellationToken = default);

    Task<ApiResult<AuthorViewResponse>> GetInitiatorOfAppealAsync(Guid appealId, CancellationToken cancellationToken = default);

    /// <summary>Restricts the argument's author from writing; a moderator role in a staff-passkey session.</summary>
    Task<ApiResult<RestrictionCreatedResponse>> RestrictAuthorOfReasonAsync(Guid reasonId, RestrictionRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<RestrictionCreatedResponse>> RestrictInitiatorOfAppealAsync(Guid appealId, RestrictionRequest request, CancellationToken cancellationToken = default);

    /// <summary>The restrictions that hold now.</summary>
    Task<ApiResult<IReadOnlyList<RestrictionResponse>>> GetRestrictionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lifts a restriction early; a moderator or an administrator.</summary>
    Task<ApiResult> LiftRestrictionAsync(Guid restrictionId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> PublishAppealAsync(Guid appealId, ModerationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ReturnAppealAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> HideAppealAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> SetThresholdAsync(Guid appealId, ModeratorThresholdRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> PublishReasonAsync(Guid reasonId, ModerationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> HideReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> MergeReasonAsync(Guid reasonId, MergeReasonRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> SetPlannedAsync(Guid appealId, ModeratorPlannedRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> HoldAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ReleaseAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Approves one urgent summary for the appeal's bill (design 8.4); a dry run in M2a.</summary>
    Task<ApiResult> UrgentAsync(Guid appealId, Digests.UrgentDigestRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Design 8.8: the administrator's settings screen. Needs an active administrator role.</summary>
public interface IAdminApi
{
    Task<ApiResult<IReadOnlyList<SettingResponse>>> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<SettingResponse>> UpdateSettingAsync(string key, UpdateSettingRequest request, CancellationToken cancellationToken = default);
}
