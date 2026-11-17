using FluentValidation;
using Lt2.Api.Contracts.Appeals;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Me;

// Design 8.25: a member's public profile by their own choice, "Apie mane", following a member and the shown number of
// followers. Who follows is in no answer; no public answer carries an account id.

/// <summary>
/// The caller's own profile for the "Profilis" screen: "Apie mane", whether a moderator hid it (and why), the public id while
/// "Viešas profilis" is on, the consent text's version in force and <c>Texts:MaxLength</c> (0 = no limit but the ceiling).
/// </summary>
public sealed record MyProfileResponse(
    string? About, bool AboutHidden, string? AboutHiddenStatement, string? ProfileId, DateTimeOffset? PublicSince, string ConsentVersion, int TextMaxLength,
    string? Address = null);

/// <summary>
/// "Apie mane" and the switch. Switching on sends <c>ConsentVersion</c>, the version of the text the form showed; switching
/// off withdraws the consent at once.
/// </summary>
public sealed record ProfileRequest
{
    public string? About { get; init; }

    public bool Public { get; init; }

    public string? ConsentVersion { get; init; }
}

/// <summary>A member's public page (design 8.25.2): the display name, "Apie mane" and the number of followers.</summary>
public sealed record MemberProfileResponse(string ProfileId, string DisplayName, string? About, int Followers, string Address);

/// <summary>
/// A published text on a member's page (design 8.25.2): <c>Kind</c> <c>argument</c> with the <c>Side</c> it was written for,
/// or <c>comment</c> (in the member's own voice) with <c>On</c>, the kind of text it stands under (<c>reason</c>,
/// <c>statement</c>, <c>response</c>); its bill, when it became public, its text. The link is the bill's page with
/// <c>#reason-{id}</c> or <c>#komentaras-{id}</c>.
/// </summary>
public sealed record MemberTextResponse(Guid Id, string Kind, BillRefResponse Bill, string? Side, string? On, string Text, DateTimeOffset PublishedAt);

/// <summary>The member's texts, newest first, the first <c>Shown</c>; <c>More</c> when there are more.</summary>
public sealed record MemberTextsResponse(IReadOnlyList<MemberTextResponse> Items, bool More);

/// <summary>A report of a member's public profile (design 8.25.6).</summary>
public sealed record MemberReportRequest
{
    /// <summary>One of <see cref="ProfileLimits.ReportGrounds"/>.</summary>
    public string Ground { get; init; } = "";

    public string? Text { get; init; }
}

/// <summary>A report as the moderators see it; never who reported.</summary>
public sealed record ProfileReportResponse(Guid Id, string Ground, string? Text, DateTimeOffset CreatedAt);

/// <summary>A reported profile or a hidden "Apie mane", for the moderators. <c>Id</c> is the key of their actions.</summary>
public sealed record ProfileModerationResponse(
    Guid Id, string? ProfileId, string? DisplayName, string? About, DateTimeOffset? HiddenAt, string? HiddenStatement, bool Restorable, IReadOnlyList<ProfileReportResponse> Reports);

/// <summary>How many members follow a thing (design 8.11.2): a number only.</summary>
public sealed record FollowersResponse(int Followers);

public static class ProfileLimits
{
    /// <summary>
    /// The version of the "Viešas profilis" consent text the form shows (<c>UiText</c> <c>PublicProfile.Consent</c>); the same as
    /// <c>PublicProfileConsent.TextVersion</c> in SharedKernel (a unit test). A new wording gets a new version in both.
    /// </summary>
    public const string ConsentVersion = "2026-11-16";

    /// <summary>The general report grounds and "Apsimeta kitu asmeniu ar pareigūnu" (design 8.25.6).</summary>
    public static readonly IReadOnlyList<string> ReportGrounds = ["impersonation", .. AppealLimits.ReportGrounds];

    /// <summary>The shown arguments on a member's page at first, and each "Rodyti daugiau" (design 8.25.2).</summary>
    public const int Page = 10;

