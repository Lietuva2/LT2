namespace Lt2.Api.Contracts.Parliament;

/// <summary>
/// Design 7.2: the moderators' correction of a vote's classification (<c>/api/v1/moderation/votes/{id}/classification</c>), for
/// a moderator or an administrator in a staff-passkey session; anyone else is refused (403).
/// </summary>
public interface IVoteModerationApi
{
    Task<ApiResult<VoteClassificationResponse>> GetClassificationAsync(long voteId, CancellationToken cancellationToken = default);

    /// <summary>PUT: corrects the kind (and a stage result's stage).</summary>
    Task<ApiResult> CorrectClassificationAsync(long voteId, VoteClassificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>DELETE: the rules' classification is in force again.</summary>
    Task<ApiResult> ResetClassificationAsync(long voteId, VoteClassificationResetRequest request, CancellationToken cancellationToken = default);
}
