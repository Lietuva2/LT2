using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>
/// A member's public page, composed by the host (design 8.25.6) from Identity (the name, "Apie mane", the profile id, the
/// party only while the member chose to show it, 8.25.9),
/// Notifications (the number of followers) and Appeals (the published arguments and comments). Nothing private of the member reaches it.
/// </summary>
internal sealed class MemberReads(IPublicProfiles profiles, IFollowerCounts followers, IMemberTexts texts)
{
    /// <summary>The public page's head: <c>member.not-found</c> (404) for an id never issued, <c>member.gone</c> (410) for a retired one.</summary>
    public async Task<Result<MemberProfileResponse>> MemberAsync(string id, CancellationToken ct)
    {
        var (state, profile) = await profiles.FindAsync(id, ct);
        return state switch
        {
            ProfileIdState.Public when profile is not null =>
                new MemberProfileResponse(
                    profile.ProfileId,
                    profile.DisplayName,
                    profile.About,
                    await followers.MemberAsync(profile.ProfileId, ct),
                    ProfileRoutes.Page(profile.ProfileId, profile.DisplayName),
                    await profiles.ShownPartyAsync(profile.ProfileId, ct)),
            ProfileIdState.Retired => Error.Gone("member.gone"),
            _ => Error.NotFound("member.not-found"),
        };
    }

    /// <summary>The member's published arguments and own-voice comments, newest first, the first <paramref name="shown"/> (10 at least).</summary>
    public async Task<Result<MemberTextsResponse>> TextsAsync(string id, int? shown, CancellationToken ct)
    {
        var (state, profile) = await profiles.FindAsync(id, ct);
        if (state != ProfileIdState.Public || profile is null)
        {
            return state == ProfileIdState.Retired ? Error.Gone("member.gone") : Error.NotFound("member.not-found");
        }

        var page = await texts.PublishedAsync(profile.UserId, ProfileLimits.Shown(shown), ct);
        return new MemberTextsResponse(
            page.Items.Select(a => new MemberTextResponse(a.Id, a.Kind, new BillRefResponse(a.BillNumber, a.BillTitle), a.Side, a.On, a.Text, a.PublishedAt)).ToList(),
            page.More);
    }
}

/// <summary>
/// The "Profilis" form (design 8.28.5) as the host answers it: the Identity module's state with the parties to choose from
/// (the MP directory) and <c>Digest:PartyGroupMinimum</c>, which the party's text states (design 8.4); saved whole by the
/// module, which checks the party against the same list.
/// </summary>
internal sealed class MyProfileForms(IMemberProfiles profiles, IMpDirectory mps, ISettings settings)
{
    public async Task<Result<MyProfileResponse>> MineAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken ct) =>
        await ToResponseAsync(await profiles.MineAsync(user, ct), await mps.PartiesAsync(ct), ct);

    public async Task<Result<MyProfileResponse>> SaveAsync(System.Security.Claims.ClaimsPrincipal user, ProfileRequest request, string? ipHash, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var choices = await mps.PartiesAsync(ct);
        return await ToResponseAsync(await profiles.SaveAsync(user, ProfileMapping.ToModule(request), choices, ipHash, ct), choices, ct);
    }

    private async Task<Result<MyProfileResponse>> ToResponseAsync(Result<MyProfileState> state, IReadOnlyList<string> choices, CancellationToken ct) =>
        state.IsSuccess
            ? ProfileMapping.Map(state.Value, choices, await settings.GetAsync(SettingKeys.DigestPartyGroupMinimum, ct))
            : state.Error!;
}

internal static class ProfileMapping
{
    public static MyProfileResponse Map(MyProfileState s, IReadOnlyList<string> choices, int groupMinimum) => new()
    {
        DisplayName = s.DisplayName,
        About = s.About,
        AboutHidden = s.AboutHidden,
        AboutHiddenStatement = s.AboutHiddenStatement,
        ProfileId = s.ProfileId,
        PublicSince = s.PublicSince,
        Address = s.Address,
        PublicConsentVersion = s.ConsentVersion,
        TextMaxLength = s.TextMaxLength,
        Party = s.Party,
        PartyChoices = choices,
        PartyGroupMinimum = groupMinimum,
        PartyPublic = s.PartyPublic,
        PartyPublicConsentVersion = PartyPublicConsent.TextVersion,
    };

