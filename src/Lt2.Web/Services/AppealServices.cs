using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Audit;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Admin;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>Hand-written mapping between the Appeals module and the API contracts.</summary>
internal static class AppealMapping
{
    public static SideCountsResponse Map(SideCounts c) => new(c.For, c.ForOlderThan30Days, c.Against, c.AgainstOlderThan30Days);

    public static ThresholdResponse Map(ThresholdInfo t) => new(t.Value, t.SetBy, t.Note, t.Participants, t.Reached, t.EnteredSummaries);

    public static BillRefResponse Map(BillFacts b) => new(b.Number, b.Title);

    public static ReasonResponse Map(ReasonInfo r) => new(r.Id, r.Side, r.Text, r.AuthorName, r.Endorsements, r.Status, r.CreatedAt);

    public static AppealResponse Map(AppealInfo a) => new(
        a.Id, Map(a.Bill), a.Status, a.InitiatorName, a.Description, a.OpenedAt, a.PublishedAt, Map(a.Counts), Map(a.Threshold),
        a.Reasons.Select(Map).ToList(), a.ReturnNote, Map(a.Surge), a.ClosingVoteId, a.ClosedAt, Map(a.Planned));

    public static PlannedDateResponse? Map(PlannedDate? p) => p is null ? null : new(p.Date, p.Stage, p.SourceUrl, p.SetByModerator);

    public static SurgeResponse? Map(SurgeInfo? s) => s is null ? null : new(s.SharePercent, s.WindowHours);

    public static AppealListResponse Map(AppealPage p) => new(
        p.Items.Select(i => new AppealListItemResponse(i.Id, Map(i.Bill), Map(i.Counts), Map(i.Threshold), i.PublishedAt, Map(i.Planned))).ToList(),
        p.Total,
        p.NextCursor);

    public static MyAppealResponse Map(MyAppeal m) => new(
        m.Side, m.Endorsed, m.EndorsementsLeft, m.MyReasons.Select(Map).ToList(), m.ReasonsLeft, m.IsInitiator, m.MayChangeThreshold, m.HasPositionConsent);

    public static AppealRulesResponse Map(AppealRulesInfo r) =>
        new(r.ThresholdDefault, r.ThresholdMinimum, r.ReasonMaxLength, r.ReasonsPerPerson, r.EndorsementsPerPerson, r.DescriptionMaxLength);

    public static QueueReportResponse Map(QueueReport r) => new(r.Id, r.Ground, r.Text, r.CreatedAt);

    public static ModerationQueueResponse Map(ModerationItems q) => new(
        q.Appeals.Select(a => new QueueAppealResponse(
            a.Id, Map(a.Bill), a.Status, a.InitiatorName, a.Description, a.FirstReason is { } f ? Map(f) : null, a.Threshold, a.Flags,
            a.Reports.Select(Map).ToList(), a.OpenedAt, Map(a.Surge), Map(a.Planned),
            (a.DateFlags ?? []).Select(f => new QueueFlagResponse(f.Id, f.Kind, f.Link, f.Note, f.CreatedAt)).ToList(),
            a.UnderThresholdOnAgenda,
            a.Held)).ToList(),
        q.Reasons.Select(r => new QueueReasonResponse(
            r.Id, r.AppealId, Map(r.Bill), r.Side, r.Text, r.AuthorName, r.Status, r.Seen, r.Flags, r.Reports.Select(Map).ToList(),
            r.MergeCandidates.Select(Map).ToList(), r.CreatedAt)).ToList());

    public static OpenAppeal ToModule(OpenAppealRequest r) => new(r.Side, r.FirstReason, r.Description, r.Threshold);

    public static EditAppeal ToModule(EditAppealRequest r) => new(r.Description, r.FirstReason);

    public static NewReason ToModule(ReasonRequest r) => new(r.Side, r.Text);

    public static NewReport ToModule(ReportRequest r) => new(r.TargetKind, r.TargetId, r.Ground, r.Text);

