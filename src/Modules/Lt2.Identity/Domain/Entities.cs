using Microsoft.AspNetCore.Identity;

namespace Lt2.Identity.Domain;

/// <summary>An account. No password is ever set: sign-in is by emailed code or passkey (design 6.2).</summary>
internal sealed class User : IdentityUser<Guid>;

internal sealed class Role : IdentityRole<Guid>;

/// <summary>How strongly the account's link to a real person was checked (design 6.2).</summary>
internal static class VerificationLevels
{
    /// <summary>Registered, but no code has been confirmed yet.</summary>
    public const string None = "none";

    /// <summary>The person proved control of the email address.</summary>
    public const string Email = "email";
}

/// <summary>
/// <c>identity.profile</c>: the application's own facts about an account. While the account is pending (design 6.2) it
/// holds only the normalised address and the invitation code that was entered.
/// </summary>
internal sealed class Profile
{
    public Guid UserId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Key for the one-account-per-address rule; see <see cref="EmailAddress.Normalize"/>.</summary>
    public string NormalizedEmail { get; set; } = "";

    public string VerificationLevel { get; set; } = VerificationLevels.None;

    /// <summary>The invitation whose use was counted when the registration was completed.</summary>
    public Guid? InvitationId { get; set; }

    /// <summary>The invitation code entered at registration (normalised); counted only on completion.</summary>
    public string? InvitationCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the registration was completed; <c>null</c> while the account is pending.</summary>
    public DateTimeOffset? RegisteredAt { get; set; }

    public bool IsPending => RegisteredAt is null;

    /// <summary>
    /// The credential id of the account's staff passkey, set when a role activation code is used (design 6.2). A staff
    /// role works only in a session signed in with this passkey. Cleared by a reset.
    /// </summary>
    public byte[]? StaffPasskeyId { get; set; }
}

/// <summary>
/// <c>identity.role_activation</c>: a one-time code that activates a granted staff role (design 5.2, 6.2). Only the hash
/// is stored; the code is shown once to whoever granted the role.
/// </summary>
internal sealed class RoleActivation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Role { get; set; } = "";

    public byte[] CodeHash { get; set; } = [];

    public Guid? IssuedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary><c>identity.invitation</c>: registration during the closed trial is by invitation (design 5.2).</summary>
internal sealed class Invitation
{
    public Guid Id { get; set; }

    /// <summary>Stored in upper case; compared case-insensitively.</summary>
    public string Code { get; set; } = "";

    public Guid? IssuedByUserId { get; set; }

    public int UsesLeft { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}

/// <summary><c>identity.consent</c>: what a person agreed to, with the version of the text (design 5.2, 6.5).</summary>
internal sealed class Consent
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = "";

    public string TextVersion { get; set; } = "";

    public DateTimeOffset GivenAt { get; set; }

    public DateTimeOffset? WithdrawnAt { get; set; }
}

internal static class ConsentKinds
{
    /// <summary>The self-declared confirmation of being 18 or older (design 6.2).</summary>
    public const string Adult = "age-18-or-older";

    /// <summary>Version of the registration text the confirmation was given against.</summary>
    public const string AdultTextVersion = "2026-11-12";
}

/// <summary><c>identity.sign_in_code</c>: an emailed one-time code, stored only as a hash.</summary>
internal sealed class SignInCode
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte[] CodeHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int FailedAttempts { get; set; }

    /// <summary>Set when the code was used, or replaced by a newer one.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}

/// <summary><c>identity.session</c>: one signed-in browser. The cookie holds only the id (design 6.2: "my sessions").</summary>
internal sealed class Session
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The authentication ticket, protected with ASP.NET Core data protection.</summary>
    public byte[] Ticket { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary><c>otp</c> or <c>pop</c>, see <c>AuthenticationMethods</c>.</summary>
    public string Method { get; set; } = "";

    /// <summary>Coarse browser and system description, never the full user agent.</summary>
    public string? Device { get; set; }

    /// <summary>For a passkey session: the credential id of the passkey it was signed in with.</summary>
    public byte[]? PasskeyId { get; set; }

    /// <summary>When the session's passkey was last used (sign-in or confirmation), for the ten-minute rule.</summary>
    public DateTimeOffset? PasskeyConfirmedAt { get; set; }
}
