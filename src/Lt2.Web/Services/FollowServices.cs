using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Follows;
using Lt2.Notifications;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>The caller's follows (design 8.11.2, 8.14.14) from the Notifications module. Only ever the caller's own.</summary>
internal static class FollowMapping
{
    public static async Task<MyFollowsResponse> MineAsync(IFollows follows, Guid userId, CancellationToken ct)
    {
        var mine = await follows.MineAsync(userId, ct);
        return new MyFollowsResponse(
            mine.Bills.Select(b => new FollowedBillResponse(
                b.Bill.Number,
                b.Bill.Title,
                AppealMapping.Map(b.Planned),
                b.LastStageResult is { } v ? new FollowedVoteResponse(v.VoteId, v.Stage, v.HeldAt, v.IsConsensus, v.For, v.Against, v.Abstain) : null)).ToList(),
            mine.Mps.Select(m => new FollowedMpResponse(m.Slug, m.FirstName, m.LastName)).ToList(),
            mine.Organisations.Select(o => new FollowedOrganisationResponse(
                o.Slug, o.Name, new Lt2.Api.Contracts.Organisations.OrganisationLabelResponse(o.ConfirmedBy, o.ConfirmedDomain), o.Status)).ToList());
    }
}

/// <summary>Server-side <see cref="IFollowsApi"/> for prerendered islands, as the signed-in person of the request.</summary>
internal sealed class ServerFollowsApi(IFollows follows, IHttpContextAccessor accessor, ErrorMapper errors) : IFollowsApi
{
    private static readonly Error SignInRequired = Error.Unauthorized("auth.required");

    public async Task<ApiResult<MyFollowsResponse>> GetMineAsync(CancellationToken cancellationToken = default) =>
        UserId is { } id
            ? ApiResult<MyFollowsResponse>.Ok(await FollowMapping.MineAsync(follows, id, cancellationToken))
            : ApiResult<MyFollowsResponse>.Fail(errors.ToProblem(SignInRequired));

    public Task<ApiResult> FollowBillAsync(string number, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => follows.FollowBillAsync(id, number, cancellationToken));

    public Task<ApiResult> UnfollowBillAsync(string number, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => follows.UnfollowBillAsync(id, number, cancellationToken));

    public Task<ApiResult> FollowMpAsync(string slug, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => follows.FollowMpAsync(id, slug, cancellationToken));

    public Task<ApiResult> UnfollowMpAsync(string slug, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => follows.UnfollowMpAsync(id, slug, cancellationToken));

    public Task<ApiResult> FollowOrganisationAsync(string slug, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => follows.FollowOrganisationAsync(id, slug, cancellationToken));

    public Task<ApiResult> UnfollowOrganisationAsync(string slug, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => follows.UnfollowOrganisationAsync(id, slug, cancellationToken));

    public async Task<ApiResult<NoticeSettingsResponse>> GetNoticesAsync(CancellationToken cancellationToken = default)
    {
        if (UserId is not { } id)
        {
            return ApiResult<NoticeSettingsResponse>.Fail(errors.ToProblem(SignInRequired));
        }

        var n = await follows.NoticesAsync(id, cancellationToken);
        return ApiResult<NoticeSettingsResponse>.Ok(new NoticeSettingsResponse(n.Planned, n.Voted, n.Statements, n.Responses));
    }

    public Task<ApiResult> SetNoticesAsync(NoticeSettingsRequest request, CancellationToken cancellationToken = default) =>
        AsMemberAsync(id => follows.SetNoticesAsync(id, new NoticeSettings(request.Planned, request.Voted, request.Statements, request.Responses), cancellationToken));

    private Guid? UserId => accessor.HttpContext?.User.UserId();

    private async Task<ApiResult> AsMemberAsync(Func<Guid, Task<Result<Unit>>> action) =>
        UserId is { } id ? errors.ToApiResult(await action(id)) : ApiResult.Fail(errors.ToProblem(SignInRequired));
}
