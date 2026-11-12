namespace Lt2.Api.Contracts.Auth;

/// <summary>Sign-in, registration and passkeys. Implemented over HTTP in the browser and in-process on the server.</summary>
public interface IAuthApi
{
    Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> RequestCodeAsync(RequestCodeRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>WebAuthn creation options as JSON, for <c>PublicKeyCredential.parseCreationOptionsFromJSON</c>.</summary>
    Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default);

    /// <summary>WebAuthn request options as JSON, for <c>PublicKeyCredential.parseRequestOptionsFromJSON</c>.</summary>
    Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default);
}