    /// <summary>The signed-in account, or <c>null</c>.</summary>
    public static Guid? UserId(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>The moderator for the audit log: the role that works in this session (moderator first).</summary>
    public static async Task<ModeratorActor> ModeratorAsync(IIdentityModule identity, ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken)
    {
        var session = await identity.GetStaffSessionAsync(user, cancellationToken);
        var role = session.Has(Roles.Moderator) ? Roles.Moderator : Roles.Administrator;
        return new ModeratorActor(UserId(user) ?? throw new InvalidOperationException("A moderator is signed in."), role, ipHash);
    }
}

/// <summary>
/// Server-side <see cref="IAppealsApi"/> (design 3.1): the server-rendered pages and the prerendered interactive
/// components call the module in-process, as the signed-in person of the current request.
/// </summary>
internal sealed class ServerAppealsApi(IAppealsModule appeals, IHttpContextAccessor accessor, ErrorMapper errors) : IAppealsApi
{
    private static readonly Error SignInRequired = Error.Unauthorized("auth.required");

    public async Task<ApiResult<AppealRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        ApiResult<AppealRulesResponse>.Ok(AppealMapping.Map(await appeals.RulesAsync(cancellationToken)));

    public async Task<ApiResult<AppealListResponse>> GetAppealsAsync(string? sort = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        ApiResult<AppealListResponse>.Ok(AppealMapping.Map(await appeals.ListAsync(sort, cursor, cancellationToken)));

    public async Task<ApiResult<AppealResponse>> GetAppealAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await appeals.GetAsync(number, UserId, cancellationToken), AppealMapping.Map);

    public async Task<ApiResult<IReadOnlyList<DescriptionRevisionResponse>>> GetHistoryAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(
            await appeals.HistoryAsync(number, cancellationToken),
            rows => (IReadOnlyList<DescriptionRevisionResponse>)rows.Select(r => new DescriptionRevisionResponse(r.ChangedAt, r.OldText, r.NewText)).ToList());

    public async Task<ApiResult<MyAppealResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await appeals.MineAsync(number, id, cancellationToken), AppealMapping.Map)
            : ApiResult<MyAppealResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult<AppealResponse>> OpenAsync(string number, OpenAppealRequest request, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await appeals.OpenAsync(number, id, AppealMapping.ToModule(request), cancellationToken), AppealMapping.Map)
            : ApiResult<AppealResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult> EditAsync(string number, EditAppealRequest request, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.EditAsync(number, id, AppealMapping.ToModule(request), cancellationToken));

    public async Task<ApiResult> ChangeThresholdAsync(string number, ThresholdRequest request, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.ChangeThresholdAsync(number, id, request.Threshold, cancellationToken));

    public async Task<ApiResult> TakePositionAsync(string number, PositionRequest request, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.TakePositionAsync(number, id, request.Side, cancellationToken));

    public async Task<ApiResult> ClearPositionAsync(string number, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.ClearPositionAsync(number, id, cancellationToken));

    public async Task<ApiResult<IReadOnlyList<ReasonResponse>>> FindSimilarAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(
            await appeals.FindSimilarAsync(number, AppealMapping.ToModule(request), cancellationToken),
            list => (IReadOnlyList<ReasonResponse>)list.Select(AppealMapping.Map).ToList());

    public async Task<ApiResult<ReasonCreatedResponse>> WriteReasonAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await appeals.WriteReasonAsync(number, id, AppealMapping.ToModule(request), cancellationToken), r => new ReasonCreatedResponse(r.Id, r.Status))
            : ApiResult<ReasonCreatedResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult> EndorseAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.EndorseAsync(reasonId, id, cancellationToken));

    public async Task<ApiResult> RemoveEndorsementAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.RemoveEndorsementAsync(reasonId, id, cancellationToken));

    public async Task<ApiResult> ReportAsync(ReportRequest request, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.ReportAsync(id, AppealMapping.ToModule(request), cancellationToken));

    public async Task<ApiResult> FlagDateAsync(string number, FlagRequest request, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.FlagDateAsync(number, id, new NewFlag(request.Kind, request.Link, request.Note), cancellationToken));

    private Guid? UserId => AppealMapping.UserId(accessor.HttpContext?.User);

    private async Task<ApiResult> AsMemberAsync(Func<Guid, Task<Result<Unit>>> action) =>
        UserId is { } id ? errors.ToApiResult(await action(id)) : ApiResult.Fail(errors.ToProblem(SignInRequired));
}

