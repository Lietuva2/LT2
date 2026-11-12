namespace Lt2.Api.Contracts.Parliament;

/// <summary>Design 7.4: the public parliament API (all GET, no account needed, cached with ETags).</summary>
public interface IParliamentApi
{
    Task<ApiResult<TermResponse>> GetCurrentTermAsync(CancellationToken cancellationToken = default);

    /// <summary>MPs of the current term, alphabetical by last name. Filters are slugs; <paramref name="q"/> searches names.</summary>
    Task<ApiResult<MpListResponse>> GetMpsAsync(string? faction = null, string? committee = null, string? q = null, string? cursor = null, CancellationToken cancellationToken = default);

    Task<ApiResult<MpProfileResponse>> GetMpAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult<MpVotesResponse>> GetMpVotesAsync(string slug, string? cursor = null, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<BodyListItem>>> GetFactionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<BodyResponse>> GetFactionAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<BodyListItem>>> GetCommitteesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<BodyResponse>> GetCommitteeAsync(string slug, CancellationToken cancellationToken = default);

    Task<ApiResult<BillListResponse>> GetBillsAsync(string? q = null, string? cursor = null, CancellationToken cancellationToken = default);

    Task<ApiResult<BillResponse>> GetBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<VoteResponse>> GetVoteAsync(long id, CancellationToken cancellationToken = default);
}
