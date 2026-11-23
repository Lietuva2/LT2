using System.Globalization;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Client.Api;

/// <summary>
/// The browser's <see cref="IParliamentApi"/> (design 7.4): the public reads, plain GET calls to <c>/api/v1</c> (the browser
/// revalidates them by their ETags). An island that reads parliament data after it has started (the bill page's seat map) takes it.
/// </summary>
internal sealed class HttpParliamentApi(ApiHttp http) : IParliamentApi
{
    public Task<ApiResult<TermResponse>> GetCurrentTermAsync(CancellationToken cancellationToken = default) =>
        Get<TermResponse>(ParliamentRoutes.CurrentTerm, cancellationToken);

    public Task<ApiResult<MpListResponse>> GetMpsAsync(string? faction = null, string? committee = null, string? q = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        Get<MpListResponse>(ParliamentRoutes.Mps + Query(("faction", faction), ("committee", committee), ("q", q), ("cursor", cursor)), cancellationToken);

    public Task<ApiResult<MpProfileResponse>> GetMpAsync(string slug, CancellationToken cancellationToken = default) =>
        Get<MpProfileResponse>(ParliamentRoutes.MpBySlug(slug), cancellationToken);

    public Task<ApiResult<MpVotesResponse>> GetMpVotesAsync(string slug, string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) =>
        Get<MpVotesResponse>(ParliamentRoutes.MpVotesBySlug(slug) + Query(("cursor", cursor), ("limit", Number(limit))), cancellationToken);

    public Task<ApiResult<IReadOnlyList<BodyListItem>>> GetFactionsAsync(CancellationToken cancellationToken = default) =>
        Get<IReadOnlyList<BodyListItem>>(ParliamentRoutes.Factions, cancellationToken);

    public Task<ApiResult<BodyResponse>> GetFactionAsync(string slug, CancellationToken cancellationToken = default) =>
        Get<BodyResponse>(ParliamentRoutes.FactionBySlug(slug), cancellationToken);

    public Task<ApiResult<IReadOnlyList<BodyListItem>>> GetCommitteesAsync(CancellationToken cancellationToken = default) =>
        Get<IReadOnlyList<BodyListItem>>(ParliamentRoutes.Committees, cancellationToken);

    public Task<ApiResult<BodyResponse>> GetCommitteeAsync(string slug, CancellationToken cancellationToken = default) =>
        Get<BodyResponse>(ParliamentRoutes.CommitteeBySlug(slug), cancellationToken);

    public Task<ApiResult<BillListResponse>> GetBillsAsync(string? q = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        Get<BillListResponse>(ParliamentRoutes.Bills + Query(("q", q), ("cursor", cursor)), cancellationToken);

    public Task<ApiResult<BillResponse>> GetBillAsync(string number, CancellationToken cancellationToken = default) =>
        Get<BillResponse>(ParliamentRoutes.BillByNumber(number), cancellationToken);

    public Task<ApiResult<VoteListResponse>> GetVotesAsync(
        string? q = null, string? kind = null, DateOnly? until = null, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default) =>
        Get<VoteListResponse>(
            ParliamentRoutes.Votes + Query(("q", q), ("kind", kind), ("until", until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("limit", Number(limit)), ("cursor", cursor)),
            cancellationToken);

    public Task<ApiResult<VoteResponse>> GetVoteAsync(long id, CancellationToken cancellationToken = default) =>
        Get<VoteResponse>(ParliamentRoutes.VoteById(id), cancellationToken);

    private Task<ApiResult<T>> Get<T>(string uri, CancellationToken cancellationToken) =>
        http.SendAsync<T>(HttpMethod.Get, uri, null, cancellationToken);

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    /// <summary>The query string of the values that are given ("" when none is).</summary>
    private static string Query(params (string Name, string? Value)[] values)
    {
        var given = values.Where(v => !string.IsNullOrEmpty(v.Value)).Select(v => $"{v.Name}={Uri.EscapeDataString(v.Value!)}").ToArray();
        return given.Length == 0 ? "" : "?" + string.Join('&', given);
    }
}
