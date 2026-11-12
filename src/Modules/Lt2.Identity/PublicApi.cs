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
    bool RegistrationComplete,
    IReadOnlyList<string> ActiveStaffRoles,
    bool AwaitingActivation,
    bool HasPositionConsent = false);

/// <summary>
/// What the current session may do as staff (design 6.2): the staff roles that work in it (only when signed in with the
/// account's staff passkey, after activation) and whether a passkey confirmation is fresh (the last ten minutes).
/// </summary>
public sealed record StaffSession(IReadOnlyList<string> ActiveRoles, bool RecentlyConfirmed)
{
    public static readonly StaffSession None = new([], false);

    public bool Has(params string[] roles) => ActiveRoles.Any(roles.Contains);
}

/// <summary>Who performs an administrative action, for the audit log.</summary>
/// <param name="UserId"><c>null</c> for the server itself (the migrator).</param>
/// <param name="Role">The role acted in, for example <c>administrator</c> or <c>system</c>.</param>
/// <param name="IpHash">Keyed hash of the client address, never the address.</param>
public sealed record Actor(Guid? UserId, string Role, string? IpHash = null)
{
    public static readonly Actor Server = new(null, "system");
}

/// <summary>A role grant: the one-time activation code, shown once and never stored in plain text.</summary>
public sealed record RoleGrant(string ActivationCode, DateTimeOffset ExpiresAt);

/// <summary>
/// Staff role administration (design 6.2): grants with a one-time activation code, and resets. Used by the
/// administrator API and by the migrator for the first administrator.
/// </summary>
public interface IIdentityAdministration
{
    /// <summary>
    /// Gives the account with this address a staff role, inactive until the returned code is used in a passkey session.
    /// Fails with <c>account.not-found</c> for an unknown or pending account.
    /// </summary>
    Task<Result<RoleGrant>> GrantRoleAsync(string email, string role, Actor actor, CancellationToken cancellationToken);

    /// <summary>Removes the account's passkeys; its staff roles stay inactive until a new activation code is used.</summary>
    Task<Result<Unit>> ResetAccountAsync(string email, Actor actor, CancellationToken cancellationToken);
}

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

    /// <summary>
    /// Adds a passkey. On an account with an active staff role this needs a staff-passkey session with a fresh
    /// confirmation. The person is told by email.
    /// </summary>
    Task<Result<Unit>> AddPasskeyAsync(ClaimsPrincipal user, string credentialJson, string? name, string? ipHash, CancellationToken cancellationToken);

    /// <summary>A fresh passkey confirmation for the current staff-passkey session (the ten-minute rule).</summary>
    Task<Result<Unit>> ConfirmPasskeyAsync(ClaimsPrincipal user, string credentialJson, CancellationToken cancellationToken);

    /// <summary>
    /// Uses a role activation code in the current passkey session: that passkey becomes the staff passkey and every
    /// other passkey on the account is removed.
    /// </summary>
    Task<Result<Unit>> ActivateRoleAsync(ClaimsPrincipal user, string code, string? ipHash, CancellationToken cancellationToken);

    /// <summary>What the current session may do as staff; checked on every staff request.</summary>
    Task<StaffSession> GetStaffSessionAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<Result<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken);

    Task<Result<Unit>> SignInWithPasskeyAsync(string credentialJson, CancellationToken cancellationToken);

    Task<Result<AccountSummary>> GetAccountAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<Result<Unit>> EndSessionAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Records the explicit consent to store positions against the text version in force (design 6.5).</summary>
    Task<Result<Unit>> GivePositionConsentAsync(ClaimsPrincipal user, string textVersion, CancellationToken cancellationToken);

    /// <summary>Withdraws the consent and, in the same transaction, deletes every position of the person (design 6.5).</summary>
    Task<Result<Unit>> WithdrawPositionConsentAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>The party the person said they support, if any (design 5.2; private, optional).</summary>
    Task<Result<string?>> GetPartySupportAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the party (one of <paramref name="allowed"/>) with the explicit consent it needs, or with <c>null</c> removes it and
    /// its consent. Stored without history.
    /// </summary>
    Task<Result<Unit>> SetPartySupportAsync(ClaimsPrincipal user, string? party, IReadOnlyCollection<string> allowed, CancellationToken cancellationToken);

    /// <summary>Everything the person gave (design 6.5), their own data only.</summary>
    Task<Result<object>> ExportAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>Deletes the account and everything the person gave (design 6.5), and signs out.</summary>
    Task<Result<Unit>> DeleteAccountAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
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
