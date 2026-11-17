using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>
/// The server-side implementation of the typed clients (design 3.1): when a component is rendered on the server it calls
/// the same module operations the HTTP endpoints call, in-process.
/// </summary>
internal sealed class ServerAuthApi(IIdentityModule identity, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors, AnalyticsCopies analytics) : IAuthApi
{
    public async Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.RegisterAsync(Mapping.ToRegistration(request), cancellationToken));

    public async Task<ApiResult> RequestCodeAsync(RequestCodeRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.RequestSignInCodeAsync(request.Email, cancellationToken));

    public async Task<ApiResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.SignInWithCodeAsync(request.Email, request.Code, cancellationToken));

    public async Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default)
    {
        // The member's kept analytics comparison ends with the session (design 8.26.6).
        if (accessor.HttpContext?.User.UserId() is { } id)
        {
            analytics.Forget(id);
        }

        await identity.SignOutAsync();
        return ApiResult.Ok();
    }

    public async Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.GetPasskeyCreationOptionsAsync(User, cancellationToken), x => x);

    public async Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.AddPasskeyAsync(
            User, request.Credential, request.Name, accessor.HttpContext is { } context ? ipHasher.Hash(context) : null, cancellationToken));

    public async Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.GetPasskeyRequestOptionsAsync(cancellationToken), x => x);

    public async Task<ApiResult> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.SignInWithPasskeyAsync(request.Credential, cancellationToken));

    private System.Security.Claims.ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();
}

internal sealed class ServerMeApi(IIdentityModule identity, IMpDirectory mps, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors, IInvitations invitations, IConfiguration configuration, AnalyticsCopies analytics, ISettings settings) : IMeApi
{
    /// <summary>
    /// The account as a page rendered on the server sees it: without the MP offer or link (design 8.5.4: nothing about it is
    /// in the page the server renders). The island that shows them asks <c>GET /me</c> from the browser.
    /// </summary>
    public async Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.GetAccountAsync(User, cancellationToken), account => Mapping.ToResponse(account) with { MpOffer = null, Mp = null });

    public async Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await identity.GetSessionsAsync(User, cancellationToken);
        return ApiResult<IReadOnlyList<SessionResponse>>.Ok(sessions.Select(Mapping.ToResponse).ToList());
    }

    public async Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.EndSessionAsync(User, sessionId, cancellationToken));

    public async Task<ApiResult<IReadOnlyList<KnownBrowserResponse>>> GetKnownBrowsersAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.GetKnownBrowsersAsync(User, cancellationToken), Mapping.ToKnownBrowsers);

    public async Task<ApiResult> ForgetKnownBrowserAsync(Guid id, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.ForgetKnownBrowserAsync(User, id, cancellationToken));

    public async Task<ApiResult> ChangeDisplayNameAsync(DisplayNameRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.ChangeDisplayNameAsync(User, request.DisplayName, IpHash, cancellationToken));

    public async Task<ApiResult> CompleteRegistrationAsync(CompleteRegistrationRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.CompleteRegistrationAsync(User, Mapping.ToCompletion(request), cancellationToken));

    public async Task<ApiResult> ActivateRoleAsync(RoleActivationRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.ActivateRoleAsync(User, request.Code, IpHash, cancellationToken));

    public async Task<ApiResult> ConfirmPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.ConfirmPasskeyAsync(User, request.Credential, cancellationToken));

    public async Task<ApiResult> StartStaffSessionAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.StartStaffSessionAsync(User, request.Credential, IpHash, cancellationToken));

    public async Task<ApiResult> GivePositionConsentAsync(PositionConsentRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.GivePositionConsentAsync(User, request.TextVersion, cancellationToken));

    public async Task<ApiResult> AcceptTermsAsync(TermsAcceptanceRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.AcceptTermsAsync(User, request.TextVersion, cancellationToken));

    public async Task<ApiResult> WithdrawPositionConsentAsync(CancellationToken cancellationToken = default)
    {
        ForgetAnalytics();
        return errors.ToApiResult(await identity.WithdrawPositionConsentAsync(User, cancellationToken));
    }

    public async Task<ApiResult<PartySupportResponse>> GetPartySupportAsync(CancellationToken cancellationToken = default)
    {
        var choices = await mps.PartiesAsync(cancellationToken);
        var minimum = await settings.GetAsync(SettingKeys.DigestPartyGroupMinimum, cancellationToken);
        return errors.ToApiResult(await identity.GetPartySupportAsync(User, cancellationToken), state => Mapping.ToResponse(state, choices, minimum));
    }

    public async Task<ApiResult> SetPartyPublicAsync(bool show, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(show
            ? await identity.ShowPartyAsync(User, PartyPublicLimits.ConsentVersion, IpHash, cancellationToken)
            : await identity.HidePartyAsync(User, IpHash, cancellationToken));

    public async Task<ApiResult> DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        ForgetAnalytics();
        return errors.ToApiResult(await identity.DeleteAccountAsync(User, cancellationToken));
    }

    /// <summary>The member's kept analytics comparison goes with their sides and their account (design 8.26.6).</summary>
    private void ForgetAnalytics()
    {
        if (User.UserId() is { } id)
        {
            analytics.Forget(id);
        }
    }

    public Task<ApiResult<MyInvitationResponse>> GetInvitationAsync(CancellationToken cancellationToken = default) =>
        InvitationAsync(id => invitations.MineAsync(id, cancellationToken));

    public Task<ApiResult<MyInvitationResponse>> ReplaceInvitationAsync(CancellationToken cancellationToken = default) =>
        InvitationAsync(id => invitations.ReplaceAsync(id, cancellationToken));

    private async Task<ApiResult<MyInvitationResponse>> InvitationAsync(Func<Guid, Task<Result<MyInvitation>>> action)
    {
        if (User.UserId() is not { } id)
        {
            return ApiResult<MyInvitationResponse>.Fail(errors.ToProblem(Error.Unauthorized("auth.required")));
        }

        return errors.ToApiResult(await action(id), mine => InvitationMapping.Map(mine, configuration));
    }

    public async Task<ApiResult> SetPartySupportAsync(PartySupportRequest request, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await identity.SetPartySupportAsync(User, request.Party, request.Consent, await mps.PartiesAsync(cancellationToken), cancellationToken));

    private string? IpHash => accessor.HttpContext is { } context ? ipHasher.Hash(context) : null;

    private System.Security.Claims.ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();
}

