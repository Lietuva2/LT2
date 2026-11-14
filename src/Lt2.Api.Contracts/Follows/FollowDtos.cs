using Lt2.Api.Contracts.Appeals;

namespace Lt2.Api.Contracts.Follows;

// Design 8.11.2: following bills and MPs. Only the caller's own follows ever leave the server: no follower count, no list
// of who follows anything, never combined with sides or invitations.

/// <summary>The latest stage result of a followed bill.</summary>
public sealed record FollowedVoteResponse(long VoteId, string? Stage, DateTimeOffset HeldAt, bool IsConsensus, int? For, int? Against, int? Abstain);

/// <summary>A bill the caller follows, with its planned date or its last stage result (design 8.11.2, the account page).</summary>
public sealed record FollowedBillResponse(string Number, string Title, PlannedDateResponse? Planned, FollowedVoteResponse? LastStageResult);

/// <summary>An MP the caller follows.</summary>
public sealed record FollowedMpResponse(string Slug, string FirstName, string LastName);

/// <summary>
/// The caller's own follows (design 8.8 <c>GET /me/follows</c>); never cached. <c>Max</c> is how many bills and MPs together
/// may be followed.
/// </summary>
public sealed record MyFollowsResponse(IReadOnlyList<FollowedBillResponse> Bills, IReadOnlyList<FollowedMpResponse> Mps, int Max);

/// <summary>Which follow emails are on (design 8.11.2): a followed bill is planned; the Seimas voted on it.</summary>
public sealed record NoticeSettingsResponse(bool Planned, bool Voted);

public sealed record NoticeSettingsRequest
{
    public bool Planned { get; init; } = true;

    public bool Voted { get; init; } = true;
}

public static class FollowRoutes
{
    public const string Mine = ApiRoutes.Prefix + "/me/follows";
    public const string Notices = ApiRoutes.Prefix + "/me/notices";
    public const string Bill = ApiRoutes.Prefix + "/bills/{number}/follow";
    public const string Mp = ApiRoutes.Prefix + "/mps/{slug}/follow";

    public static string BillOf(string number) => $"{ApiRoutes.Prefix}/bills/{Uri.EscapeDataString(number)}/follow";

    public static string MpOf(string slug) => $"{ApiRoutes.Prefix}/mps/{Uri.EscapeDataString(slug)}/follow";
}

/// <summary>Design 8.11.2: the caller's follows and follow emails. Needs a signed-in member.</summary>
public interface IFollowsApi
{
    Task<ApiResult<MyFollowsResponse>> GetMineAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> FollowBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult> UnfollowBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult> FollowMpAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult> UnfollowMpAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult<NoticeSettingsResponse>> GetNoticesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> SetNoticesAsync(NoticeSettingsRequest request, CancellationToken cancellationToken = default);
}
