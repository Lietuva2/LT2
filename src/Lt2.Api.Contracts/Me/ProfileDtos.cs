using FluentValidation;
using Lt2.Api.Contracts.Appeals;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Me;

// Design 8.25: a member's public profile by their own choice, "Apie mane", following a member and the shown number of
// followers. Who follows is in no answer; no public answer carries an account id.

/// <summary>
/// The "Profilis" form's state (design 8.28.5), the caller's own: the display name; "Apie mane", whether a moderator hid it
/// (and why); the public id and address while "Viešas profilis" is on, its consent text's version in force and
/// <c>Texts:MaxLength</c> (0 = no limit but the ceiling); the party supported (private, design 5.2), the parties to choose
/// from, whether the party is shown on the public profile (design 8.25.9) and its box's consent text version in force; and
/// <c>Digest:PartyGroupMinimum</c> (design 8.4), which the party's text states.
/// </summary>
public sealed record MyProfileResponse
{
    public string? DisplayName { get; init; }

    public string? About { get; init; }

    public bool AboutHidden { get; init; }

    public string? AboutHiddenStatement { get; init; }

    public string? ProfileId { get; init; }

    public DateTimeOffset? PublicSince { get; init; }

    public string? Address { get; init; }

    public string PublicConsentVersion { get; init; } = ProfileLimits.ConsentVersion;

    public int TextMaxLength { get; init; }

    public string? Party { get; init; }

    public IReadOnlyList<string> PartyChoices { get; init; } = [];

    public int PartyGroupMinimum { get; init; }

    public bool PartyPublic { get; init; }

    public string PartyPublicConsentVersion { get; init; } = PartyPublicLimits.ConsentVersion;
}

/// <summary>
/// The whole "Profilis" form (design 8.28.5), saved whole or not at all by <c>PUT /me/profile</c>: the display name (empty
/// removes it), "Apie mane", "Viešas profilis" with <see cref="PublicConsentVersion"/> (the version of the text the form showed,
/// needed to switch it on), the party (null: none) with <see cref="PartyConsent"/> (needed to give or change it), and
/// "Rodyti viešame profilyje" with <see cref="PartyPublicConsentVersion"/>. An unticked box withdraws its consent at once. A
/// refusal names every refused field by these names (camelCase).
/// </summary>
public sealed record ProfileRequest
{
    public string? DisplayName { get; init; }

    public string? About { get; init; }

    public bool Public { get; init; }

    public string? PublicConsentVersion { get; init; }

    public string? Party { get; init; }

    public bool PartyConsent { get; init; }

    public bool PartyPublic { get; init; }

    public string? PartyPublicConsentVersion { get; init; }
}

/// <summary>
/// A member's public page (design 8.25.2): the display name, "Apie mane" and the number of followers; <c>Party</c> only while
/// the member ticked "Rodyti viešame profilyje" (design 8.25.9), null otherwise.
/// </summary>
public sealed record MemberProfileResponse(string ProfileId, string DisplayName, string? About, int Followers, string Address, string? Party = null);

/// <summary>
/// A published text on a member's page (design 8.25.2): <c>Kind</c> <c>argument</c> with the <c>Side</c> it was written for,
/// or <c>comment</c> (in the member's own voice) with <c>On</c>, the kind of text it stands under (<c>reason</c>,
/// <c>statement</c>, <c>response</c>); its bill, when it became public, its text. The link is
/// <c>BillTextLinks.ToReason</c> or <c>BillTextLinks.ToComment</c>, which land on it (design 8.23.6).
/// </summary>
public sealed record MemberTextResponse(Guid Id, string Kind, BillRefResponse Bill, string? Side, string? On, string Text, DateTimeOffset PublishedAt);

/// <summary>The member's texts, newest first, the first <c>Shown</c>; <c>More</c> when there are more.</summary>
public sealed record MemberTextsResponse(IReadOnlyList<MemberTextResponse> Items, bool More);

