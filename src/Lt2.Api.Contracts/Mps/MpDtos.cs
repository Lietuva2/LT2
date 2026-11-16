using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Mps;

/// <summary>Design 8.5 and 8.8: an MP's take-over of a profile, the office, and an administrator's end of a link.</summary>
public static class MpRoutes
{
    /// <summary><c>POST</c>: sends the take-over link to the official address (design 8.5.2, step 1).</summary>
    public const string Claim = ApiRoutes.Prefix + "/mps/{slug}/claim";

    /// <summary><c>DELETE</c>: the MP's account gives the profile up; an office member leaves.</summary>
    public const string Mine = ApiRoutes.Prefix + "/me/mp";

    /// <summary><c>POST</c>: the link's token and the new passkey (design 8.5.2, step 3).</summary>
    public const string Takeover = Mine + "/takeover";

    public const string Office = Mine + "/office";
    public const string OfficeInvitations = Office + "/invitations";
    public const string OfficeMember = Office + "/{id:guid}";
    public const string OfficeAcceptance = Office + "/acceptance";

    /// <summary><c>POST</c>, administrators: ends an MP's link with a statement and a fresh passkey confirmation.</summary>
    public const string EndLink = ApiRoutes.Prefix + "/admin/mps/{slug}/link/end";

    /// <summary>The page the take-over link opens; the token follows the <c>#</c> (design 6.8).</summary>
    public const string TakeoverPage = "/seimo-narys/perimti";

    /// <summary>The page an office member's invitation opens; the token follows the <c>#</c>.</summary>
    public const string OfficeInvitationPage = "/seimo-narys/biuras";

    public static string ClaimOf(string slug) => $"{ApiRoutes.Prefix}/mps/{Uri.EscapeDataString(slug)}/claim";

    public static string OfficeMemberOf(Guid id) => $"{Office}/{id}";

    public static string EndLinkOf(string slug) => $"{ApiRoutes.Prefix}/admin/mps/{Uri.EscapeDataString(slug)}/link/end";

    public static string OfficeInvitationPath(string token) => OfficeInvitationPage + "#" + Uri.EscapeDataString(token);
}

/// <summary>
/// Design 8.5.1: the account's own address is this MP's official address and the account is not linked; it is offered
/// "Perimti profilį". Only in the account's own <c>GET /me</c>.
/// </summary>
public sealed record MpOfferResponse(string Slug, string FirstName, string LastName);

/// <summary>
/// Design 8.5.4: the MP the account speaks for, in which part (<c>mp</c>: the MP's own account; <c>office</c>: an office
/// member), and whether this session may speak for the MP (<see cref="MayWrite"/>: a passkey session). Only in the account's
/// own answers.
/// </summary>
public sealed record MpLinkResponse(string Slug, string FirstName, string LastName, string Part, bool MayWrite);

/// <summary>The link's token and the passkey made in the browser for it (design 8.5.2, step 3).</summary>
public sealed record MpTakeoverRequest
{
    public string Token { get; init; } = "";

    /// <summary>The output of the browser's <c>PublicKeyCredential.toJSON()</c>, as for adding a passkey.</summary>
    public string Credential { get; init; } = "";
}

/// <summary>An office member's one-time invitation token, read from the page's <c>#</c>.</summary>
public sealed record MpOfficeAcceptanceRequest
{
    public string Token { get; init; } = "";
}

/// <summary>An office member as the MP's account sees them (design 8.5.3). Nobody else sees who they are.</summary>
public sealed record MpOfficeMemberResponse(Guid Id, string? DisplayName, DateTimeOffset Since);

/// <summary>The MP's office: its members and how many it may have.</summary>
public sealed record MpOfficeResponse(IReadOnlyList<MpOfficeMemberResponse> Members, int Limit);

/// <summary>A new office invitation, shown once: the page's path with the token after the <c>#</c>, and when it stops working.</summary>
public sealed record MpOfficeInvitationResponse(string Path, DateTimeOffset ExpiresAt);

public sealed class MpTakeoverRequestValidator : AbstractValidator<MpTakeoverRequest>
{
    public MpTakeoverRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Token).Cascade(CascadeMode.Stop).NotEmpty().WithMessage(l["MpLink.Required"]).MaximumLength(100).WithMessage(l["MpLink.Required"]);
        RuleFor(x => x.Credential).NotEmpty().WithMessage(l["Passkey.Required"]).MaximumLength(16_384).WithMessage(l["Passkey.Required"]);
    }
}

public sealed class MpOfficeAcceptanceRequestValidator : AbstractValidator<MpOfficeAcceptanceRequest>
{
    public MpOfficeAcceptanceRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Token).Cascade(CascadeMode.Stop).NotEmpty().WithMessage(l["MpLink.Required"]).MaximumLength(100).WithMessage(l["MpLink.Required"]);
}

/// <summary>Design 8.5: what an account does as or for an MP. Only the account's own answers; nothing here is public.</summary>
public interface IMpApi
{
    /// <summary>"Perimti profilį": the link goes to the official address, which is the account's own.</summary>
    Task<ApiResult> ClaimAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Finishes the take-over with the link's token and a new passkey; the session becomes a passkey session.</summary>
    Task<ApiResult<MpLinkResponse>> TakeOverAsync(MpTakeoverRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gives the profile up (the MP's account) or leaves the office (an office member).</summary>
    Task<ApiResult> EndAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<MpOfficeResponse>> GetOfficeAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<MpOfficeInvitationResponse>> InviteOfficeMemberAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveOfficeMemberAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<ApiResult<MpLinkResponse>> AcceptOfficeInvitationAsync(MpOfficeAcceptanceRequest request, CancellationToken cancellationToken = default);
}
