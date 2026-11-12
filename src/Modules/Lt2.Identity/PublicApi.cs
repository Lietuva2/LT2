using System.Security.Claims;
using Lt2.SharedKernel;

namespace Lt2.Identity;

/// <summary>Registration request as the module sees it; validated by the shared contracts first.</summary>
public sealed record Registration(string Email, string? InvitationCode, bool ConfirmsAdult);

/// <summary>The signed-in person's own account.</summary>
public sealed record AccountSummary(
    Guid Id,
    string Email,
    string? DisplayName,
    string VerificationLevel,
    IReadOnlyList<string> Roles,
    int PasskeyCount,
    DateTimeOffset CreatedAt);

/// <summary>One signed-in browser.</summary>
public sealed record SessionSummary(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    string? Device,
    string Method,
    bool IsCurrent);

/// <summary>
/// The Identity module's public interface. Operations that sign in or out act on the current HTTP context.
/// No operation reveals whether an email address is registered (design 6.2).
/// </summary>
public interface IIdentityModule
{
    /// <summary>Creates the account (or, for an existing one, does nothing visible) and emails a sign-in code.</summary>
    Task<Result<Unit>> RegisterAsync(Registration registration, CancellationToken cancellationToken);

    /// <summary>Emails a sign-in code if the address belongs to an account; succeeds either way.</summary>
    Task<Result<Unit>> RequestSignInCodeAsync(string email, CancellationToken cancellationToken);

    Task<Result<Unit>> SignInWithCodeAsync(string email, string code, CancellationToken cancellationToken);

    Task SignOutAsync();

    Task<Result<string>> GetPasskeyCreationOptionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<Result<Unit>> AddPasskeyAsync(ClaimsPrincipal user, string credentialJson, string? name, CancellationToken cancellationToken);

    Task<Result<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken);

    Task<Result<Unit>> SignInWithPasskeyAsync(string credentialJson, CancellationToken cancellationToken);

    Task<Result<AccountSummary>> GetAccountAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<Result<Unit>> EndSessionAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken);
}

/// <summary>Housekeeping run by the worker.</summary>
public interface IIdentityMaintenance
{
    /// <summary>Deletes sign-in codes older than a day and expired sessions; returns how many rows went.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}
