namespace Lt2.Api.Contracts.Me;

/// <summary>The signed-in person's own account.</summary>
public interface IMeApi
{
    Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>The browsers known to the account (design 8.20.2), last seen first.</summary>
    Task<ApiResult<IReadOnlyList<KnownBrowserResponse>>> GetKnownBrowsersAsync(CancellationToken cancellationToken = default);

    /// <summary>"Pamiršti": the browser becomes a stranger to the account again (design 8.20.2).</summary>
    Task<ApiResult> ForgetKnownBrowserAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the display name (design 6.2), as often as the member likes; <c>writing.restricted</c> while a restriction holds.
    /// </summary>
    Task<ApiResult> ChangeDisplayNameAsync(DisplayNameRequest request, CancellationToken cancellationToken = default);

    /// <summary>Completes a pending registration (design 6.2).</summary>
    Task<ApiResult> CompleteRegistrationAsync(CompleteRegistrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Uses a staff role activation code in the current passkey session (design 6.2).</summary>
    Task<ApiResult> ActivateRoleAsync(RoleActivationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// A fresh confirmation with the staff passkey (design 6.2: the ten-minute rule for sensitive actions such as saving a
    /// setting); the assertion answers <see cref="Auth.IAuthApi.GetPasskeyRequestOptionsAsync"/>.
    /// </summary>
    Task<ApiResult> ConfirmPasskeyAsync(Auth.PasskeyCredentialRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Design 6.2: this code session on a staff account becomes a staff session with the account's staff passkey, under a new
    /// session key; the assertion answers <see cref="Auth.IAuthApi.GetPasskeyRequestOptionsAsync"/>. Any other passkey is
    /// refused with <c>passkey.staff-required</c>.
    /// </summary>
    Task<ApiResult> StartStaffSessionAsync(Auth.PasskeyCredentialRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gives the explicit consent to store positions (design 6.5).</summary>
    Task<ApiResult> GivePositionConsentAsync(PositionConsentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Accepts the terms of use in force (design 8.22.2): the one write an account that has not accepted them may make, besides signing out and deleting.</summary>
    Task<ApiResult> AcceptTermsAsync(TermsAcceptanceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the consent; every position of the person is deleted (design 6.5).</summary>
    Task<ApiResult> WithdrawPositionConsentAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<PartySupportResponse>> GetPartySupportAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> SetPartySupportAsync(PartySupportRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes the account and everything the person gave (design 6.5).</summary>
    Task<ApiResult> DeleteAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>The caller's own invitation code and count (design 6.2).</summary>
    Task<ApiResult<MyInvitationResponse>> GetInvitationAsync(CancellationToken cancellationToken = default);

    /// <summary>A new code; the old one stops working; the count carries over.</summary>
    Task<ApiResult<MyInvitationResponse>> ReplaceInvitationAsync(CancellationToken cancellationToken = default);
}
