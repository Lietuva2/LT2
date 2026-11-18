namespace Lt2.Api.Contracts.Parliament;

/// <summary><c>limit</c> of <c>GET /mps/{slug}/votes</c>: 50 by default, at most as many as the history of votes takes (design 8.18).</summary>
public static class MpVoteLimits
{
    public const int Default = 50;

    public const int Max = VoteListQuery.MaxLimit;

    /// <summary>A whole number from 1 up, at most <see cref="Max"/>; <see cref="Default"/> for anything else.</summary>
    public static int Of(string? limit) =>
        int.TryParse(limit, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1 ? Math.Min(n, Max) : Default;
}

/// <summary>Design 7.4: the public parliament API (all GET, no account needed, cached with ETags).</summary>
public interface IParliamentApi
{
    Task<ApiResult<TermResponse>> GetCurrentTermAsync(CancellationToken cancellationToken = default);

    /// <summary>MPs of the current term, alphabetical by last name. Filters are slugs; <paramref name="q"/> searches names.</summary>
    Task<ApiResult<MpListResponse>> GetMpsAsync(string? faction = null, string? committee = null, string? q = null, string? cursor = null, CancellationToken cancellationToken = default);

    Task<ApiResult<MpProfileResponse>> GetMpAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>The MP's votes, newest first: <paramref name="limit"/> of them (<see cref="MpVoteLimits"/>) from <paramref name="cursor"/> on.</summary>
    Task<ApiResult<MpVotesResponse>> GetMpVotesAsync(string slug, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<BodyListItem>>> GetFactionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<BodyResponse>> GetFactionAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<BodyListItem>>> GetCommitteesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<BodyResponse>> GetCommitteeAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult<BillListResponse>> GetBillsAsync(string? q = null, string? cursor = null, CancellationToken cancellationToken = default);

    Task<ApiResult<BillResponse>> GetBillAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Design 8.18: the history of votes, newest first: <paramref name="kind"/> one of <see cref="VoteListQuery"/>'s kinds,
    /// <paramref name="q"/> a bill's number or title, <paramref name="until"/> the last Vilnius day included.
    /// </summary>
    Task<ApiResult<VoteListResponse>> GetVotesAsync(
        string? q = null, string? kind = null, DateOnly? until = null, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default);

    Task<ApiResult<VoteResponse>> GetVoteAsync(long id, CancellationToken cancellationToken = default);
}
