using System.Security.Claims;
using Lt2.SharedKernel;

namespace Lt2.Identity;

/// <summary>Registration request as the module sees it; validated by the shared contracts first.</summary>
/// <param name="OfficeInvitation">An MP office member's invitation code in place of an invitation code (design 8.5.3, owner 2026-11-17).</param>
public sealed record Registration(string Email, string? InvitationCode, string? OfficeInvitation = null);

/// <summary>What a person gives once their address is proven (design 6.2), with the terms of use accepted (design 8.22.2).</summary>
public sealed record RegistrationCompletion(string? DisplayName, bool ConfirmsAdult, string? InvitationCode, bool AcceptsTerms, string? OfficeInvitation = null);

/// <summary>
/// The signed-in person's own account. <paramref name="StaffAccount"/>: the account holds an activated staff role (whatever
/// this session is), so deleting it needs a session signed in with its staff passkey (design 6.9).
/// <paramref name="HasPositionConsent"/>: the positions consent to the text in force; <paramref name="OlderPositionConsent"/>:
/// none to that text, but one to an older text not withdrawn (design 6.5, owner 2026-11-19).
/// </summary>
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
    bool HasPositionConsent = false,
    bool StaffAccount = false,
    MpOffer? MpOffer = null,
    MpLinkState? Mp = null,
    bool TermsRequired = false,
    bool PasskeySession = false,
    bool OlderPositionConsent = false);

/// <summary>
/// A sign-in's outcome: <paramref name="AccountChanged"/> when the browser was signed in to another account, whose session
/// ended with this sign-in.
/// </summary>
public sealed record SignInOutcome(bool AccountChanged);

/// <summary>
/// Design 8.5.1: the account's proven address is this MP's official address and the account is not linked yet; it is
/// offered "Perimti profilį". Only the account's own answer says so.
/// </summary>
public sealed record MpOffer(string Slug, string FirstName, string LastName);

/// <summary>
/// Design 8.5.4: the MP the account is linked to, in which part (<c>mp</c> or <c>office</c>), and whether this session may
/// speak for the MP (a passkey session).
/// </summary>
public sealed record MpLinkState(string Slug, string FirstName, string LastName, string Part, bool MayWrite);

/// <summary>An office member as the MP's account sees them (design 8.5.3): the display name and since when. Nobody else does.</summary>
public sealed record OfficeMember(Guid Id, string? DisplayName, DateTimeOffset Since);

/// <summary>The MP's office: its members and how many it may have.</summary>
public sealed record MpOffice(IReadOnlyList<OfficeMember> Members, int Limit);

/// <summary>
/// An office member's one-time invitation, shown once: its code (the secret after the link's <c>#</c>, also typed on the page,
/// design 8.5.3), when it stops working, and whether the site emailed it (owner, 2026-11-17).
/// </summary>
public sealed record OfficeInvitation(string Token, DateTimeOffset ExpiresAt, bool Emailed = false);

/// <summary>
/// MPs on the platform (design 8.5.1 to 8.5.4): the take-over of a profile through a link to the official address and a new
/// passkey, the office, giving the profile up, an administrator's end of a link. Every request that speaks for an MP reads
/// the link and the session from the database. Who is linked to which MP and who the office members are is never public.
/// </summary>
public interface IPoliticians
{
    /// <summary>
    /// "Perimti profilį": sends a take-over link to the account's own address when it is the official address of the MP
    /// <paramref name="slug"/>; a new link replaces the old one; at most three a day per MP. Also replaces a lost passkey.
    /// </summary>
    Task<Result<Unit>> ClaimAsync(ClaimsPrincipal user, string slug, string? ipHash, CancellationToken cancellationToken);

    /// <summary>
    /// In one transaction: checks the link's token and that the address is still the MP's official address, stores the new
    /// passkey, removes the account's other passkeys and their sessions, links the account and withdraws its sides; audited,
    /// the official address told. The session then becomes a passkey session.
    /// </summary>
    Task<Result<MpLinkState>> TakeOverAsync(ClaimsPrincipal user, string token, string credentialJson, string? ipHash, CancellationToken cancellationToken);

