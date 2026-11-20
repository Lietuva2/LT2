using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Client.Api;

/// <summary>The browser's <see cref="IVoteModerationApi"/> (design 7.2): the vote page's "Moderavimas" part.</summary>
internal sealed class HttpVoteModerationApi(ApiHttp http) : IVoteModerationApi
{
    public Task<ApiResult<VoteClassificationResponse>> GetClassificationAsync(long voteId, CancellationToken cancellationToken = default) =>
        http.SendAsync<VoteClassificationResponse>(HttpMethod.Get, ParliamentRoutes.VoteClassificationOf(voteId), null, cancellationToken);

    public Task<ApiResult> CorrectClassificationAsync(long voteId, VoteClassificationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ParliamentRoutes.VoteClassificationOf(voteId), request, cancellationToken);

    public Task<ApiResult> ResetClassificationAsync(long voteId, VoteClassificationResetRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, ParliamentRoutes.VoteClassificationOf(voteId), request, cancellationToken);
}