/// <summary>Hand-written mapping between module types and API contracts.</summary>
internal static class Mapping
{
    public static Registration ToRegistration(RegisterRequest request) =>
        new(request.Email, request.InvitationCode, request.OfficeInvitation);

    public static RegistrationCompletion ToCompletion(CompleteRegistrationRequest request) =>
        new(request.DisplayName, request.ConfirmsAdult, request.InvitationCode, request.AcceptsTerms, request.OfficeInvitation);

    public static PartySupportResponse ToResponse(PartySupportState state, IReadOnlyList<string> choices, int groupMinimum) =>
        new(state.Party, choices, groupMinimum, state.Public, state.ProfilePublic, state.ConsentVersion);

    public static MeResponse ToResponse(AccountSummary account) => new()
    {
        Id = account.Id,
        Email = account.Email,
        DisplayName = account.DisplayName,
        VerificationLevel = account.VerificationLevel,
        Roles = account.Roles,
        PasskeyCount = account.PasskeyCount,
        CreatedAt = account.CreatedAt,
        RegistrationComplete = account.RegistrationComplete,
        ActiveStaffRoles = account.ActiveStaffRoles,
        AwaitingActivation = account.AwaitingActivation,
        HasPositionConsent = account.HasPositionConsent,
        StaffAccount = account.StaffAccount,
        MpOffer = account.MpOffer is { } offer ? MpMapping.Map(offer) : null,
        Mp = account.Mp is { } link ? MpMapping.Map(link) : null,
        TermsRequired = account.TermsRequired,
        PasskeySession = account.PasskeySession,
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

    public static IReadOnlyList<KnownBrowserResponse> ToKnownBrowsers(IReadOnlyList<KnownBrowserSummary> browsers) =>
        browsers.Select(b => new KnownBrowserResponse(b.Id, b.Label, b.FirstSeenAt, b.LastSeenAt, b.IsCurrent)).ToList();

    public static ApiResult ToApiResult(this ErrorMapper errors, Result<Unit> result) =>
        result.IsSuccess ? ApiResult.Ok() : ApiResult.Fail(errors.ToProblem(result.Error!));

    public static ApiResult<TOut> ToApiResult<TIn, TOut>(this ErrorMapper errors, Result<TIn> result, Func<TIn, TOut> map) =>
        result.IsSuccess ? ApiResult<TOut>.Ok(map(result.Value)) : ApiResult<TOut>.Fail(errors.ToProblem(result.Error!));
}