    /// <summary>The MP's account gives the profile up; an office member leaves. A passkey session.</summary>
    Task<Result<Unit>> EndOwnAsync(ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken);

    /// <summary>The office members, for the MP's account only.</summary>
    Task<Result<MpOffice>> OfficeAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>
    /// A one-time invitation for an office member (seven days), made by the MP's account in a passkey session; emailed by the site
    /// to <paramref name="email"/> when one is given (owner, 2026-11-17), the address kept only in the outbox row.
    /// </summary>
    Task<Result<OfficeInvitation>> InviteOfficeMemberAsync(ClaimsPrincipal user, string? email, string? ipHash, CancellationToken cancellationToken);

    /// <summary>
    /// A member accepts an office invitation and becomes an office member of that MP (design 8.5.3): in a passkey session, or
    /// (owner, 2026-11-17) in a code session with <paramref name="newPasskey"/> (a passkey made now, stored in the same
    /// transaction) or <paramref name="passkey"/> (an assertion with one of the account's passkeys); the session then becomes
    /// a passkey session under a new key.
    /// </summary>
    Task<Result<MpLinkState>> AcceptOfficeInvitationAsync(ClaimsPrincipal user, string token, string? newPasskey, string? passkey, string? ipHash, CancellationToken cancellationToken);

    /// <summary>The MP's account removes an office member, in a passkey session.</summary>
    Task<Result<Unit>> RemoveOfficeMemberAsync(ClaimsPrincipal user, Guid memberId, string? ipHash, CancellationToken cancellationToken);

    /// <summary>An administrator ends an MP's link with a statement (the caller checks the fresh confirmation).</summary>
    Task<Result<Unit>> EndByAdministratorAsync(string slug, string statement, StaffActor administrator, CancellationToken cancellationToken);

    /// <summary>The public trace (design 8.5.2): of the given MPs, those whose profile is taken over, and since when.</summary>
    Task<IReadOnlyDictionary<long, DateTimeOffset>> TakenOverAsync(IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken);

    /// <summary>
    /// Whom the signed-in account speaks for (design 8.5.4, 8.5.5), read from the database on every call: the MP's Seimas person
    /// number, in which part, and whether this session may write (a passkey session). Null for an account without an open link.
    /// </summary>
    Task<MpSpeaker?> SpeakerAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>
/// The account speaking for an MP (design 8.5.4): who it is, for which MP (the Seimas person number), as the MP's own account
/// (<c>mp</c>) or an office member (<c>office</c>), and whether this session may write. Only the host sees it; no public answer
/// or log names the account.
/// </summary>
public sealed record MpSpeaker(Guid UserId, long SeimasPersonId, string Part, bool MayWrite);

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

    /// <summary>
    /// Design 8.3, the full lock: in one transaction every session of the account ends and no sign-in works until it is unlocked,
    /// its public profile is not shown and its follows send nothing; its texts stay. Audited with the statement (no address); the
    /// person gets one email with it and <paramref name="contact"/>. The caller checks the administrator and the fresh confirmation.
    /// <c>statement.required</c>, <c>account.not-found</c>, <c>account.lock-self</c>, <c>account.lock-staff</c> (it holds an active staff
    /// role: remove the role first), <c>account.locked</c> (already).
    /// </summary>
    Task<Result<Unit>> LockAccountAsync(string email, string statement, StaffActor administrator, string? contact, CancellationToken cancellationToken);

    /// <summary>Unlocks the account, with a statement; audited; one email. <c>account.not-locked</c> when it is not locked.</summary>
    Task<Result<Unit>> UnlockAccountAsync(string email, string statement, StaffActor administrator, string? contact, CancellationToken cancellationToken);
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
/// A browser known to the account (design 8.20.2): the browser and system it said when it last signed in, when it first and
/// last signed in, and whether it is the browser asking. Never the cookie or its hash.
/// </summary>
public sealed record KnownBrowserSummary(Guid Id, string? Label, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, bool IsCurrent);

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

