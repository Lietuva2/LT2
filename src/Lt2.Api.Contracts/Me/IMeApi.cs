namespace Lt2.Api.Contracts.Me;

/// <summary>The signed-in person's own account.</summary>
public interface IMeApi
{
    Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Completes a pending registration (design 6.2).</summary>
    Task<ApiResult> CompleteRegistrationAsync(CompleteRegistrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Uses a staff role activation code in the current passkey session (design 6.2).</summary>
    Task<ApiResult> ActivateRoleAsync(RoleActivationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gives the explicit consent to store positions (design 6.5).</summary>
    Task<ApiResult> GivePositionConsentAsync(PositionConsentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the consent; every position of the person is deleted (design 6.5).</summary>
    Task<ApiResult> WithdrawPositionConsentAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<PartySupportResponse>> GetPartySupportAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> SetPartySupportAsync(PartySupportRequest request, CancellationToken cancellationToken = default);
}