    /// <summary>The most a page's address may ask for at once.</summary>
    public const int MaxShown = 500;

    /// <summary>The shown count a page's address asks for (<c>?rodyti=</c>), within 10 and <see cref="MaxShown"/>.</summary>
    public static int Shown(int? asked) => Math.Clamp(asked ?? Page, Page, MaxShown);
}

public static class ProfileRoutes
{
    public const string Mine = ApiRoutes.Prefix + "/me/profile";

    /// <summary><c>DELETE</c>: "Viešas profilis" off, nothing else changed (design 8.25.1; open behind the terms, 8.22.2).</summary>
    public const string MinePublic = Mine + "/public";
    public const string Member = ApiRoutes.Prefix + "/members/{id}";
    public const string Texts = ApiRoutes.Prefix + "/members/{id}/texts";
    public const string Follow = ApiRoutes.Prefix + "/members/{id}/follow";
    public const string Report = ApiRoutes.Prefix + "/members/{id}/report";
    public const string Moderation = ApiRoutes.Prefix + "/moderation/profiles";
    public const string Hide = ApiRoutes.Prefix + "/moderation/profiles/{id}/hide";
    public const string Keep = ApiRoutes.Prefix + "/moderation/profiles/{id}/keep";
    public const string Restore = ApiRoutes.Prefix + "/moderation/profiles/{id}/restore";

    /// <summary>
    /// The public page of a member's profile (design 8.25.2, owner 2026-11-16): <c>/nariai/{name-slug}-{id}</c> from the id and
    /// the current display name (<see cref="MemberAddress"/>); any other name part redirects to it.
    /// </summary>
    public static string Page(string id, string? displayName) => MemberAddress.Page(id, displayName);

    public static string MemberOf(string id) => $"{ApiRoutes.Prefix}/members/{Uri.EscapeDataString(id)}";

    public static string TextsOf(string id, int shown) => $"{MemberOf(id)}/texts?shown={shown}";

    public static string FollowOf(string id) => $"{MemberOf(id)}/follow";

    public static string ReportOf(string id) => $"{MemberOf(id)}/report";

    public static string ActionOf(Guid id, string action) => $"{ApiRoutes.Prefix}/moderation/profiles/{id:D}/{action}";
}

/// <summary>
/// Design 8.25: the caller's profile, a member's public page, reports and the moderators' side. The page itself answers 410
/// for a retired id and 404 for an unknown one (<see cref="ApiResult.Problem"/> with that status).
/// </summary>
public interface IProfilesApi
{
    Task<ApiResult<MyProfileResponse>> GetMineAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<MyProfileResponse>> SaveAsync(ProfileRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<MemberProfileResponse>> GetMemberAsync(string id, CancellationToken cancellationToken = default);

    Task<ApiResult<MemberTextsResponse>> GetTextsAsync(string id, int shown, CancellationToken cancellationToken = default);

    Task<ApiResult> ReportAsync(string id, MemberReportRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<ProfileModerationResponse>>> GetModerationAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The shared checks of the profile form: only the technical ceiling (the setting is checked where it is written).</summary>
public sealed class ProfileRequestValidator : AbstractValidator<ProfileRequest>
{
    public ProfileRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        ArgumentNullException.ThrowIfNull(l);
        RuleFor(x => x.About).Must(t => t is null || t.Trim().Length <= MemberTextLimits.Ceiling)
            .WithMessage(l["About.TooLong", MemberTextLimits.Number(MemberTextLimits.Ceiling)]);
    }
}

public sealed class MemberReportRequestValidator : AbstractValidator<MemberReportRequest>
{
    public MemberReportRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        ArgumentNullException.ThrowIfNull(l);
        RuleFor(x => x.Ground).Must(g => ProfileLimits.ReportGrounds.Contains(g)).WithMessage(l["Report.Ground"]);
        RuleFor(x => x.Text).Must(t => t is null || t.Trim().Length <= AppealLimits.ReportTextMaxLength).WithMessage(l["Report.TooLong"]);
    }
}