    /// <summary>
    /// Signs in with the emailed code under a new session key. A browser signed in to another account ends that session, as
    /// by signing out, and the outcome says so (<see cref="SignInOutcome.AccountChanged"/>).
    /// </summary>
    Task<Result<SignInOutcome>> SignInWithCodeAsync(string email, string code, CancellationToken cancellationToken);

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
    /// Design 6.2: the signed-in account's code session becomes a staff session by an assertion with the account's own staff
    /// passkey, under a new session key (the old session ends); refused with <c>passkey.staff-required</c> for any other
    /// passkey or an account without an activated staff role, changing nothing. Audited; no email.
    /// </summary>
    Task<Result<Unit>> StartStaffSessionAsync(ClaimsPrincipal user, string credentialJson, string? ipHash, CancellationToken cancellationToken);

    /// <summary>
    /// Uses a role activation code in the current passkey session: that passkey becomes the staff passkey and every
    /// other passkey on the account is removed.
    /// </summary>
    Task<Result<Unit>> ActivateRoleAsync(ClaimsPrincipal user, string code, string? ipHash, CancellationToken cancellationToken);

    /// <summary>What the current session may do as staff; checked on every staff request.</summary>
    Task<StaffSession> GetStaffSessionAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<Result<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken);

    /// <summary>Signs in with a passkey under a new session key; as <see cref="SignInWithCodeAsync"/> for a browser signed in to another account.</summary>
    Task<Result<SignInOutcome>> SignInWithPasskeyAsync(string credentialJson, CancellationToken cancellationToken);

    /// <summary>
    /// The signed-in account's session becomes a passkey session by an assertion with one of its own passkeys, under a new
    /// session key (the old session ends); a staff session too when it is the account's staff passkey. Another account's
    /// passkey is refused with <c>passkey.other-account</c>, an assertion that fails with <c>passkey.invalid</c>, changing
    /// nothing. Audited; no email.
    /// </summary>
    Task<Result<Unit>> StartPasskeySessionAsync(ClaimsPrincipal user, string credentialJson, string? ipHash, CancellationToken cancellationToken);

    Task<Result<AccountSummary>> GetAccountAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<Result<Unit>> EndSessionAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>The signed-in person's known browsers (design 8.20.2), last seen first.</summary>
    Task<Result<IReadOnlyList<KnownBrowserSummary>>> GetKnownBrowsersAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>"Pamiršti": the browser becomes a stranger to the account again (design 8.20.2); <c>known-browser.not-found</c>.</summary>
    Task<Result<Unit>> ForgetKnownBrowserAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken);

    /// <summary>Records the explicit consent to store positions against the text version in force (design 6.5).</summary>
    Task<Result<Unit>> GivePositionConsentAsync(ClaimsPrincipal user, string textVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Accepts the terms of use (design 8.22.2) for a member, against the version in force only (<c>terms.version</c>
    /// otherwise); accepting a version already accepted changes nothing.
    /// </summary>
    Task<Result<Unit>> AcceptTermsAsync(ClaimsPrincipal user, string textVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the signed-in account is a member who must accept the terms in force before writing (design 8.22.2), read from
    /// the database: false for a visitor and for a pending account (which accepts them when completing its registration).
    /// </summary>
    Task<bool> TermsRequiredAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>Withdraws the consent and, in the same transaction, deletes every position of the person (design 6.5).</summary>
    Task<Result<Unit>> WithdrawPositionConsentAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>The party no longer shown: <c>party-public</c> withdrawn at once (open behind the terms, design 8.22.2).</summary>
    Task<Result<Unit>> HidePartyAsync(ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken);

    /// <summary>
    /// The party support withdrawn (design 5.2): the party removed, its consent withdrawn and the party no longer shown; open
    /// behind the terms (design 8.22.2). Giving one is the "Profilis" form's (<see cref="IMemberProfiles.SaveAsync"/>).
    /// </summary>
    Task<Result<Unit>> WithdrawPartySupportAsync(ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken);

    /// <summary>
    /// Design 6.2, changing the email address: a code to the address in use, the proof a code session gives (the account's code
    /// like any other, with the sign-in code's limits). Refused where a code proves nothing: a staff account
    /// (<c>passkey.staff-required</c>, <c>passkey.confirmation-required</c>) and the MP's own account (<c>mp.passkey-required</c>).
    /// </summary>
    Task<Result<Unit>> SendEmailProofCodeAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>
    /// The new address, with the proof: a passkey session, or <paramref name="proofCode"/> sent to the address in use
    /// (<c>email.proof-required</c> without it, <c>auth.code-invalid</c> on <c>proofCode</c> for a wrong one). Refused for an address
    /// that is no address (<c>email.invalid</c>), a disposable one (<c>email.disposable</c>) or the one in use (<c>email.same</c>).
    /// Otherwise the same answer whatever the address: a code goes to it, or another member's account holding it is told.
    /// </summary>
    Task<Result<Unit>> ChangeEmailAsync(ClaimsPrincipal user, string email, string? proofCode, CancellationToken cancellationToken);

    /// <summary>
    /// The code from the new address, in the session that asked: the account moves to it (<c>auth.code-invalid</c> otherwise), its
    /// other sessions end and this one goes on under a new key; the old address gets the link back. Answers the new address.
    /// </summary>
    Task<Result<string>> ConfirmEmailChangeAsync(ClaimsPrincipal user, string code, string? ipHash, CancellationToken cancellationToken);

    /// <summary>
    /// The old address's link (design 6.2): puts it back and ends every session of the account, without a session; one answer,
    /// <c>email.restore-invalid</c>, for a link that cannot.
    /// </summary>
    Task<Result<Unit>> RestoreEmailAsync(string token, string? ipHash, CancellationToken cancellationToken);

    /// <summary>Everything the person gave (design 6.5), their own data only.</summary>
    Task<Result<object>> ExportAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>Deletes the account and everything the person gave (design 6.5), and signs out.</summary>
    Task<Result<Unit>> DeleteAccountAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>Housekeeping run by the worker.</summary>
public interface IIdentityMaintenance
{
    /// <summary>
    /// Deletes sign-in codes older than a day, expired sessions, known browsers not seen for a year (design 8.20.2), and
    /// accounts still pending after the configured number of days; returns how many rows went.
    /// </summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}

/// <summary>A member's own invitation code (design 6.2): the code, how many completed registration with their codes, and whether it is on.</summary>
/// <param name="Usable">The code would let a registration through now: on, not replaced, not used up (design 8.11.5).</param>
public sealed record MyInvitation(string Code, int Completed, bool On, bool Usable = true);

public sealed record InvitationDay(DateOnly Day, int Completed);

/// <summary>
/// One code for administrators: a team code by its code, a personal code by its member's display name (null once deleted),
/// an organisation's code by <paramref name="OrganisationId"/> (the host names it). <paramref name="On"/> is the
/// administrator's switch; <paramref name="OrganisationOff"/> is <c>blocked</c> or <c>closed</c> while the organisation is.
/// </summary>
public sealed record InvitationCodeStats(
    Guid Id, string Kind, string? Label, int Completed, bool On, bool Replaced, IReadOnlyList<InvitationDay> Days, Guid? OrganisationId = null, string? OrganisationOff = null);

/// <summary>Registrations completed at one level: 1 by team codes, 2 by codes of level-1 people, and so on.</summary>
public sealed record InvitationLevel(int Level, int Completed);

/// <summary>The administrators' numbers (design 6.2): per day, per code, per level. Never who invited whom.</summary>
public sealed record InvitationStats(
    DateOnly From,
    DateOnly To,
    int Total,
    IReadOnlyList<InvitationDay> Days,
    IReadOnlyList<InvitationCodeStats> Codes,
    IReadOnlyList<InvitationLevel> Levels,
    int WithoutCode);

/// <summary>Personal invitation codes and the administrators' numbers (design 6.2).</summary>
public interface IInvitations
{
    /// <summary>The member's own code, made when first needed.</summary>
    Task<Result<MyInvitation>> MineAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A new code; the old one stops working; the count carries over. Not while an administrator has it switched off.</summary>
    Task<Result<MyInvitation>> ReplaceAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The last <paramref name="days"/> days; <paramref name="code"/> narrows the codes to one typed code.</summary>
    Task<InvitationStats> StatsAsync(int days, string? code, CancellationToken cancellationToken);

    /// <summary>An administrator switches a code off or on again, with a statement; audited.</summary>
    Task<Result<Unit>> SwitchAsync(Guid invitationId, bool on, string statement, Guid adminUserId, string role, string? ipHash, CancellationToken cancellationToken);
}

/// <summary>
/// An organisation's invitation code as its representatives see it (design 8.14.14, stage B): the code (null while none was
/// made), one number (everyone who completed registration with its codes), whether it lets a registration through now,
/// whether an administrator has it on, and whether it is off with its organisation (blocked or closed).
/// </summary>
public sealed record OrganisationInvitation(string? Code, int Completed, bool Usable, bool On, bool OffWithOrganisation);

/// <summary>
/// Organisations' invitation codes (design 8.14.14, stage B). The caller (the host) has checked with the Appeals module that
/// the person represents the organisation and whether it is active; this module knows only the organisation's id.
/// </summary>
public interface IOrganisationInvitations
{
    /// <summary>
    /// The organisation's current code, made when first asked for while it is active, at the level a personal code of
    /// <paramref name="requestedByUserId"/> would have; nothing of that person is kept on it.
    /// </summary>
    Task<Result<OrganisationInvitation>> ForOrganisationAsync(Guid organisationId, bool active, Guid requestedByUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A new code; the old one stops working; the count carries over. Not while the organisation is not active
    /// (<c>invitation.organisation-inactive</c>) or an administrator has the code switched off (<c>invitation.organisation-switched-off</c>).
    /// </summary>
    Task<Result<OrganisationInvitation>> ReplaceAsync(Guid organisationId, bool active, CancellationToken cancellationToken);
}

/// <summary>
/// The "Profilis" form's state (design 8.28.5): the display name; "Apie mane", whether a moderator hid it (with the statement);
/// the public id while "Viešas profilis" is on and the consent text's version in force; <c>Texts:MaxLength</c>; the party
/// supported (private, design 5.2) and whether it is shown on the public profile (design 8.25.9: the consent
/// <c>party-public</c> in force while the profile is public).
/// </summary>
public sealed record MyProfileState(
    string? DisplayName, string? About, bool AboutHidden, string? AboutHiddenStatement, string? ProfileId, DateTimeOffset? PublicSince, string ConsentVersion,
    int TextMaxLength, string? Address, string? Party, bool PartyPublic);

/// <summary>
/// The whole "Profilis" form (design 8.28.5), saved whole or not at all: the display name, "Apie mane", "Viešas profilis" with
/// the version of the consent text the form showed, the party with its explicit consent, and "Rodyti viešame profilyje" with
/// the version of its text. An unticked box withdraws its consent.
/// </summary>
public sealed record ProfileForm(
    string? DisplayName, string? About, bool Public, string? PublicConsentVersion, string? Party, bool PartyConsent, bool PartyPublic, string? PartyPublicConsentVersion);

/// <summary>A report of a profile as the moderators see it: never who reported.</summary>
public sealed record ProfileReportInfo(Guid Id, string Ground, string? Text, DateTimeOffset CreatedAt);

/// <summary>
/// A profile for the moderators (design 8.25.6): reported, or with "Apie mane" hidden. <paramref name="Id"/> is an opaque key
/// for the moderators' actions; no address, side, follow or party.
/// </summary>
public sealed record ProfileModerationItem(
    Guid Id, string? ProfileId, string? DisplayName, string? About, DateTimeOffset? HiddenAt, string? HiddenStatement, bool Restorable, IReadOnlyList<ProfileReportInfo> Reports);

/// <summary>Public profiles: the person's own form (design 8.25), reports and moderation (design 8.25.6).</summary>
public interface IMemberProfiles
{
    Task<Result<MyProfileState>> MineAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the whole "Profilis" form (design 8.28.5) in one transaction, or nothing: every changed part is checked first and
    /// every refusal is answered at once, each with its field (<see cref="Error.Refusals"/>): <c>display-name.too-long</c>,
    /// <c>about.too-long</c>, <c>about.personal-code</c>, <c>writing.restricted</c> (a changed name or text),
    /// <c>profile.consent-outdated</c>, <c>profile.name-required</c>, <c>party.unknown</c> (not one of
    /// <paramref name="parties"/>), <c>party.consent-required</c>, <c>party.consent-outdated</c>, <c>party.none</c>,
    /// <c>party.profile-off</c>, <c>mp.takes-no-part</c>. An unchanged part is not checked, written or audited.
    /// </summary>
    Task<Result<MyProfileState>> SaveAsync(ClaimsPrincipal user, ProfileForm form, IReadOnlyCollection<string> parties, string? ipHash, CancellationToken cancellationToken);

    /// <summary>"Viešas profilis" off and nothing else (design 8.25.1): open behind the terms (8.22.2) and under a writing restriction.</summary>
    Task<Result<MyProfileState>> SwitchOffAsync(ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken);

    /// <summary>
    /// A member reports a public profile: <c>member.not-found</c> for an id that is not public, <c>report.ground</c>,
    /// <c>report.too-long</c>, <c>member.required</c>; reporting again while one is open changes nothing.
    /// </summary>
    Task<Result<Unit>> ReportAsync(Guid reporterUserId, string profileId, string ground, string? text, CancellationToken cancellationToken);

    /// <summary>Profiles with open reports, then hidden texts, for the moderators.</summary>
    Task<IReadOnlyList<ProfileModerationItem>> QueueAsync(CancellationToken cancellationToken);

    /// <summary>Hides "Apie mane" with a statement emailed to the person; its open reports close; audited without the text.</summary>
    Task<Result<Unit>> HideAboutAsync(Guid id, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>"Palikti": the open reports close and nothing changes; audited.</summary>
    Task<Result<Unit>> KeepAsync(Guid id, string? statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Brings a hidden text back while the person has not written another; emailed; audited.</summary>
    Task<Result<Unit>> RestoreAboutAsync(Guid id, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);
}

/// <summary>The report grounds of a profile (design 8.25.6): the general ones and "Apsimeta kitu asmeniu ar pareigūnu".</summary>
public static class ProfileReportGrounds
{
    public static readonly IReadOnlyList<string> All = ["impersonation", "illegal", "personal-data", "insult", "spam", "other"];

    public const int TextMaxLength = 1000;
}

/// <summary>What a member sends (design 8.10). <c>MayReply</c> decides whether the item is linked to the account at all.</summary>
public sealed record NewFeedback(string Text, string? Kind, string? PagePath, string? Browser, bool MayReply);

public sealed record FeedbackReply(DateTimeOffset At, string Text);

/// <summary>A feedback item for staff: the display name only on linked items; an unlinked item has no author.</summary>
public sealed record FeedbackInfo(
    Guid Id, string? Kind, string Text, string Status, string? Note, string? PagePath, string? Browser, DateOnly CreatedOn, bool Linked, string? DisplayName, IReadOnlyList<FeedbackReply> Replies);

/// <summary>Feedback from members to the team (design 8.10).</summary>
public interface IFeedback
{
    /// <summary>A member sends feedback; linked to the account only when they allow a reply. The per-account daily limit is the caller's.</summary>
    Task<Result<Unit>> SendAsync(Guid userId, NewFeedback feedback, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeedbackInfo>> ListAsync(string? kind, string? status, CancellationToken cancellationToken);

    /// <summary>Changes an item's status and the team's note; audited with the old and new status and who did it, never a text (design 6.9).</summary>
    Task<Result<Unit>> UpdateAsync(Guid id, string status, string? note, StaffActor staff, CancellationToken cancellationToken);

    /// <summary>A reply to a linked item, sent through the outbox to the member's address; audited without its text.</summary>
    Task<Result<Unit>> ReplyAsync(Guid id, string text, StaffActor staff, CancellationToken cancellationToken);

    /// <summary>At most one notice a day to the team address with the number of new items; none when nothing is new.</summary>
    Task<int> SendNoticeAsync(string? teamAddress, CancellationToken cancellationToken);
}

/// <summary>What the feedback form suggests sending along (design 8.10): the page path only, the browser's name and version.</summary>
public static class FeedbackSuggestions
{
    public static string? PagePath(string? path) => Domain.FeedbackRules.PagePath(path);

    public static string? Browser(string? userAgent) => Domain.FeedbackRules.Browser(userAgent);
}
