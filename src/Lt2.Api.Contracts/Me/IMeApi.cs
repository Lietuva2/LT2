namespace Lt2.Api.Contracts.Me;

/// <summary>The signed-in person's own account.</summary>
public interface IMeApi
{
    Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