    public static ProfileForm ToModule(ProfileRequest r) =>
        new(r.DisplayName, r.About, r.Public, r.PublicConsentVersion, r.Party, r.PartyConsent, r.PartyPublic, r.PartyPublicConsentVersion);

    public static IReadOnlyList<ProfileModerationResponse> Map(IReadOnlyList<ProfileModerationItem> items) =>
        items.Select(i => new ProfileModerationResponse(
                i.Id, i.ProfileId, i.DisplayName, i.About, i.HiddenAt, i.HiddenStatement, i.Restorable,
                i.Reports.Select(r => new ProfileReportResponse(r.Id, r.Ground, r.Text, r.CreatedAt)).ToList()))
            .ToList();
}

/// <summary>Server-side <see cref="IProfilesApi"/> for the server-rendered page and prerendered islands, as the request's person.</summary>
internal sealed class ServerProfilesApi(IMemberProfiles profiles, MyProfileForms forms, MemberReads reads, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors) : IProfilesApi
{
    private static readonly Error SignInRequired = Error.Unauthorized("auth.required");

    private HttpContext? Http => accessor.HttpContext;

    /// <summary>
    /// The person's own profile, read once per request: islands prerendered side by side on one page ask for it at the same
    /// time ("Sekti" and "Pranešti apie profilį" on a member's page, design 8.25.6), and the request has one Identity context,
    /// which cannot run two reads at once (browser look, 2026-11-17: the page answered 500 to a signed-in member).
    /// </summary>
    public Task<ApiResult<MyProfileResponse>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        if (Http is not { } http)
        {
            return Task.FromResult(ApiResult<MyProfileResponse>.Fail(errors.ToProblem(SignInRequired)));
        }

        if (http.Items[typeof(ServerProfilesApi)] is not Task<ApiResult<MyProfileResponse>> mine)
        {
            http.Items[typeof(ServerProfilesApi)] = mine = ReadMineAsync(http, cancellationToken);
        }

        return mine;
    }

    private async Task<ApiResult<MyProfileResponse>> ReadMineAsync(HttpContext http, CancellationToken cancellationToken) =>
        errors.ToApiResult(await forms.MineAsync(http.User, cancellationToken), x => x);

    public async Task<ApiResult<MyProfileResponse>> SaveAsync(ProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (Http is not { } http)
        {
            return ApiResult<MyProfileResponse>.Fail(errors.ToProblem(SignInRequired));
        }

        http.Items.Remove(typeof(ServerProfilesApi));
        return errors.ToApiResult(await forms.SaveAsync(http.User, request, ipHasher.Hash(http), cancellationToken), x => x);
    }

    public async Task<ApiResult<MemberProfileResponse>> GetMemberAsync(string id, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.MemberAsync(id, cancellationToken), x => x);

    public async Task<ApiResult<MemberTextsResponse>> GetTextsAsync(string id, int shown, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await reads.TextsAsync(id, shown, cancellationToken), x => x);

    public async Task<ApiResult> ReportAsync(string id, MemberReportRequest request, CancellationToken cancellationToken = default) =>
        Http?.User.UserId() is { } userId
            ? errors.ToApiResult(await profiles.ReportAsync(userId, id, request.Ground, request.Text, cancellationToken))
            : ApiResult.Fail(errors.ToProblem(SignInRequired));

    // The moderators' list only to a moderator or an administrator in a staff-passkey session; their actions go over HTTP only
    // (the islands that use them run in the browser, design 3.1).
    public async Task<ApiResult<IReadOnlyList<ProfileModerationResponse>>> GetModerationAsync(CancellationToken cancellationToken = default) =>
        Http is { } http && (await http.RequestServices.GetRequiredService<IIdentityModule>().GetStaffSessionAsync(http.User, cancellationToken)).Has(Roles.Moderator, Roles.Administrator)
            ? ApiResult<IReadOnlyList<ProfileModerationResponse>>.Ok(ProfileMapping.Map(await profiles.QueueAsync(cancellationToken)))
            : ApiResult<IReadOnlyList<ProfileModerationResponse>>.Fail(errors.ToProblem(Error.Forbidden("forbidden")));

    public Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) => NotHere();

    public Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default) => NotHere();

    public Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default) => NotHere();

    private Task<ApiResult> NotHere() => Task.FromResult(ApiResult.Fail(errors.ToProblem(Error.Forbidden("forbidden"))));
}
