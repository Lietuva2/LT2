using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Organisations;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>Hand-written mapping between the organisations of the Appeals module and the API contracts (design 8.14.14).</summary>
internal static class OrganisationMapping
{
    public static OrganisationLabelResponse Map(OrganisationLabel l) => new(l.ConfirmedBy, l.Domain);

    public static StatementResponse Map(StatementInfo s) => new(s.Id, s.Stance, s.Text, s.Link, s.LinkHost, s.Goal, s.Progress, s.PublishedAt, s.EditedAt, s.Status) { Comments = s.Comments };

    public static OrganisationListResponse Map(OrganisationListPage p) =>
        new(p.Items.Select(i => new OrganisationSummaryResponse(i.Slug, i.Name, Map(i.Label), i.Status)).ToList(), p.Total);

    /// <summary>The profile with how many follow the organisation (design 8.11.2): a number only.</summary>
    public static async Task<OrganisationProfileResponse> MapAsync(OrganisationProfile p, IFollowerCounts followers, CancellationToken ct) =>
        Map(p) with { Followers = await followers.OrganisationAsync(p.Id, ct) };

    public static OrganisationProfileResponse Map(OrganisationProfile p) => new(
        p.Id, p.Slug, p.Name, p.Description, p.Website, p.Website is { } website ? UserLink.Host(website) : null, p.LegalCode, p.DeclaredLobbyist, Map(p.Label),
        p.Status, p.BlockStatement, p.RegisteredAt, p.ClosedAt,
        p.History.Select(h => new ProfileChangeResponse(h.At, h.Field, h.OldValue, h.NewValue)).ToList(),
        p.Statements.Select(s => new ProfileStatementResponse(
            AppealMapping.Map(s.Bill), Map(s.Statement), s.BillOpen,
            s.Earlier.Select(v => new StatementVersionResponse(v.ReplacedAt, v.Stance, v.Text, v.Link, v.Goal)).ToList())).ToList());

    public static BillStatementsResponse Map(BillStatements b) =>
        new(b.BillNumber, b.Open, b.Items.Select(i => new BillStatementResponse(i.OrganisationId, i.Slug, i.Name, Map(i.Label), Map(i.Statement))).ToList());

    public static MyOrganisationResponse Map(MyOrganisation m) => new(m.Slug, m.Name, m.Status, Map(m.Label));

    public static OrganisationManagementResponse Map(OrganisationManagement m) => new(
        m.IsRepresentative,
        m.Representatives.Select(r => new RepresentativeResponse(r.Id, r.DisplayName, r.IsMe, r.Since)).ToList(),
        m.DomainConfirmation,
        m.WebsiteHost,
        m.Statements.Select(s => new RepresentedStatementResponse(AppealMapping.Map(s.Bill), Map(s.Statement))).ToList(),
        m.Refusal);

    public static MyBillOrganisationResponse Map(MyBillOrganisation m) =>
        new(m.OrganisationId, m.Slug, m.Name, m.Status, Map(m.Label), m.Statement is { } s ? Map(s) : null, m.Refusal);

    public static NewOrganisation ToModule(OrganisationRequest r) => new(r.Name, r.Description, r.Website, r.LegalCode, r.DeclaredLobbyist);

    public static NewStatement ToModule(OrganisationStatementRequest r) => new(r.Stance, r.Text, r.Link, r.Goal);

    public static InviteCreatedResponse Map(InviteCreated i) => new(i.Token, OrganisationLimits.InvitePath(i.Token), i.ExpiresAt);

    public static QueueStatementResponse Map(QueueStatement s) => new(
        s.Id, AppealMapping.Map(s.Bill), s.OrganisationId, s.Slug, s.OrganisationName, Map(s.Label), s.Stance, s.Text, s.Link, s.Goal, s.Status, s.Seen, s.Flags,
        s.Reports.Select(AppealMapping.Map).ToList(), s.PublishedAt, s.BillFinished);

    public static OrganisationModerationResponse Map(OrganisationModerationItems q) => new(
        q.Organisations.Select(o => new QueueOrganisationResponse(
            o.Id, o.Slug, o.Name, o.Description, o.Website, o.LegalCode, o.DeclaredLobbyist, Map(o.Label), o.Status, o.Seen, o.Changed, o.Flags,
            o.Reports.Select(AppealMapping.Map).ToList(), o.History.Select(h => new ProfileChangeResponse(h.At, h.Field, h.OldValue, h.NewValue)).ToList(),
            o.ConfirmationNote, o.DomainBarred, o.RegisteredAt)).ToList(),
        q.Statements.Select(Map).ToList());

