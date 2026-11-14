using FluentValidation;
using Lt2.Api.Contracts.Appeals;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Organisations;

/// <summary>Design 8.14.14, stage A: the routes of organisations and their statements.</summary>
public static class OrganisationRoutes
{
    public const string Organisations = ApiRoutes.Prefix + "/organisations";
    public const string Organisation = Organisations + "/{slug}";
    public const string Management = Organisation + "/mine";
    public const string ConfirmationCode = Organisation + "/confirmation/code";
    public const string Confirmation = Organisation + "/confirmation";
    public const string Invites = Organisation + "/invites";
    public const string Representative = Organisation + "/representatives/{id:guid}";
    public const string Leave = Organisation + "/leave";
    public const string Close = Organisation + "/close";

    /// <summary>Design 8.14.14, stage B: the organisation's invitation code, to its representatives only.</summary>
    public const string Invitation = Organisation + "/invitation";
    public const string ReplaceInvitation = Invitation + "/replace";
    public const string Invite = ApiRoutes.Prefix + "/organisation-invites/{token}";
    public const string AcceptInvite = Invite + "/accept";
    public const string Mine = ApiRoutes.Prefix + "/me/organisations";
    public const string OnBill = ApiRoutes.Prefix + "/bills/{number}/organisations";
    public const string MineOnBill = OnBill + "/mine";
    public const string Statement = OnBill + "/{slug}/statement";

    public const string ModerationQueue = ApiRoutes.Prefix + "/moderation/organisations";
    public const string ModerationHidden = ModerationQueue + "/hidden";
    public const string ModerationBill = ApiRoutes.Prefix + "/moderation/bills/{number}/statements";
    public const string Keep = ModerationQueue + "/{id:guid}/keep";
    public const string Block = ModerationQueue + "/{id:guid}/block";
    public const string Unblock = ModerationQueue + "/{id:guid}/unblock";
    public const string Confirm = ModerationQueue + "/{id:guid}/confirm";
    public const string Unconfirm = ModerationQueue + "/{id:guid}/unconfirm";
    public const string PublishStatement = ApiRoutes.Prefix + "/moderation/statements/{id:guid}/publish";
    public const string HideStatement = ApiRoutes.Prefix + "/moderation/statements/{id:guid}/hide";
    public const string RestoreStatement = ApiRoutes.Prefix + "/moderation/statements/{id:guid}/restore";
    public const string StatementWriter = ApiRoutes.Prefix + "/moderation/statements/{id:guid}/writer";
    public const string RestrictStatementWriter = ApiRoutes.Prefix + "/moderation/statements/{id:guid}/restrict-writer";

    public static string List(string? search) =>
        string.IsNullOrWhiteSpace(search) ? Organisations : Organisations + "?q=" + Uri.EscapeDataString(search.Trim());

    public static string Of(string slug) => $"{Organisations}/{Uri.EscapeDataString(slug)}";

    public static string ManagementOf(string slug) => Of(slug) + "/mine";

    public static string ConfirmationCodeOf(string slug) => Of(slug) + "/confirmation/code";

    public static string ConfirmationOf(string slug) => Of(slug) + "/confirmation";

    public static string InvitesOf(string slug) => Of(slug) + "/invites";

    public static string RepresentativeOf(string slug, Guid id) => $"{Of(slug)}/representatives/{id}";

    public static string LeaveOf(string slug) => Of(slug) + "/leave";

    public static string CloseOf(string slug) => Of(slug) + "/close";

    public static string InvitationOf(string slug) => Of(slug) + "/invitation";

    public static string ReplaceInvitationOf(string slug) => Of(slug) + "/invitation/replace";

    public static string InviteOf(string token) => $"{ApiRoutes.Prefix}/organisation-invites/{Uri.EscapeDataString(token)}";

    public static string AcceptInviteOf(string token) => InviteOf(token) + "/accept";

    public static string OnBillOf(string number) => $"{ApiRoutes.Prefix}/bills/{Uri.EscapeDataString(number)}/organisations";

    public static string MineOnBillOf(string number) => OnBillOf(number) + "/mine";

    public static string StatementOf(string number, string slug) => $"{OnBillOf(number)}/{Uri.EscapeDataString(slug)}/statement";

    public static string ModerationBillOf(string number) => $"{ApiRoutes.Prefix}/moderation/bills/{Uri.EscapeDataString(number)}/statements";

    /// <summary>A moderation route with the target id filled in.</summary>
    public static string With(string route, Guid id) => route.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal);
}

/// <summary>Organisations and their statements for visitors and members (design 8.14.14).</summary>
public interface IOrganisationsApi
{
    Task<ApiResult<OrganisationListResponse>> GetOrganisationsAsync(string? search = null, CancellationToken cancellationToken = default);

