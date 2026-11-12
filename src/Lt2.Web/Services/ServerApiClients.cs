using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>
/// The server-side implementation of the typed clients (design 3.1): when a component is rendered on the server it calls
/// the same module operations the HTTP endpoints call, in-process.
/// </summary>
internal sealed class ServerAuthApi(IIdentityModule identity, IHttpContextAccessor accessor, ErrorMapper errors) : IAuthApi
{
    public async Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.RegisterAsync(Mapping.ToRegistration(request), cancellationToken));

    public async Task<ApiResult> RequestCodeAsync(RequestCodeRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.RequestSignInCodeAsync(request.Email, cancellationToken));

    public async Task<ApiResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.SignInWithCodeAsync(request.Email, request.Code, cancellationToken));

    public async Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default)
    {
        await identity.SignOutAsync();
        return ApiResult.Ok();
    }

    public async Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.GetPasskeyCreationOptionsAsync(User, cancellationToken), x => x);

    public async Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.AddPasskeyAsync(User, request.Credential, request.Name, cancellationToken));

    public async Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.GetPasskeyRequestOptionsAsync(cancellationToken), x => x);

    public async Task<ApiResult> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.SignInWithPasskeyAsync(request.Credential, cancellationToken));

    private System.Security.Claims.ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();
}

internal sealed class ServerMeApi(IIdentityModule identity, IHttpContextAccessor accessor, ErrorMapper errors) : IMeApi
{
    public async Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.GetAccountAsync(User, cancellationToken), Mapping.ToResponse);

    public async Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await identity.GetSessionsAsync(User, cancellationToken);
        return ApiResult<IReadOnlyList<SessionResponse>>.Ok(sessions.Select(Mapping.ToResponse).ToList());
    }

    public async Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.EndSessionAsync(User, sessionId, cancellationToken));

    private System.Security.Claims.ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();
}

/// <summary>Hand-written mapping between module types and API contracts.</summary>
internal static class Mapping
{
    public static Registration ToRegistration(RegisterRequest request) =>
        new(request.Email, request.InvitationCode, request.ConfirmsAdult, request.DisplayName);

    public static MeResponse ToResponse(AccountSummary account) => new()
    {
        Id = account.Id,
        Email = account.Email,
        DisplayName = account.DisplayName,
        VerificationLevel = account.VerificationLevel,
        Roles = account.Roles,
        PasskeyCount = account.PasskeyCount,
        CreatedAt = account.CreatedAt,
    };

    public static SessionResponse ToResponse(SessionSummary session) => new()
    {
        Id = session.Id,
        CreatedAt = session.CreatedAt,
        LastSeenAt = session.LastSeenAt,
        Device = session.Device,
        Method = session.Method,
        IsCurrent = session.IsCurrent,
    };

    public static ApiResult ToApiResult(this ErrorMapper errors, Result<Unit> result) =>
        result.IsSuccess ? ApiResult.Ok() : ApiResult.Fail(errors.ToProblem(result.Error!));

    public static ApiResult<TOut> ToApiResult<TIn, TOut>(this ErrorMapper errors, Result<TIn> result, Func<TIn, TOut> map) =>
        result.IsSuccess ? ApiResult<TOut>.Ok(map(result.Value)) : ApiResult<TOut>.Fail(errors.ToProblem(result.Error!));
}