    public static HiddenOrganisationsResponse Map(HiddenOrganisationItems h) => new(
        h.Organisations.Select(o => new HiddenOrganisationResponse(o.Id, o.Slug, o.Name, o.BlockedAt, o.BlockedBy, o.Statement)).ToList(),
        h.Statements.Select(s => new HiddenStatementResponse(s.Id, AppealMapping.Map(s.Bill), s.OrganisationName, s.Stance, s.Text, s.HiddenAt, s.HiddenBy, s.Statement, s.Restorable)).ToList());
}

/// <summary>
/// The organisation's invitation code for its representatives (design 8.14.14, stage B), composed by the host: the Appeals
/// module says whether the caller represents the organisation, whether it is active and which bills it has public statements
/// on; the Identity module keeps the code, which knows only the organisation's id. Nothing here is logged.
/// </summary>
internal static class OrganisationInvitationMapping
{
    public static async Task<Result<OrganisationInvitationResponse>> GetAsync(
        IOrganisations organisations, IOrganisationInvitations codes, IConfiguration configuration, string slug, Guid userId, bool replace, CancellationToken ct)
    {
        var access = await organisations.CodeAccessAsync(slug, userId, ct);
        if (!access.IsSuccess)
        {
            return access.Error!;
        }

        var a = access.Value!;
        var code = replace
            ? await codes.ReplaceAsync(a.OrganisationId, a.Active, ct)
            : await codes.ForOrganisationAsync(a.OrganisationId, a.Active, userId, ct);
        if (!code.IsSuccess)
        {
            return code.Error!;
        }

        var c = code.Value!;
        var link = c.Code is null ? null : InvitationMapping.Link(c.Code);
        IReadOnlyList<OrganisationBillLinkResponse> bills = link is null
            ? []
            : a.Bills
                .Where(b => LandingPath.Accept("/seimas/" + b.Number) is not null)
                .Select(b => new OrganisationBillLinkResponse(AppealMapping.Map(b), LandingPath.ShareLink(link, "/seimas/" + b.Number)))
                .ToList();
        return new OrganisationInvitationResponse(c.Code, link, c.Completed, c.Usable, c.On, c.OffWithOrganisation, bills, c.On ? null : AppealMapping.Contact(configuration));
    }
}