    Task<ApiResult<OrganisationProfileResponse>> GetProfileAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>"Organizacijų pozicijos" on a bill: public, the same for everyone.</summary>
    Task<ApiResult<BillStatementsResponse>> GetOnBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<OrganisationCreatedResponse>> RegisterAsync(OrganisationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ChangeAsync(string slug, OrganisationRequest request, CancellationToken cancellationToken = default);

    /// <summary>"Mano organizacijos".</summary>
    Task<ApiResult<IReadOnlyList<MyOrganisationResponse>>> GetMineAsync(CancellationToken cancellationToken = default);

    /// <summary>The caller's own answer for the profile's management controls.</summary>
    Task<ApiResult<OrganisationManagementResponse>> GetManagementAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult> SendCodeAsync(string slug, ConfirmationCodeRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ConfirmAsync(string slug, ConfirmationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<InviteCreatedResponse>> CreateInviteAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult<InviteResponse>> GetInviteAsync(string token, CancellationToken cancellationToken = default);

    Task<ApiResult<OrganisationCreatedResponse>> AcceptInviteAsync(string token, CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveRepresentativeAsync(string slug, Guid representativeId, CancellationToken cancellationToken = default);

    Task<ApiResult> LeaveAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult> CloseAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>The caller's organisations and their statements on one bill: the caller's own answer, never cached.</summary>
    Task<ApiResult<IReadOnlyList<MyBillOrganisationResponse>>> GetMineOnBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<StatementSavedResponse>> SaveStatementAsync(string number, string slug, OrganisationStatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> WithdrawStatementAsync(string number, string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// The organisation's invitation code with its links and one number (design 8.14.14, stage B): the caller's own answer,
    /// to a representative only, never cached; made when first asked for while the organisation is active.
    /// </summary>
    Task<ApiResult<OrganisationInvitationResponse>> GetInvitationAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>A new code; the old one stops working; the number carries over.</summary>
    Task<ApiResult<OrganisationInvitationResponse>> ReplaceInvitationAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>Moderation of organisations and their statements (design 8.14.14). Needs an active moderator role.</summary>
public interface IOrganisationModerationApi
{
    Task<ApiResult<OrganisationModerationResponse>> GetQueueAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<QueueStatementResponse>>> GetOnBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<HiddenOrganisationsResponse>> GetHiddenAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> KeepAsync(Guid organisationId, ModerationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> BlockAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> UnblockAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Confirms by hand; the statement is the required note of what was checked.</summary>
    Task<ApiResult> ConfirmAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> UnconfirmAsync(Guid organisationId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> PublishStatementAsync(Guid statementId, ModerationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> HideStatementAsync(Guid statementId, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> RestoreStatementAsync(Guid statementId, StatementRequest request, CancellationToken cancellationToken = default);

    /// <summary>The statement's writer as a moderator sees them before restricting (design 8.3).</summary>
    Task<ApiResult<AuthorViewResponse>> GetWriterAsync(Guid statementId, CancellationToken cancellationToken = default);

    Task<ApiResult<RestrictionCreatedResponse>> RestrictWriterAsync(Guid statementId, RestrictionRequest request, CancellationToken cancellationToken = default);
}

public sealed class OrganisationRequestValidator : AbstractValidator<OrganisationRequest>
{
    public OrganisationRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Name).Must(n => (n ?? "").Trim().Length is >= OrganisationLimits.NameMinLength and <= OrganisationLimits.NameMaxLength)
            .WithMessage(l["Organisation.Name"]);
        RuleFor(x => x.Description).Must(d => d is null || d.Trim().Length <= OrganisationLimits.DescriptionMaxLength).WithMessage(l["Organisation.Description"]);
        RuleFor(x => x.Website).Must(w => UserLink.IsEmptyOrAcceptable(w, OrganisationLimits.WebsiteMaxLength)).WithMessage(l["Organisation.Website"]);
        RuleFor(x => x.LegalCode).Must(c => string.IsNullOrWhiteSpace(c) || (c.Trim().Length is >= 7 and <= 9 && c.Trim().All(char.IsAsciiDigit)))
            .WithMessage(l["Organisation.LegalCode"]);
    }
}

public sealed class OrganisationStatementRequestValidator : AbstractValidator<OrganisationStatementRequest>
{
    public OrganisationStatementRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Stance).Must(s => OrganisationLimits.Stances.Contains(s)).WithMessage(l["OrgStatement.Stance"]);
        RuleFor(x => x.Text).Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["OrgStatement.Required"])
            .Must(t => t.Trim().Length <= OrganisationLimits.StatementMaxLength).WithMessage(l["OrgStatement.TooLong"]);
        RuleFor(x => x.Link).Must(link => UserLink.IsEmptyOrAcceptable(link, OrganisationLimits.LinkMaxLength)).WithMessage(l["OrgStatement.Link"]);
        RuleFor(x => x.Goal).Must((x, goal) => goal is null || (OrganisationLimits.GoalStances.Contains(x.Stance) && goal is >= 1 and <= OrganisationLimits.GoalMax))
            .WithMessage(l["OrgStatement.Goal"]);
    }
}

public sealed class ConfirmationCodeRequestValidator : AbstractValidator<ConfirmationCodeRequest>
{
    public ConfirmationCodeRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Address).Must(a => !string.IsNullOrWhiteSpace(a) && a.Trim().Length <= 254 && a.Contains('@', StringComparison.Ordinal))
            .WithMessage(l["Confirm.Address"]);
}

public sealed class ConfirmationRequestValidator : AbstractValidator<ConfirmationRequest>
{
    public ConfirmationRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Code).Must(c => !string.IsNullOrWhiteSpace(c) && c.Trim().Length <= 12).WithMessage(l["Confirm.Code"]);
}
