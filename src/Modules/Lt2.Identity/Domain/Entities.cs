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

    /// <summary>
    /// The invitation code entered at registration (normalised); counted only on completion. Never an organisation's code
    /// (design 8.14.14, stage B): that one stays in the browser until completion and is not kept afterwards.
    /// </summary>
    public string? InvitationCode { get; set; }

    /// <summary>
    /// The level of the code the registration was completed with (design 6.2), or null without a code. For an
    /// organisation's code it is all that is kept: nothing names the code or the organisation.
    /// </summary>
    public int? InvitationLevel { get; set; }

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

    /// <summary>The code as typed for comparison: upper case, without dashes or spaces (a stored, generated column).</summary>
    public string Lookup { get; set; } = "";

    /// <summary>
    /// <c>team</c> (made by the team, with its own uses and expiry), <c>personal</c> (one per member, design 6.2) or
    /// <c>organisation</c> (one per organisation, design 8.14.14 stage B).
    /// </summary>
    public string Kind { get; set; } = InvitationKinds.Team;

    /// <summary>
    /// For a team code, who made it (if known); for a personal code, its member. Cleared when that account is deleted. Never
    /// set for an organisation's code: it belongs to the organisation, not to the representative who asked for it.
    /// </summary>
    public Guid? IssuedByUserId { get; set; }

    /// <summary>For an organisation's code: the organisation (an id of the Appeals module); otherwise null.</summary>
    public Guid? OrganisationId { get; set; }

    /// <summary>
    /// For an organisation's code: null while the organisation is active, otherwise <c>blocked</c> or <c>closed</c>
    /// (<see cref="Lt2.SharedKernel.OrganisationCodeStates"/>). Separate from an administrator's switch-off, which a lifted
    /// block does not undo.
    /// </summary>
    public string? OrganisationOff { get; set; }

    /// <summary>Team codes only: uses left. Personal codes are limited by a setting instead (0 = no limit).</summary>
    public int UsesLeft { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Registrations completed with this code. Never drops, also when an invited account is deleted.</summary>
    public int UsedCount { get; set; }

    /// <summary>Level 1: team codes; a personal code's level is its member's level plus one (design 6.2).</summary>
    public int Level { get; set; } = 1;

    /// <summary>Switched off by an administrator, or because its member deleted the account.</summary>
    public DateTimeOffset? SwitchedOffAt { get; set; }

    /// <summary>Replaced by its member with a new code.</summary>
    public DateTimeOffset? ReplacedAt { get; set; }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}

internal static class InvitationKinds
{
    public const string Team = "team";
    public const string Personal = "personal";
    public const string Organisation = "organisation";
}

/// <summary>
/// <c>identity.feedback</c>: what a member told the team (design 8.10). <see cref="UserId"/> is set only when the member
/// allowed a reply; otherwise the item has a random id and only the day, so nothing leads back to its author.
/// </summary>
internal sealed class FeedbackItem
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    /// <summary><c>bug</c>, <c>idea</c>, <c>unclear</c>, <c>other</c>, or none.</summary>
    public string? Kind { get; set; }

    public string Text { get; set; } = "";

    public string? PagePath { get; set; }

    public string? Browser { get; set; }

    /// <summary><c>new</c>, <c>seen</c> or <c>done</c>.</summary>
    public string Status { get; set; } = "new";

    /// <summary>The team's internal note, for example the address of a GitHub issue.</summary>
    public string? Note { get; set; }

    /// <summary>The Vilnius day it came. No time, so an unlinked item cannot be matched to a request.</summary>
    public DateOnly CreatedOn { get; set; }

    /// <summary>The team's replies (linked items only), as JSON: when and what.</summary>
    public string Replies { get; set; } = "[]";

    /// <summary>Counted in a daily notice to the team already.</summary>
    public bool Noticed { get; set; }
}

/// <summary><c>identity.invitation_daily</c>: registrations completed per code and Vilnius day, for the administrators' chart.</summary>
internal sealed class InvitationDaily
{
    public Guid InvitationId { get; set; }

    public DateOnly Day { get; set; }

    public int Completed { get; set; }
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

/// <summary>
/// <c>identity.party_support</c> (design 5.2): the party a person says they support. Private and optional; used only for
/// group numbers of at least 20 (design 6.3). One party per person; changing it overwrites, withdrawing deletes.
/// </summary>
internal sealed class PartySupport
{
    public Guid UserId { get; set; }

    /// <summary>The party as named by the nominating party of MPs in the open data.</summary>
    public string Party { get; set; } = "";

    public DateTimeOffset GivenAt { get; set; }
}