/// <summary>
/// Server-side <see cref="IOrganisationsApi"/> (design 3.1): the server-rendered pages and the prerendered islands call the
/// module in-process, as the signed-in person of the current request.
/// </summary>
internal sealed class ServerOrganisationsApi(
    IOrganisations organisations, IOrganisationInvitations codes, IFollowerCounts followers, IConfiguration configuration, IHttpContextAccessor accessor, ErrorMapper errors) : IOrganisationsApi
{
    public Task<ApiResult<OrganisationInvitationResponse>> GetInvitationAsync(string slug, CancellationToken cancellationToken = default) =>
        InvitationAsync(slug, replace: false, cancellationToken);

    public Task<ApiResult<OrganisationInvitationResponse>> ReplaceInvitationAsync(string slug, CancellationToken cancellationToken = default) =>
        InvitationAsync(slug, replace: true, cancellationToken);

    private async Task<ApiResult<OrganisationInvitationResponse>> InvitationAsync(string slug, bool replace, CancellationToken ct) =>
        UserId is { } id
            ? errors.ToApiResult(await OrganisationInvitationMapping.GetAsync(organisations, codes, configuration, slug, id, replace, ct), r => r)
            : ApiResult<OrganisationInvitationResponse>.Fail(errors.ToProblem(SignInRequired));

    private static readonly Error SignInRequired = Error.Unauthorized("auth.required");

    private Guid? UserId => accessor.HttpContext?.User.UserId();

    public async Task<ApiResult<OrganisationListResponse>> GetOrganisationsAsync(string? search = null, CancellationToken cancellationToken = default) =>
        ApiResult<OrganisationListResponse>.Ok(OrganisationMapping.Map(await organisations.ListAsync(search, cancellationToken)));

    public async Task<ApiResult<OrganisationProfileResponse>> GetProfileAsync(string slug, CancellationToken cancellationToken = default) =>
        await organisations.ProfileAsync(slug, cancellationToken) is var found && found.IsSuccess
            ? ApiResult<OrganisationProfileResponse>.Ok(await OrganisationMapping.MapAsync(found.Value!, followers, cancellationToken))
            : errors.ToApiResult(found, OrganisationMapping.Map);

    public async Task<ApiResult<BillStatementsResponse>> GetOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await organisations.OnBillAsync(number, cancellationToken), OrganisationMapping.Map);

    public async Task<ApiResult<OrganisationCreatedResponse>> RegisterAsync(OrganisationRequest request, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await organisations.RegisterAsync(id, OrganisationMapping.ToModule(request), cancellationToken), c => new OrganisationCreatedResponse(c.Id, c.Slug))
            : ApiResult<OrganisationCreatedResponse>.Fail(errors.ToProblem(SignInRequired));

    public Task<ApiResult> ChangeAsync(string slug, OrganisationRequest request, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => organisations.ChangeAsync(slug, id, OrganisationMapping.ToModule(request), cancellationToken));

    public async Task<ApiResult<IReadOnlyList<MyOrganisationResponse>>> GetMineAsync(CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? ApiResult<IReadOnlyList<MyOrganisationResponse>>.Ok((await organisations.MineAsync(id, cancellationToken)).Select(OrganisationMapping.Map).ToList())
            : ApiResult<IReadOnlyList<MyOrganisationResponse>>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult<OrganisationManagementResponse>> GetManagementAsync(string slug, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await organisations.ManagementAsync(slug, id, cancellationToken), OrganisationMapping.Map)
            : ApiResult<OrganisationManagementResponse>.Fail(errors.ToProblem(SignInRequired));

    public Task<ApiResult> SendCodeAsync(string slug, ConfirmationCodeRequest request, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => organisations.SendCodeAsync(slug, id, request.Address, cancellationToken));

    public Task<ApiResult> ConfirmAsync(string slug, ConfirmationRequest request, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => organisations.ConfirmAsync(slug, id, request.Code, cancellationToken));

    public async Task<ApiResult<InviteCreatedResponse>> CreateInviteAsync(string slug, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await organisations.InviteAsync(slug, id, cancellationToken), OrganisationMapping.Map)
            : ApiResult<InviteCreatedResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult<InviteResponse>> GetInviteAsync(string token, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await organisations.InviteViewAsync(token, id, cancellationToken), v => new InviteResponse(v.Slug, v.Name, v.Usable, v.AlreadyRepresentative))
            : ApiResult<InviteResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult<InviteAcceptedResponse>> AcceptInviteAsync(string token, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await organisations.AcceptInviteAsync(token, id, cancellationToken), slug => new InviteAcceptedResponse(slug))
            : ApiResult<InviteAcceptedResponse>.Fail(errors.ToProblem(SignInRequired));

    public Task<ApiResult> RemoveRepresentativeAsync(string slug, Guid representativeId, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => organisations.RemoveRepresentativeAsync(slug, representativeId, id, cancellationToken));

    public Task<ApiResult> LeaveAsync(string slug, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => organisations.LeaveAsync(slug, id, cancellationToken));

    public Task<ApiResult> CloseAsync(string slug, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => organisations.CloseAsync(slug, id, cancellationToken));

    public async Task<ApiResult<IReadOnlyList<MyBillOrganisationResponse>>> GetMineOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await organisations.MineOnBillAsync(number, id, cancellationToken), list => (IReadOnlyList<MyBillOrganisationResponse>)list.Select(OrganisationMapping.Map).ToList())
            : ApiResult<IReadOnlyList<MyBillOrganisationResponse>>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult<StatementSavedResponse>> SaveStatementAsync(string number, string slug, OrganisationStatementRequest request, CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? errors.ToApiResult(await organisations.SaveStatementAsync(number, slug, id, OrganisationMapping.ToModule(request), cancellationToken), s => new StatementSavedResponse(s.Id, s.Status))
            : ApiResult<StatementSavedResponse>.Fail(errors.ToProblem(SignInRequired));

    public Task<ApiResult> WithdrawStatementAsync(string number, string slug, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => organisations.WithdrawStatementAsync(number, slug, id, cancellationToken));

    private async Task<ApiResult> AsMemberAsync(Func<Guid, Task<Result<Unit>>> action) =>
        UserId is { } id ? errors.ToApiResult(await action(id)) : ApiResult.Fail(errors.ToProblem(SignInRequired));
}

