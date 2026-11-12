using System.Security.Claims;
using Lt2.SharedKernel;

namespace Lt2.Identity;

/// <summary>Registration request as the module sees it; validated by the shared contracts first.</summary>
public sealed record Registration(string Email, string? InvitationCode);

/// <summary>What a person gives once their address is proven (design 6.2).</summary>
public sealed record RegistrationCompletion(string? DisplayName, bool ConfirmsAdult, string? InvitationCode);

/// <summary>The signed-in person's own account.</summary>
public sealed record AccountSummary(
    Guid Id,
    string Email,
    string? DisplayName,
    string VerificationLevel,
    IReadOnlyList<string> Roles,
    int PasskeyCount,
    DateTimeOffset CreatedAt,
    bool RegistrationComplete);

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
    /// <summary>
    /// Creates a pending account (or, for an address that already has one, does nothing visible) and emails a sign-in
    /// code.
    /// </summary>
    Task<Result<Unit>> RegisterAsync(Registration registration, CancellationToken cancellationToken);

    /// <summary>Completes the signed-in person's pending registration and makes the account a member.</summary>
    Task<Result<Unit>> CompleteRegistrationAsync(ClaimsPrincipal user, RegistrationCompletion completion, CancellationToken cancellationToken);

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
    /// <summary>
    /// Deletes sign-in codes older than a day, expired sessions, and accounts still pending after the configured number
    /// of days; returns how many rows went.
    /// </summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}
