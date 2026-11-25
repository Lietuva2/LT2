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

    public static ReasonResponse Map(ReasonInfo r) => new(r.Id, r.Side, r.Text, r.AuthorName, r.Endorsements, r.Status, r.CreatedAt, r.AuthorProfileId) { Comments = r.Comments };

    public static BillRecordResponse Map(BillRecordInfo a) => new(
        a.Id, Map(a.Bill), a.Status, a.Description, a.DescriptionSource, Map(a.Counts), Map(a.Threshold),
        a.Reasons.Select(Map).ToList(), Map(a.Surge), a.ClosingVoteId, a.ClosedAt, Map(a.Planned), a.HiddenStatement, a.DescriptionUrl, a.DescriptionAt,
        a.Cards?.Select(Map).ToList(), Map(a.ReasonTotals ?? ReasonTotals.None), a.ClosedReason, a.ClosedDecidedOn, a.ClosedSourceUrl, a.PlainTitle);

    public static AuthorCardResponse Map(AuthorCard c) => new(c.ProfileId, c.Name, c.About);

    public static ReasonTotalsResponse Map(ReasonTotals t) => new(t.For, t.Against);

    public static ReasonPageResponse Map(ReasonPage p) => new(p.Side, p.Items.Select(Map).ToList(), p.Total, p.NextCursor, p.Cards.Select(Map).ToList());

    public static ReasonOnBillResponse Map(ReasonOnBill r) => new(r.BillNumber, Map(r.Reason), r.Cards.Select(Map).ToList());

    public static PlannedDateResponse? Map(PlannedDate? p) => p is null ? null : new(p.Date, p.Stage, p.SourceUrl, p.SetByModerator);

    public static SurgeResponse? Map(SurgeInfo? s) => s is null ? null : new(s.SharePercent, s.WindowHours);

    public static BillListResponse Map(BillListPage p) => new(
        p.Items.Select(i => new BillListItemResponse(i.Id, new BillRefResponse(i.Bill.Number, i.Bill.Title, i.PlainTitle), i.Status, Map(i.Counts), Map(i.Threshold), Map(i.Planned), i.Arguments, i.LastActivityAt)).ToList(),
        p.Total,
        p.NextCursor);

    public static MyBillRecordResponse Map(MyBillRecord m) =>
        new(m.Side, m.Endorsed, m.EndorsementsLeft, m.MyReasons.Select(Map).ToList(), m.ReasonsLeft, m.HasPositionConsent) { Editable = m.Editable, HiddenStatements = m.HiddenStatements, Uncounted = m.Uncounted };

    public static AppealRulesResponse Map(AppealRulesInfo r) =>
        new(r.ThresholdDefault, r.ThresholdMinimum, r.TextMaxLength, r.ReasonsPerPerson, r.EndorsementsPerPerson, r.DescriptionMaxLength, r.AiSummaries) { MinReasonWords = r.MinReasonWords };

    public static QueueReportResponse Map(QueueReport r) => new(r.Id, r.Ground, r.Text, r.CreatedAt);

    public static ModerationBillResponse Map(ModerationBillState s) =>
        new(s.Id, s.BillNumber, s.Status, s.Description, s.DescriptionSource, s.Threshold, s.ThresholdSetBy, s.Held, Map(s.Planned), s.HasOpenItems,
            s.Reasons.Select(Map).ToList(), s.DescriptionUrl, s.DescriptionReports.Select(Map).ToList(), s.ClosedReason, s.PlainTitle);

    public static QueueReasonResponse Map(QueueReason r) => new(
        r.Id, r.RecordId, Map(r.Bill), r.Side, r.Text, r.AuthorName, r.Status, r.Seen, r.Flags, r.Reports.Select(Map).ToList(),
        r.MergeCandidates.Select(Map).ToList(), r.CreatedAt, r.RecordStatus) { Comments = r.Comments };

    public static ModerationQueueResponse Map(ModerationItems q) => new(
        q.Records.Select(a => new QueueRecordResponse(
            a.Id, Map(a.Bill), a.Description, a.DescriptionSource, a.Threshold, a.Reports.Select(Map).ToList(), Map(a.Surge), Map(a.Planned),
            a.DateFlags.Select(f => new QueueFlagResponse(f.Id, f.Kind, f.Link, f.Note, f.CreatedAt, f.Date)).ToList(),
            a.UnderThresholdOnAgenda, a.Held, a.Participants, a.DescriptionUrl, a.Status, a.PlainTitle)).ToList(),
        q.Reasons.Select(Map).ToList(),
        q.RecordsTotal,
        q.ReasonsTotal);

    public static UndescribedBillsResponse Map(UndescribedBills u) => new(
        u.Bills.Select(b => new UndescribedBillResponse(
            new BillRefResponse(b.Bill.Number, b.Bill.Title, b.PlainTitle), b.DescriptionSource, b.Participants, b.Arguments, b.Followers, Map(b.Planned))).ToList(),
        u.Total);

    public static HiddenItemsResponse Map(HiddenItems h) => new(
        h.Records.Select(a => new HiddenRecordResponse(a.Id, Map(a.Bill), a.Description, a.HiddenAt, a.HiddenBy, a.Statement, a.RestoresAs)).ToList(),
        h.Reasons.Select(r => new HiddenReasonResponse(
            r.Id, Map(r.Bill), r.Side, r.Text, r.AuthorName, r.HiddenAt, r.HiddenBy, r.Statement, r.Restorable)).ToList());

    public static NewReason ToModule(ReasonRequest r) => new(r.Side, r.Text);

    public static NewReport ToModule(ReportRequest r) => new(r.TargetKind, r.TargetId, r.Ground, r.Text);

    public static ModeratorDescription ToModule(ModeratorDescriptionRequest r) => new(r.Description, r.Statement, r.FromDraft, r.DescriptionUrl, r.PlainTitle, r.WrittenByAi);

    public static DescriptionRevisionResponse Map(DescriptionRevision r) =>
        new(r.ChangedAt, r.OldText, r.NewText, r.TextChanged, r.OldTitle, r.NewTitle, r.TitleChanged, r.OldUrl, r.NewUrl, r.UrlChanged, r.Confirmed, r.Statement);

    public static RecordClosure ToModule(CloseRecordRequest r) => new(r.Reason, r.DecidedOn, r.SourceUrl, r.Statement);


    public static MyRestrictionResponse Map(MyRestriction? r) => r is null ? new(false, null, null) : new(true, r.Until, r.Statement);

    public static RestrictionResponse Map(RestrictionInfo r) => new(r.Id, r.DisplayName, r.Until, r.Statement, r.SetAt, r.Holds, r.LiftedAt, r.LiftStatement);

    public static AuthorViewResponse Map(AuthorView v) => new(
        v.DisplayName,
        v.Reasons.Select(r => new AuthorReasonResponse(r.Id, r.BillNumber, r.Side, r.Text, r.Status, r.CreatedAt)).ToList(),
        v.Actions.Select(m => new ModerationEntryResponse(m.TargetKind, m.Action, m.Statement, m.At)).ToList(),
        v.Restrictions.Select(Map).ToList());

    /// <summary>The moderators' contact address for objecting (configuration <c>Moderation:ContactAddress</c>), if set.</summary>
    public static string? Contact(IConfiguration configuration) =>
        configuration["Moderation:ContactAddress"] is { Length: > 0 } address ? address : null;

    /// <summary>The moderator for the audit log: the role that works in this session (moderator first).</summary>
    public static async Task<StaffActor> ModeratorAsync(IIdentityModule identity, ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken)
    {
        var session = await identity.GetStaffSessionAsync(user, cancellationToken);
        var role = session.Has(Roles.Moderator) ? Roles.Moderator : Roles.Administrator;
        return new StaffActor(user.UserId() ?? throw new InvalidOperationException("A moderator is signed in."), role, ipHash);
    }
}

