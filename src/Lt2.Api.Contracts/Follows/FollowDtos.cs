using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Organisations;

namespace Lt2.Api.Contracts.Follows;

// Design 8.11.2: following bills and MPs; design 8.14.14 (stage B): following organisations; design 8.25.5: following members.
// Only the caller's own follows leave the server, never combined with sides or invitations; how many follow a thing is a
// public number (design 8.11.2, owner 2026-11-16), never who.

/// <summary>The latest stage result of a followed bill.</summary>
public sealed record FollowedVoteResponse(long VoteId, string? Stage, DateTimeOffset HeldAt, bool IsConsensus, int? For, int? Against, int? Abstain);

/// <summary>
/// A bill the caller follows, with its planned date or its last stage result (design 8.11.2, the account page), and what was
/// written there since they last opened its page (design 8.11.8), null when nothing.
/// </summary>
public sealed record FollowedBillResponse(string Number, string Title, PlannedDateResponse? Planned, FollowedVoteResponse? LastStageResult, Me.BillNewsCountsResponse? New = null, string? PlainTitle = null);

/// <summary>An MP the caller follows.</summary>
public sealed record FollowedMpResponse(string Slug, string FirstName, string LastName);

/// <summary>An organisation the caller follows, with its label and status (<c>active</c>, <c>blocked</c>, <c>closed</c>).</summary>
public sealed record FollowedOrganisationResponse(string Slug, string Name, OrganisationLabelResponse Label, string Status);

/// <summary>
/// The caller's own follows (design 8.8 <c>GET /me/follows</c>); never cached. There is no limit on how many (design 8.11.2).
/// </summary>
public sealed record MyFollowsResponse(
    IReadOnlyList<FollowedBillResponse> Bills,
    IReadOnlyList<FollowedMpResponse> Mps,
    IReadOnlyList<FollowedOrganisationResponse> Organisations,
    IReadOnlyList<FollowedMemberResponse>? Members = null);

/// <summary>A member with a public profile the caller follows (design 8.25.5).</summary>
public sealed record FollowedMemberResponse(string ProfileId, string DisplayName);

/// <summary>
/// Which follow emails are on: a followed bill is planned; the Seimas voted on it (design 8.11.2); a followed organisation
/// made a statement public (design 8.14.14); MPs answered on a followed bill (design 8.20.1); a followed member published an
/// argument (design 8.25.5); someone commented under the person's own argument (design 8.23.9); and how often they come
/// (<see cref="NoticeFrequencies"/>, design 8.24; changed at <see cref="NoticeFrequencyRoutes.Mine"/>).
/// </summary>
public sealed record NoticeSettingsResponse(
    bool Planned,
    bool Voted,
    bool Statements = true,
    bool Responses = true,
    bool Members = true,
    bool Comments = true,
    string Frequency = NoticeFrequencies.Immediate);

public sealed record NoticeSettingsRequest
{
    public bool Planned { get; init; } = true;

    public bool Voted { get; init; } = true;

    public bool Statements { get; init; } = true;

    public bool Responses { get; init; } = true;

    /// <summary>A followed member published an argument (design 8.25.5).</summary>
    public bool Members { get; init; } = true;

    /// <summary>"Kai mano argumentą pakomentuoja" (design 8.23.9).</summary>
    public bool Comments { get; init; } = true;
}

public static class FollowRoutes
{
    public const string Mine = ApiRoutes.Prefix + "/me/follows";
    public const string Notices = ApiRoutes.Prefix + "/me/notices";
    public const string Bill = ApiRoutes.Prefix + "/bills/{number}/follow";
    public const string Mp = ApiRoutes.Prefix + "/mps/{slug}/follow";
    public const string Organisation = ApiRoutes.Prefix + "/organisations/{slug}/follow";

    /// <summary>How many follow a bill (design 8.11.2): a public number, apart from the bill's record and its sides.</summary>
    public const string BillFollowers = ApiRoutes.Prefix + "/bills/{number}/followers";

    public static string BillFollowersOf(string number) => $"{ApiRoutes.Prefix}/bills/{Uri.EscapeDataString(number)}/followers";

    public static string BillOf(string number) => $"{ApiRoutes.Prefix}/bills/{Uri.EscapeDataString(number)}/follow";

    public static string MpOf(string slug) => $"{ApiRoutes.Prefix}/mps/{Uri.EscapeDataString(slug)}/follow";

    public static string OrganisationOf(string slug) => $"{ApiRoutes.Prefix}/organisations/{Uri.EscapeDataString(slug)}/follow";
}

/// <summary>Design 8.11.2: the caller's follows and follow emails. Needs a signed-in member.</summary>
public interface IFollowsApi
{
    Task<ApiResult<MyFollowsResponse>> GetMineAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> FollowBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult> UnfollowBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult> FollowMpAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult> UnfollowMpAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult> FollowOrganisationAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult> UnfollowOrganisationAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Design 8.25.5: <c>member.not-found</c> for a profile that is not public.</summary>
    Task<ApiResult> FollowMemberAsync(string profileId, CancellationToken cancellationToken = default);

    Task<ApiResult> UnfollowMemberAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>How many follow a bill (public; design 8.11.2).</summary>
    Task<ApiResult<Me.FollowersResponse>> GetBillFollowersAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<NoticeSettingsResponse>> GetNoticesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> SetNoticesAsync(NoticeSettingsRequest request, CancellationToken cancellationToken = default);
}
