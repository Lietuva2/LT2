namespace Lt2.Api.Contracts.Me;

/// <summary>The signed-in person's own account.</summary>
public interface IMeApi
{
    Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Completes a pending registration (design 6.2).</summary>
    Task<ApiResult> CompleteRegistrationAsync(CompleteRegistrationRequest request, CancellationToken cancellationToken = default);
}