/// <summary>A report of a member's public profile (design 8.25.6).</summary>
public sealed record MemberReportRequest
{
    /// <summary>One of <see cref="ReportGrounds.Profile"/>.</summary>
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

    /// <summary>The texts a member's page shows at first (design 8.25.2).</summary>
    public const int Page = 10;

    /// <summary>The texts each "Rodyti daugiau" adds (owner, 2026-11-19: a list of rows, 50 a press).</summary>
    public const int Step = ListPaging.RowStep;

    /// <summary>The most a page's address may ask for at once.</summary>
    public const int MaxShown = 500;

    /// <summary>The shown count a page's address asks for (<c>?rodyti=</c>), within 10 and <see cref="MaxShown"/>.</summary>
    public static int Shown(int? asked) => Math.Clamp(asked ?? Page, Page, MaxShown);

    /// <summary>What "Rodyti daugiau" asks for: as many as shown and <see cref="Step"/> more, at most <see cref="MaxShown"/>.</summary>
    public static int More(int? asked) => ListPaging.Next(Shown(asked), Step, MaxShown);
}

public static class ProfileRoutes
{
    public const string Mine = ApiRoutes.Prefix + "/me/profile";

    /// <summary><c>DELETE</c>: "Viešas profilis" off, nothing else changed (design 8.25.1; open behind the terms, 8.22.2).</summary>
    public const string MinePublic = Mine + "/public";

    /// <summary><c>PUT</c>: the consent text in force accepted by a person whose profile is on under an earlier text (design 8.25.1).</summary>
    public const string MineConsent = Mine + "/consent";
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
    /// <summary>
    /// Design 8.25.1: accepts the public-profile consent text in force (<c>profile.consent-outdated</c> for another version,
    /// <c>profile.not-public</c> while the profile is off); nothing else changes.
    /// </summary>
    Task<ApiResult> AcceptConsentAsync(ProfileConsentRequest request, CancellationToken cancellationToken = default);

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

/// <summary>
/// The shared checks of the profile form: only technical ceilings. Every rule of the form (the name's length included) is
/// checked where it is written, all at once, so a refusal names every refused field (design 8.28.5).
/// </summary>
public sealed class ProfileRequestValidator : AbstractValidator<ProfileRequest>
{
    public ProfileRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        ArgumentNullException.ThrowIfNull(l);
        RuleFor(x => x.About).Must(t => t is null || t.Trim().Length <= MemberTextLimits.Ceiling)
            .WithMessage(l["About.TooLong", MemberTextLimits.Number(MemberTextLimits.Ceiling)]);
        RuleFor(x => x.DisplayName).MaximumLength(MemberTextLimits.Ceiling).WithMessage(l["DisplayName.TooLong"]);
        RuleFor(x => x.Party).MaximumLength(200);
        RuleFor(x => x.PublicConsentVersion).MaximumLength(50).WithMessage(l["Consent.Required"]);
        RuleFor(x => x.PartyPublicConsentVersion).MaximumLength(50).WithMessage(l["Consent.Required"]);
    }
}

/// <summary>The version of the public-profile consent text the person read; it must be the one in force (design 8.25.1).</summary>
public sealed record ProfileConsentRequest
{
    public string TextVersion { get; init; } = "";
}

public sealed class ProfileConsentRequestValidator : AbstractValidator<ProfileConsentRequest>
{
    public ProfileConsentRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.TextVersion).NotEmpty().WithMessage(l["Consent.Required"]).MaximumLength(50).WithMessage(l["Consent.Required"]);
}

public sealed class MemberReportRequestValidator : AbstractValidator<MemberReportRequest>
{
    public MemberReportRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        ArgumentNullException.ThrowIfNull(l);
        RuleFor(x => x.Ground).Must(g => ReportGrounds.Profile.Contains(g)).WithMessage(l["Report.Ground"]);
        RuleFor(x => x.Text).Must(t => t is null || t.Trim().Length <= AppealLimits.ReportTextMaxLength).WithMessage(l["Report.TooLong"]);
    }
}
