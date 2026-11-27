namespace Lt2.Api.Contracts.Auth;

/// <summary>Sign-in, registration and passkeys. Implemented over HTTP in the browser and in-process on the server.</summary>
public interface IAuthApi
{
    /// <summary>Registers an address; the answer says whether the browser is signed in already (a trial site without email, design 6.2).</summary>
    Task<ApiResult<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> RequestCodeAsync(RequestCodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Signs in with the emailed code; the answer says whether the browser was signed in to another account (design 6.2).</summary>
    Task<ApiResult<SignInResponse>> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>WebAuthn creation options as JSON, for <c>PublicKeyCredential.parseCreationOptionsFromJSON</c>.</summary>
    Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default);

    /// <summary>WebAuthn request options as JSON, for <c>PublicKeyCredential.parseRequestOptionsFromJSON</c>.</summary>
    Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Signs in with a passkey; the answer says whether the browser was signed in to another account (design 6.2).</summary>
    Task<ApiResult<SignInResponse>> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default);
}