/// <summary>Server-side <see cref="IOrganisationModerationApi"/>, for the prerendered moderation pages.</summary>
internal sealed class ServerOrganisationModerationApi(
    IOrganisationModeration moderation, IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors, IConfiguration configuration)
    : IOrganisationModerationApi
{
    private static readonly Error Forbidden = Error.Forbidden("forbidden");

    public async Task<ApiResult<OrganisationModerationResponse>> GetQueueAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<OrganisationModerationResponse>.Ok(OrganisationMapping.Map(await moderation.QueueAsync(cancellationToken)))
            : ApiResult<OrganisationModerationResponse>.Fail(errors.ToProblem(Forbidden));

    public async Task<ApiResult<IReadOnlyList<QueueStatementResponse>>> GetOnBillAsync(string number, CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? errors.ToApiResult(await moderation.OnBillAsync(number, cancellationToken), list => (IReadOnlyList<QueueStatementResponse>)list.Select(OrganisationMapping.Map).ToList())
            : ApiResult<IReadOnlyList<QueueStatementResponse>>.Fail(errors.ToProblem(Forbidden));

    public async Task<ApiResult<HiddenOrganisationsResponse>> GetHiddenAsync(CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? ApiResult<HiddenOrganisationsResponse>.Ok(OrganisationMapping.Map(await moderation.HiddenAsync(cancellationToken)))
            : ApiResult<HiddenOrganisationsResponse>.Fail(errors.ToProblem(Forbidden));

    public Task<ApiResult> KeepAsync(Guid organisationId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.KeepAsync(organisationId, request.Statement, m, Contact, cancellationToken));

    public Task<ApiResult> BlockAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.BlockAsync(organisationId, request.Statement, m, Contact, cancellationToken));

    public Task<ApiResult> UnblockAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.UnblockAsync(organisationId, request.Statement, m, Contact, cancellationToken));

    public Task<ApiResult> ConfirmAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.ConfirmAsync(organisationId, request.Statement, m, Contact, cancellationToken));

    public Task<ApiResult> UnconfirmAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.WithdrawConfirmationAsync(organisationId, request.Statement, m, Contact, cancellationToken));

    public Task<ApiResult> PublishStatementAsync(Guid statementId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.PublishStatementAsync(statementId, request.Statement, m, Contact, cancellationToken));

    public Task<ApiResult> KeepStatementAsync(Guid statementId, ModerationRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.KeepStatementAsync(statementId, request.Statement, m, Contact, cancellationToken));

    public Task<ApiResult> HideStatementAsync(Guid statementId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.HideStatementAsync(statementId, request.Statement, m, Contact, cancellationToken));

    public Task<ApiResult> RestoreStatementAsync(Guid statementId, StatementRequest request, CancellationToken cancellationToken = default) =>
        ActAsync(m => moderation.RestoreStatementAsync(statementId, request.Statement, m, Contact, cancellationToken));

    public async Task<ApiResult<AuthorViewResponse>> GetWriterAsync(Guid statementId, CancellationToken cancellationToken = default) =>
        await IsModeratorAsync()
            ? errors.ToApiResult(await moderation.WriterOfStatementAsync(statementId, cancellationToken), AppealMapping.Map)
            : ApiResult<AuthorViewResponse>.Fail(errors.ToProblem(Forbidden));

    public async Task<ApiResult<RestrictionCreatedResponse>> RestrictWriterAsync(Guid statementId, RestrictionRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsModeratorAsync())
        {
            return ApiResult<RestrictionCreatedResponse>.Fail(errors.ToProblem(Forbidden));
        }

        var http = accessor.HttpContext!;
        var moderator = await AppealMapping.ModeratorAsync(identity, http.User, ipHasher.Hash(http), cancellationToken);
        return errors.ToApiResult(
            await moderation.RestrictWriterOfStatementAsync(statementId, request.Until, request.Statement, moderator, Contact, cancellationToken),
            id => new RestrictionCreatedResponse(id));
    }

    private string? Contact => AppealMapping.Contact(configuration);

    private Task<bool> IsModeratorAsync() => ServerStaff.IsAllowedAsync(accessor, Policies.Moderator);

    private Task<ApiResult> ActAsync(Func<StaffActor, Task<Result<Unit>>> action) => ServerStaff.ActAsync(accessor, identity, ipHasher, errors, action);
}
