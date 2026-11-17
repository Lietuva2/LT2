using Lt2.Api.Contracts.Admin;

namespace Lt2.Api.Contracts.Appeals;

/// <summary>Design 8.8, 8.12: bills as people take part on them, positions, arguments, endorsements and reports.</summary>
public interface IAppealsApi
{
    Task<ApiResult<AppealRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>/projektai</c> (design 8.12.5): active bills in the order <paramref name="sort"/> (<c>activity</c> by default,
    /// <c>participants</c>, <c>arguments</c>, <c>planned</c>, <c>latest</c>); a search by number or title; only those planned soon; or, with
    /// <paramref name="closed"/>, the closed ones.
    /// </summary>
    Task<ApiResult<BillListResponse>> GetAppealsAsync(string? search = null, bool planned = false, bool closed = false, string? cursor = null, string? sort = null, CancellationToken cancellationToken = default);

    /// <summary>The bill as people take part on it; an active bill nobody took part in answers the empty open state.</summary>
    Task<ApiResult<BillRecordResponse>> GetRecordAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>The caller's own side, marks and arguments on this bill.</summary>
    Task<ApiResult<MyBillRecordResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>The public edit history of the bill's description.</summary>
    Task<ApiResult<IReadOnlyList<DescriptionRevisionResponse>>> GetHistoryAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult> TakePositionAsync(string number, PositionRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ClearPositionAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<ReasonResponse>>> FindSimilarAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<ReasonCreatedResponse>> WriteReasonAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> EndorseAsync(Guid reasonId, CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveEndorsementAsync(Guid reasonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The author edits their argument's text and side while the record is open and nobody has reacted (design 8.2, owner
    /// 2026-11-17); the status after it (<c>pending</c> when pre-screening flagged the new text).
    /// </summary>
    Task<ApiResult<ReasonCreatedResponse>> EditReasonAsync(Guid reasonId, ReasonRequest request, CancellationToken cancellationToken = default);

    /// <summary>The author withdraws their own argument while the record is open (design 8.2).</summary>
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

    /// <summary>Restricts the argument's author from writing; a moderator role in a staff-passkey session.</summary>
    Task<ApiResult<RestrictionCreatedResponse>> RestrictAuthorOfReasonAsync(Guid reasonId, RestrictionRequest request, CancellationToken cancellationToken = default);

    /// <summary>The restrictions that hold now.</summary>
    Task<ApiResult<IReadOnlyList<RestrictionResponse>>> GetRestrictionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lifts a restriction early; a moderator or an administrator.</summary>
    Task<ApiResult> LiftRestrictionAsync(Guid restrictionId, StatementRequest request, CancellationToken cancellationToken = default);

    /// <summary>"Palikti": keeps a reported or flagged bill record as it is; its reports and date flags close.</summary>
    Task<ApiResult> KeepRecordAsync(Guid recordId, ModerationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Hides a bill's record: taking part on the bill closes, with the statement shown on its page (design 8.12.3).</summary>
    Task<ApiResult> HideRecordAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Hidden appeals and reasons, with who hid them, when and why (design 8.3).</summary>
    Task<ApiResult<HiddenItemsResponse>> GetHiddenAsync(CancellationToken cancellationToken = default);

    /// <summary>"Atkurti": brings a hidden bill record back as it was; statement required (design 8.12.3).</summary>
    Task<ApiResult> RestoreRecordAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default);

    /// <summary>"Atkurti": brings a hidden reason back, while its record is open; statement required.</summary>
    Task<ApiResult> RestoreReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> SetThresholdAsync(Guid recordId, ModeratorThresholdRequest request, CancellationToken cancellationToken = default);

    /// <summary>The state a moderator's controls on the bill's page need (design 8.12.3); forbidden for anyone else.</summary>
    Task<ApiResult<ModerationBillResponse>> GetBillAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>The bill's record for an action on it, created on an active bill that has none (design 8.12.1).</summary>
    Task<ApiResult<BillRecordIdResponse>> EnsureRecordAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>Writes or corrects an active bill's description (design 8.12.3, 8.13); the statement is required.</summary>
    Task<ApiResult> WriteDescriptionAsync(string number, ModeratorDescriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Confirms an AI summary nobody has read (design 8.13): it becomes "Sukurta DI · taisė žmonės"; the note is optional.</summary>
    Task<ApiResult> ConfirmDescriptionAsync(string number, ModerationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes the bill's description (design 8.13); the statement is required, and the summary job never writes it again.</summary>
    Task<ApiResult> RemoveDescriptionAsync(string number, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> PublishReasonAsync(Guid reasonId, ModerationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> HideReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> MergeReasonAsync(Guid reasonId, MergeReasonRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> SetPlannedAsync(Guid recordId, ModeratorPlannedRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> HoldAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ReleaseAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Approves one urgent summary for the record's bill (design 8.4); a dry run in M2a.</summary>
    Task<ApiResult> UrgentAsync(Guid recordId, Digests.UrgentDigestRequest request, CancellationToken cancellationToken = default);


    /// <summary>Whether an AI draft can be asked for (design 8.11.4).</summary>
    Task<ApiResult<AiStatusResponse>> GetAiAsync(CancellationToken cancellationToken = default);

    /// <summary>An AI draft of the description from pasted official text (design 8.11.4); refused when no provider is configured.</summary>
    Task<ApiResult<DescriptionDraftResponse>> DraftDescriptionAsync(string number, DescriptionDraftRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Design 8.8: the administrator's settings screen. Needs an active administrator role.</summary>
public interface IAdminApi
{
    Task<ApiResult<IReadOnlyList<SettingResponse>>> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<SettingResponse>> UpdateSettingAsync(string key, UpdateSettingRequest request, CancellationToken cancellationToken = default);

    /// <summary>The invitation numbers (design 6.2): per day, per code, per level; <paramref name="code"/> finds one code.</summary>
    Task<ApiResult<Me.InvitationStatsResponse>> GetInvitationStatsAsync(string? code = null, CancellationToken cancellationToken = default);

    /// <summary>Switches a code off or on again, with a statement; audited.</summary>
    Task<ApiResult> SwitchInvitationAsync(Guid invitationId, bool on, Appeals.StatementRequest request, CancellationToken cancellationToken = default);
}