/// <summary>
/// Server-side <see cref="IAppealsApi"/> (design 3.1): the server-rendered pages and the prerendered interactive
/// components call the module in-process, as the signed-in person of the current request.
/// </summary>
internal sealed class ServerAppealsApi(IAppealsModule appeals, BillListReads list, TakingSides sides, IHttpContextAccessor accessor, ErrorMapper errors) : IAppealsApi
{
    private static readonly Error SignInRequired = Error.Unauthorized("auth.required");

    public async Task<ApiResult<AppealRulesResponse>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        ApiResult<AppealRulesResponse>.Ok(AppealMapping.Map(await appeals.RulesAsync(cancellationToken)));

    public async Task<ApiResult<BillListResponse>> GetAppealsAsync(string? search = null, bool planned = false, bool closed = false, string? cursor = null, string? sort = null, int? limit = null, CancellationToken cancellationToken = default) =>
        ApiResult<BillListResponse>.Ok(await list.ListAsync(new BillListQuery(search, planned, closed, cursor, sort, BillListLimits.Of(limit?.ToString(System.Globalization.CultureInfo.InvariantCulture))), cancellationToken));

    public async Task<ApiResult<BillRecordResponse>> GetRecordAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await appeals.GetAsync(number, cancellationToken), AppealMapping.Map);

    public async Task<ApiResult<IReadOnlyList<DescriptionRevisionResponse>>> GetHistoryAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(
            await appeals.HistoryAsync(number, cancellationToken),
            rows => (IReadOnlyList<DescriptionRevisionResponse>)rows.Select(AppealMapping.Map).ToList());

    public async Task<ApiResult<ReasonPageResponse>> GetReasonsAsync(string number, string side, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await appeals.ReasonsAsync(number, side, cursor, limit, cancellationToken), AppealMapping.Map);

    public async Task<ApiResult<ReasonOnBillResponse>> GetReasonAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await appeals.ReasonAsync(reasonId, cancellationToken), AppealMapping.Map);

    public async Task<ApiResult<MyBillRecordResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await appeals.MineAsync(number, id, cancellationToken), AppealMapping.Map)
            : ApiResult<MyBillRecordResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult> TakePositionAsync(string number, PositionRequest request, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => sides.TakeAsync(number, id, request.Side, cancellationToken));

    public async Task<ApiResult> ClearPositionAsync(string number, PositionRequest? request = null, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => sides.ClearAsync(number, id, request?.Side, cancellationToken));

    public async Task<ApiResult<IReadOnlyList<ReasonResponse>>> FindSimilarAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(
            await appeals.FindSimilarAsync(number, AppealMapping.ToModule(request), cancellationToken),
            list => (IReadOnlyList<ReasonResponse>)list.Select(AppealMapping.Map).ToList());

    public async Task<ApiResult<ReasonCreatedResponse>> WriteReasonAsync(string number, ReasonRequest request, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await appeals.WriteReasonAsync(number, id, AppealMapping.ToModule(request), cancellationToken), r => new ReasonCreatedResponse(r.Id, r.Status))
            : ApiResult<ReasonCreatedResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult<ReasonCreatedResponse>> EditReasonAsync(Guid reasonId, ReasonRequest request, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await appeals.EditReasonAsync(reasonId, id, AppealMapping.ToModule(request), cancellationToken), r => new ReasonCreatedResponse(r.Id, r.Status))
            : ApiResult<ReasonCreatedResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult> EndorseAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.EndorseAsync(reasonId, id, cancellationToken));

    public async Task<ApiResult> RemoveEndorsementAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.RemoveEndorsementAsync(reasonId, id, cancellationToken));

    public async Task<ApiResult> WithdrawReasonAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.WithdrawReasonAsync(reasonId, id, cancellationToken));

    public async Task<ApiResult<MyRestrictionResponse>> GetMyRestrictionAsync(CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? ApiResult<MyRestrictionResponse>.Ok(AppealMapping.Map(await appeals.MyRestrictionAsync(id, cancellationToken)))
            : ApiResult<MyRestrictionResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult> ReportAsync(ReportRequest request, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.ReportAsync(id, AppealMapping.ToModule(request), cancellationToken));

    public async Task<ApiResult> FlagDateAsync(string number, FlagRequest request, CancellationToken cancellationToken = default) =>
        await AsMemberAsync(id => appeals.FlagDateAsync(number, id, new NewFlag(request.Kind, request.Link, request.Note, request.Date), cancellationToken));

    private Guid? UserId => accessor.HttpContext?.User.UserId();

    private async Task<ApiResult> AsMemberAsync(Func<Guid, Task<Result<Unit>>> action) =>
        UserId is { } id ? errors.ToApiResult(await action(id)) : ApiResult.Fail(errors.ToProblem(SignInRequired));
}

