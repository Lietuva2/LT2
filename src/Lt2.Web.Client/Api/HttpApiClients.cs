using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;

namespace Lt2.Web.Client.Api;

internal sealed class HttpAuthApi(ApiHttp http) : IAuthApi
{
    public Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.Register, request, cancellationToken);

    public Task<ApiResult> RequestCodeAsync(RequestCodeRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.Code, request, cancellationToken);

    public Task<ApiResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.SignIn, request, cancellationToken);

    public Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.SignOut, null, cancellationToken);

    public Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default) =>
        http.SendForJsonTextAsync(HttpMethod.Post, ApiRoutes.Auth.PasskeyCreationOptions, cancellationToken);

    public Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.Passkeys, request, cancellationToken);

    public Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) =>
        http.SendForJsonTextAsync(HttpMethod.Post, ApiRoutes.Auth.PasskeyRequestOptions, cancellationToken);

    public Task<ApiResult> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Post, ApiRoutes.Auth.PasskeySignIn, request, cancellationToken);
}

internal sealed class HttpMeApi(ApiHttp http) : IMeApi
{
    public Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<MeResponse>(HttpMethod.Get, ApiRoutes.Me.Root, null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        http.SendAsync<IReadOnlyList<SessionResponse>>(HttpMethod.Get, ApiRoutes.Me.Sessions, null, cancellationToken);

    public Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Delete, ApiRoutes.Me.SessionById(sessionId), null, cancellationToken);

    public Task<ApiResult> CompleteRegistrationAsync(CompleteRegistrationRequest request, CancellationToken cancellationToken = default) =>
        http.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, request, cancellationToken);
}
