using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>
/// A member's public page, composed by the host (design 8.25.6) from Identity (the name, "Apie mane", the profile id),
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
                new MemberProfileResponse(profile.ProfileId, profile.DisplayName, profile.About, await followers.MemberAsync(profile.ProfileId, ct)),
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

internal static class ProfileMapping
{
    public static MyProfileResponse Map(MyProfileState s) =>
        new(s.About, s.AboutHidden, s.AboutHiddenStatement, s.ProfileId, s.PublicSince, s.ConsentVersion, s.TextMaxLength);

    public static ProfileChange ToModule(ProfileRequest r) => new(r.About, r.Public, r.ConsentVersion);

    public static IReadOnlyList<ProfileModerationResponse> Map(IReadOnlyList<ProfileModerationItem> items) =>
        items.Select(i => new ProfileModerationResponse(
                i.Id, i.ProfileId, i.DisplayName, i.About, i.HiddenAt, i.HiddenStatement, i.Restorable,
                i.Reports.Select(r => new ProfileReportResponse(r.Id, r.Ground, r.Text, r.CreatedAt)).ToList()))
            .ToList();
}

/// <summary>Server-side <see cref="IProfilesApi"/> for the server-rendered page and prerendered islands, as the request's person.</summary>
internal sealed class ServerProfilesApi(IMemberProfiles profiles, MemberReads reads, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors) : IProfilesApi
{
    private static readonly Error SignInRequired = Error.Unauthorized("auth.required");

    private HttpContext? Http => accessor.HttpContext;

    public async Task<ApiResult<MyProfileResponse>> GetMineAsync(CancellationToken cancellationToken = default) =>
        Http is { } http
            ? errors.ToApiResult(await profiles.MineAsync(http.User, cancellationToken), ProfileMapping.Map)
            : ApiResult<MyProfileResponse>.Fail(errors.ToProblem(SignInRequired));

    public async Task<ApiResult<MyProfileResponse>> SaveAsync(ProfileRequest request, CancellationToken cancellationToken = default) =>
        Http is { } http
            ? errors.ToApiResult(await profiles.ChangeAsync(http.User, ProfileMapping.ToModule(request), ipHasher.Hash(http), cancellationToken), ProfileMapping.Map)
            : ApiResult<MyProfileResponse>.Fail(errors.ToProblem(SignInRequired));

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