/// <summary>Server-side <see cref="IModerationApi"/>, for the prerendered moderation page.</summary>
internal sealed class ServerModerationApi(
    IAppealsModeration moderation, Lt2.Notifications.IDigests digests, IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors,
    IConfiguration configuration, ModerationCountReads counts) : IModerationApi
{
    public async Task<ApiResult<AuthorViewResponse>> GetAuthorOfReasonAsync(Guid reasonId, CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? errors.ToApiResult(await moderation.AuthorOfReasonAsync(reasonId, cancellationToken), AppealMapping.Map)
            : ApiResult<AuthorViewResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));


    public Task<ApiResult<RestrictionCreatedResponse>> RestrictAuthorOfReasonAsync(Guid reasonId, RestrictionRequest request, CancellationToken cancellationToken = default) =>
        RestrictAsync(m => moderation.RestrictAuthorOfReasonAsync(reasonId, request.Until, request.Statement, m, AppealMapping.Contact(configuration), cancellationToken));


    public async Task<ApiResult<IReadOnlyList<RestrictionResponse>>> GetRestrictionsAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<IReadOnlyList<RestrictionResponse>>.Ok((await moderation.RestrictionsAsync(cancellationToken)).Select(AppealMapping.Map).ToList())
            : ApiResult<IReadOnlyList<RestrictionResponse>>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> LiftRestrictionAsync(Guid restrictionId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.LiftRestrictionAsync(restrictionId, request.Statement, m, AppealMapping.Contact(configuration), cancellationToken));

    private async Task<ApiResult<RestrictionCreatedResponse>> RestrictAsync(Func<StaffActor, Task<Result<Guid>>> action)
    {
        if (!await IsModeratorAsync())
        {
            return ApiResult<RestrictionCreatedResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        return errors.ToApiResult(await action(await AppealMapping.ModeratorAsync(identity, http.User, ipHasher.Hash(http), http.RequestAborted)), id => new RestrictionCreatedResponse(id));
    }

    public async Task<ApiResult<ModerationQueueResponse>> GetQueueAsync(string? list = null, CancellationToken cancellationToken = default)
    {
        if (!await IsModeratorAsync())
        {
            return ApiResult<ModerationQueueResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        return list is null or QueueLists.Records or QueueLists.Reasons
            ? ApiResult<ModerationQueueResponse>.Ok(AppealMapping.Map(
                await moderation.QueueAsync(list is null or QueueLists.Records, list is null or QueueLists.Reasons, cancellationToken)))
            : ApiResult<ModerationQueueResponse>.Fail(errors.ToProblem(Error.Validation("queue.list-invalid", "list")));
    }

    public async Task<ApiResult<ModerationCountsResponse>> GetCountsAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<ModerationCountsResponse>.Ok(await counts.CountsAsync(cancellationToken))
            : ApiResult<ModerationCountsResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<UndescribedBillsResponse>> GetUndescribedAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<UndescribedBillsResponse>.Ok(AppealMapping.Map(await moderation.UndescribedAsync(cancellationToken)))
            : ApiResult<UndescribedBillsResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> KeepRecordAsync(Guid recordId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.KeepRecordAsync(recordId, request.Statement, m, cancellationToken));

    public async Task<ApiResult<HiddenItemsResponse>> GetHiddenAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<HiddenItemsResponse>.Ok(AppealMapping.Map(await moderation.HiddenAsync(cancellationToken)))
            : ApiResult<HiddenItemsResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> RestoreRecordAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.RestoreRecordAsync(recordId, request.Statement, m, cancellationToken));

    public Task<ApiResult> RestoreReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.RestoreReasonAsync(reasonId, request.Statement, m, cancellationToken));


    public Task<ApiResult> HideRecordAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HideRecordAsync(recordId, request.Statement, m, cancellationToken));

    public Task<ApiResult> CloseRecordAsync(Guid recordId, CloseRecordRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.CloseRecordAsync(recordId, AppealMapping.ToModule(request), m, cancellationToken));

    public Task<ApiResult> ReopenRecordAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.ReopenRecordAsync(recordId, request.Statement, m, cancellationToken));

    public async Task<ApiResult<ModerationBillResponse>> GetBillAsync(string number, CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? errors.ToApiResult(await moderation.BillStateAsync(number, cancellationToken), AppealMapping.Map)
            : ApiResult<ModerationBillResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<BillRecordIdResponse>> EnsureRecordAsync(string number, CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? errors.ToApiResult(await moderation.EnsureRecordAsync(number, cancellationToken), id => new BillRecordIdResponse(id))
            : ApiResult<BillRecordIdResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> WriteDescriptionAsync(string number, ModeratorDescriptionRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.WriteDescriptionAsync(number, AppealMapping.ToModule(request), m, cancellationToken));

    public Task<ApiResult> ConfirmDescriptionAsync(string number, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.ConfirmDescriptionAsync(number, request.Statement, m, cancellationToken));

    public Task<ApiResult> RemoveDescriptionAsync(string number, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.RemoveDescriptionAsync(number, request.Statement, m, cancellationToken));

    public Task<ApiResult> SetThresholdAsync(Guid recordId, ModeratorThresholdRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.SetThresholdAsync(recordId, request.Threshold, request.Statement, m, cancellationToken));

    public Task<ApiResult> PublishReasonAsync(Guid reasonId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.PublishReasonAsync(reasonId, request.Statement, m, cancellationToken));

    public Task<ApiResult> HideReasonAsync(Guid reasonId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HideReasonAsync(reasonId, request.Statement, m, cancellationToken));

    public Task<ApiResult> MergeReasonAsync(Guid reasonId, MergeReasonRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.MergeReasonAsync(reasonId, request.IntoReasonId, request.Statement, m, cancellationToken));

    public Task<ApiResult> SetPlannedAsync(Guid recordId, ModeratorPlannedRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.SetPlannedAsync(recordId, request.Date, request.Stage, request.Statement, m, cancellationToken));

    public Task<ApiResult> HoldAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HoldAsync(recordId, request.Statement, m, cancellationToken));

    public Task<ApiResult> ReleaseAsync(Guid recordId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.ReleaseAsync(recordId, request.Statement, m, cancellationToken));

    public Task<ApiResult> UrgentAsync(Guid recordId, Lt2.Api.Contracts.Digests.UrgentDigestRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(async m =>
        {
            // Design 6.2: the staff passkey confirmed in the last ten minutes, the same check as the endpoint's.
            if (await FreshConfirmation.MissingAsync(identity, accessor.HttpContext!.User, cancellationToken) is { } missing)
            {
                return missing;
            }

            var made = await digests.UrgentAsync(recordId, request.Statement, m, cancellationToken);
            return made.IsSuccess ? Result.Ok() : made.Error!;
        });

    public async Task<ApiResult<AiStatusResponse>> GetAiAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<AiStatusResponse>.Ok(new AiStatusResponse(moderation.DraftAvailable, moderation.ReadsOfficialTexts))
            : ApiResult<AiStatusResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult<DescriptionDraftResponse>> DraftDescriptionAsync(string number, DescriptionDraftRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsModeratorAsync())
        {
            return ApiResult<DescriptionDraftResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        var moderator = await AppealMapping.ModeratorAsync(identity, http.User, ipHasher.Hash(http), cancellationToken);
        return errors.ToApiResult(await moderation.DraftDescriptionAsync(number, request.SourceText, moderator, cancellationToken), d => new DescriptionDraftResponse(d.Draft, d.PlainTitle, d.Statement, d.DescriptionUrl));
    }

    private Task<bool> IsModeratorAsync() => ServerStaff.IsAllowedAsync(accessor, Policies.Moderator);

    private Task<ApiResult> ActAsync(Func<StaffActor, Task<Result<Unit>>> action) => ServerStaff.ActAsync(accessor, identity, ipHasher, errors, action);
}

/// <summary>
/// The staff checks of the server-side API clients: the same policies the endpoints require, applied to the current
/// request, and a moderator's action run as that moderator.
/// </summary>
internal static class ServerStaff
{
    public static async Task<bool> IsAllowedAsync(IHttpContextAccessor accessor, string policy)
    {
        var http = accessor.HttpContext;
        if (http is null)
        {
            return false;
        }

        // The staff check reads the request's one Identity context; components prerendered side by side on one page (the
        // moderators' lists) ask at the same time, so the checks of one request take turns.
        if (http.Items[typeof(ServerStaff)] is not SemaphoreSlim turns)
        {
            http.Items[typeof(ServerStaff)] = turns = new SemaphoreSlim(1, 1);
            http.Response.RegisterForDispose(turns);
        }

        await turns.WaitAsync(http.RequestAborted);
        try
        {
            var authorization = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
            return (await authorization.AuthorizeAsync(http.User, http, policy)).Succeeded;
        }
        finally
        {
            turns.Release();
        }
    }

    /// <summary>A moderator's action, refused unless the request passes <see cref="Policies.Moderator"/>.</summary>
    public static async Task<ApiResult> ActAsync(
        IHttpContextAccessor accessor, IIdentityModule identity, IpHasher ipHasher, ErrorMapper errors, Func<StaffActor, Task<Result<Unit>>> action)
    {
        if (!await IsAllowedAsync(accessor, Policies.Moderator))
        {
            return ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        return errors.ToApiResult(await action(await AppealMapping.ModeratorAsync(identity, http.User, ipHasher.Hash(http), http.RequestAborted)));
    }
}

/// <summary>Server-side <see cref="IAdminApi"/>, for the prerendered settings page.</summary>
internal sealed class ServerAdminApi(
    ISettingsAdministration settings, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors, IInvitations invitations, IOrganisations organisations,
    IIdentityAdministration administration, IConfiguration configuration, IPoliticians politicians, IMpDirectory mps, Lt2.Appeals.IAuthorLookup authors) : IAdminApi
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
        if (await Endpoints.Admin.FreshConfirmation.MissingAsync(http.RequestServices.GetRequiredService<IIdentityModule>(), http.User, cancellationToken) is { } missing)
        {
            return ApiResult<SettingResponse>.Fail(errors.ToProblem(missing));
        }

        var result = await settings.UpdateAsync(key, request.Value, http.User.UserId()!.Value, Roles.Administrator, ipHasher.Hash(http), cancellationToken);
        return errors.ToApiResult(result, SettingMapping.ToResponse);
    }

    public async Task<ApiResult<Lt2.Api.Contracts.Me.InvitationStatsResponse>> GetInvitationStatsAsync(string? code = null, CancellationToken cancellationToken = default) =>
        await IsAdministratorAsync()
            ? ApiResult<Lt2.Api.Contracts.Me.InvitationStatsResponse>.Ok(await InvitationMapping.MapAsync(await invitations.StatsAsync(60, code, cancellationToken), organisations, cancellationToken))
            : ApiResult<Lt2.Api.Contracts.Me.InvitationStatsResponse>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult> SwitchInvitationAsync(Guid invitationId, bool on, StatementRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync())
        {
            return ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        return errors.ToApiResult(await invitations.SwitchAsync(invitationId, on, request.Statement, http.User.UserId()!.Value, Roles.Administrator, ipHasher.Hash(http), cancellationToken));
    }

    public async Task<ApiResult<IReadOnlyList<Lt2.Api.Contracts.Mps.AdminMpLinkResponse>>> GetMpLinksAsync(CancellationToken cancellationToken = default) =>
        await IsAdministratorAsync()
            ? ApiResult<IReadOnlyList<Lt2.Api.Contracts.Mps.AdminMpLinkResponse>>.Ok(await MpLinkList.ReadAsync(politicians, mps, cancellationToken))
            : ApiResult<IReadOnlyList<Lt2.Api.Contracts.Mps.AdminMpLinkResponse>>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public async Task<ApiResult> EndMpLinkAsync(string slug, StatementRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync())
        {
            return ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        if (await Endpoints.Admin.FreshConfirmation.MissingAsync(http.RequestServices.GetRequiredService<IIdentityModule>(), http.User, cancellationToken) is { } missing)
        {
            return ApiResult.Fail(errors.ToProblem(missing));
        }

        return errors.ToApiResult(await politicians.EndByAdministratorAsync(
            slug, request.Statement, new StaffActor(http.User.UserId()!.Value, Roles.Administrator, ipHasher.Hash(http)), cancellationToken));
    }

    public Task<ApiResult> LockAccountAsync(Lt2.Api.Contracts.Admin.AccountLockRequest request, CancellationToken cancellationToken = default) =>
        SetLockAsync(request, locked: true, cancellationToken);

    public Task<ApiResult> UnlockAccountAsync(Lt2.Api.Contracts.Admin.AccountLockRequest request, CancellationToken cancellationToken = default) =>
        SetLockAsync(request, locked: false, cancellationToken);

    private async Task<ApiResult> SetLockAsync(Lt2.Api.Contracts.Admin.AccountLockRequest request, bool locked, CancellationToken cancellationToken)
    {
        var contact = AppealMapping.Contact(configuration);
        return await ActAsLockAdministratorAsync(administrator => locked
            ? administration.LockAccountAsync(request.Email, request.Statement, administrator, contact, cancellationToken)
            : administration.UnlockAccountAsync(request.Email, request.Statement, administrator, contact, cancellationToken), cancellationToken);
    }

    public Task<ApiResult> LockAuthorAsync(Lt2.Api.Contracts.Admin.AuthorLockRequest request, CancellationToken cancellationToken = default) =>
        ActAsLockAdministratorAsync(
            administrator => Endpoints.Admin.AuthorLock.LockAsync(request, administrator, AppealMapping.Contact(configuration), authors, administration, cancellationToken), cancellationToken);

    public async Task<ApiResult<IReadOnlyList<Lt2.Api.Contracts.Admin.LockedAccountResponse>>> GetLockedAccountsAsync(CancellationToken cancellationToken = default) =>
        await IsAdministratorAsync()
            ? ApiResult<IReadOnlyList<Lt2.Api.Contracts.Admin.LockedAccountResponse>>.Ok(await Endpoints.Admin.AuthorLock.ListAsync(administration, cancellationToken))
            : ApiResult<IReadOnlyList<Lt2.Api.Contracts.Admin.LockedAccountResponse>>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> UnlockAccountByIdAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsLockAdministratorAsync(
            administrator => administration.UnlockAccountByIdAsync(id, request.Statement, administrator, AppealMapping.Contact(configuration), cancellationToken), cancellationToken);

    /// <summary>An administrator only, with a fresh passkey confirmation (design 8.3, 6.2), then the action as them.</summary>
    private async Task<ApiResult> ActAsLockAdministratorAsync(Func<StaffActor, Task<Result<Unit>>> action, CancellationToken cancellationToken)
    {
        if (!await IsAdministratorAsync())
        {
            return ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden")));
        }

        var http = accessor.HttpContext!;
        if (await Endpoints.Admin.FreshConfirmation.MissingAsync(http.RequestServices.GetRequiredService<IIdentityModule>(), http.User, cancellationToken) is { } missing)
        {
            return ApiResult.Fail(errors.ToProblem(missing));
        }

        return errors.ToApiResult(await action(new StaffActor(http.User.UserId()!.Value, Roles.Administrator, ipHasher.Hash(http))));
    }

    private Task<bool> IsAdministratorAsync() => ServerStaff.IsAllowedAsync(accessor, Policies.Administrator);
}