/// <summary>Server-side <see cref="IModerationApi"/>, for the prerendered moderation page.</summary>
internal sealed class ServerModerationApi(
    IAppealsModeration moderation, Lt2.Notifications.IDigests digests, IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors) : IModerationApi
{
    public async Task<ApiResult<ModerationQueueResponse>> GetQueueAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<ModerationQueueResponse>.Ok(AppealMapping.Map(await moderation.QueueAsync(cancellationToken)))
            : ApiResult<ModerationQueueResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> PublishAppealAsync(Guid appealId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.PublishAppealAsync(appealId, request.Statement, m, cancellationToken));

    public Task<ApiResult> ReturnAppealAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.ReturnAppealAsync(appealId, request.Statement, m, cancellationToken));

    public Task<ApiResult> HideAppealAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HideAppealAsync(appealId, request.Statement, m, cancellationToken));

    public Task<ApiResult> SetThresholdAsync(Guid appealId, ModeratorThresholdRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.SetThresholdAsync(appealId, request.Threshold, request.Statement, m, cancellationToken));

    public Task<ApiResult> PublishReasonAsync(Guid reasonId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.PublishReasonAsync(reasonId, request.Statement, m, cancellationToken));

    public Task<ApiResult> HideReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HideReasonAsync(reasonId, request.Statement, m, cancellationToken));

    public Task<ApiResult> MergeReasonAsync(Guid reasonId, MergeReasonRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.MergeReasonAsync(reasonId, request.IntoReasonId, request.Statement, m, cancellationToken));

    public Task<ApiResult> SetPlannedAsync(Guid appealId, ModeratorPlannedRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.SetPlannedAsync(appealId, request.Date, request.Stage, request.Statement, m, cancellationToken));

    public Task<ApiResult> HoldAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HoldAsync(appealId, request.Statement, m, cancellationToken));

    public Task<ApiResult> ReleaseAsync(Guid appealId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.ReleaseAsync(appealId, request.Statement, m, cancellationToken));

    public Task<ApiResult> UrgentAsync(Guid appealId, Lt2.Api.Contracts.Digests.UrgentDigestRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(async m =>
        {
            var made = await digests.UrgentAsync(appealId, request.Statement, new Lt2.Notifications.StaffActor(m.UserId, m.Role, m.IpHash), cancellationToken);
            return made.IsSuccess ? Result.Ok() : made.Error!;
        });

    private async Task<bool> IsModeratorAsync()
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            return false;
        }

        var authorization = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, http, Policies.Moderator)).Succeeded;
    }

    private async Task<ApiResult> ActAsync(Func<ModeratorActor, Task<Result<Unit>>> action)
    {
        if (!await IsModeratorAsync())
        {
            return ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        return errors.ToApiResult(await action(await AppealMapping.ModeratorAsync(identity, http.User, ipHasher.Hash(http), http.RequestAborted)));
    }
}

/// <summary>Server-side <see cref="IAdminApi"/>, for the prerendered settings page.</summary>
internal sealed class ServerAdminApi(ISettingsAdministration settings, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors) : IAdminApi
{
    public async Task<ApiResult<IReadOnlyList<SettingResponse>>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        await IsAdministratorAsync()
            ? ApiResult<IReadOnlyList<SettingResponse>>.Ok((await settings.ListAsync(cancellationToken)).Select(SettingMapping.ToResponse).ToList())
            : ApiResult<IReadOnlyList<SettingResponse>>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<SettingResponse>> UpdateSettingAsync(string key, UpdateSettingRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync())
        {
            return ApiResult<SettingResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        var result = await settings.UpdateAsync(key, request.Value, AppealMapping.UserId(http.User)!.Value, Roles.Administrator, ipHasher.Hash(http), cancellationToken);
        return errors.ToApiResult(result, SettingMapping.ToResponse);
    }

    private async Task<bool> IsAdministratorAsync()
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            return false;
        }

        var authorization = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
        return (await authorization.AuthorizeAsync(http.User, http, Policies.Administrator)).Succeeded;
    }
}
