namespace Lt2.Appeals.Domain;

/// <summary>
/// <c>appeals.organisation</c> (design 8.14.2, 8.14.14 stage A): an organisation's public profile, registered by a member
/// and shown at once. Who registered it and who represents it are never public.
/// </summary>
internal sealed class Organisation
{
    public Guid Id { get; set; }

    /// <summary>The address <c>/o/{slug}</c>, made from the first name; never changes, never reused.</summary>
    public string Slug { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The name compared without case, for "no other organisation has the same name" (a partial unique index).</summary>
    public string NameKey { get; set; } = "";

    /// <summary>The organisation's own words, plain text; empty when it gave none.</summary>
    public string Description { get; set; } = "";

    /// <summary>An https address, or null.</summary>
    public string? Website { get; set; }

    /// <summary>The legal entity code (7 to 9 digits), when it gave one.</summary>
    public string? LegalCode { get; set; }

    /// <summary>"Esame registruoti lobistai": the organisation's own declaration, not checked.</summary>
    public bool DeclaredLobbyist { get; set; }

    /// <summary>The member who registered it; emptied when that account goes. Never public.</summary>
    public Guid? RegisteredByUserId { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>See <see cref="OrganisationStatuses"/>.</summary>
    public string Status { get; set; } = OrganisationStatuses.Active;

    /// <summary>See <see cref="ConfirmationKinds"/>; null while "Nepatvirtinta".</summary>
    public string? ConfirmedBy { get; set; }

    /// <summary>With a confirmation by domain: the website's host without <c>www.</c>, shown as "@domenas".</summary>
    public string? ConfirmedDomain { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>With a confirmation by hand: the moderator.</summary>
    public Guid? ConfirmedByUserId { get; set; }

    /// <summary>With a confirmation by hand: the moderator's note of what was checked (staff only).</summary>
    public string? ConfirmationNote { get; set; }

    /// <summary>A moderator withdrew a confirmation: from then on only a moderator can confirm this organisation.</summary>
    public bool DomainBarred { get; set; }

    public DateTimeOffset? BlockedAt { get; set; }

    public Guid? BlockedByUserId { get; set; }

    /// <summary>The moderator's statement for the block, shown on the profile while it is blocked.</summary>
    public string? BlockStatement { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>When a moderator last looked; null while new or changed (name or website), so it is in the queue.</summary>
    public DateTimeOffset? SeenAt { get; set; }

    /// <summary>What pre-screening found in the name and the description, for the moderator.</summary>
    public string[] ScreeningFlags { get; set; } = [];

    public DateTimeOffset ChangedAt { get; set; }
}

internal static class OrganisationStatuses
{
    public const string Active = "active";

    /// <summary>Blocked by a moderator: its statements are shown nowhere and its representatives cannot publish.</summary>
    public const string Blocked = "blocked";

    /// <summary>Closed by its last representative (or with that account): statements on open bills withdrawn.</summary>
    public const string Closed = "closed";
}

internal static class ConfirmationKinds
{
    /// <summary>"Patvirtinta per el. pašto adresą @domenas".</summary>
    public const string Domain = "domain";

    /// <summary>"Patvirtino moderatorius".</summary>
    public const string Moderator = "moderator";
}

/// <summary>
/// <c>appeals.organisation_representative</c>: a member who acts for an organisation. Visible only to the organisation's
/// other representatives; ended rows are kept until the account goes, so the history of who was a representative does not
/// rest on one row.
/// </summary>
internal sealed class OrganisationRepresentative
{
    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The representative whose link brought them; null for the one who registered it, or when that account went.</summary>
    public Guid? GrantedByUserId { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>
/// <c>appeals.organisation_invite</c>: a one-time link a representative gives a colleague (design 8.14.14). Only a hash of
/// its token is kept; nobody is looked up by email.
/// </summary>
internal sealed class OrganisationInvite
{
    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>
/// <c>appeals.organisation_code</c>: a code that confirms an organisation by its email domain (design 8.14.14). Holds the
/// domain, a hash of the code, its expiry and failed attempts; never the mailbox, which exists only in the outbox row.
/// </summary>
internal sealed class OrganisationCode
{
    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }

    /// <summary>The website's host the address was on (without <c>www.</c>).</summary>
    public string Domain { get; set; } = "";

    public byte[] CodeHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int FailedAttempts { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>The representative who asked; emptied when that account goes.</summary>
    public Guid? RequestedByUserId { get; set; }
}

/// <summary>
/// <c>appeals.organisation_statement</c>: an organisation's statement on a bill (design 8.14.3), one per organisation per
/// bill. Never counted: it is not a position. <see cref="WrittenByUserId"/> is never in a public answer.
/// </summary>
internal sealed class OrganisationStatement
{
    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }

    public string BillNumber { get; set; } = "";

    /// <summary>See <see cref="Stances"/>.</summary>
    public string Stance { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>One https link to the organisation's full position, or null.</summary>
    public string? Link { get; set; }

    /// <summary>With <c>for</c> or <c>against</c>: participants the organisation aims for on its side; never with remarks.</summary>
    public int? Goal { get; set; }

    /// <summary>See <see cref="StatementStatuses"/>.</summary>
    public string Status { get; set; } = StatementStatuses.Published;

    public string[] ScreeningFlags { get; set; } = [];

    /// <summary>When a moderator last looked; "not yet seen" while null.</summary>
    public DateTimeOffset? SeenAt { get; set; }

    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>
    /// When it first became public (design 8.14.14, stage B): published at once, or published by a moderator after it waited,
    /// or restored. Null while it never was. Edits do not move it; the follow emails go by it.
    /// </summary>
    public DateTimeOffset? FirstPublicAt { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>The representative who wrote or last changed it; emptied when that account goes. Never public.</summary>
    public Guid? WrittenByUserId { get; set; }
}

/// <summary>An organisation's stance on a bill (design 8.14.3): it asks to vote for, against, or makes remarks.</summary>
internal static class Stances
{
    public const string For = "for";
    public const string Against = "against";
    public const string Remarks = "remarks";

    public static readonly IReadOnlyList<string> All = [For, Against, Remarks];
}

internal static class StatementStatuses
{
    /// <summary>Flagged by pre-screening: waits for a moderator, seen only by the organisation's representatives.</summary>
    public const string Pending = "pending";

    public const string Published = "published";

    /// <summary>Hidden by a moderator; "Atkurti" brings it back while the bill is open.</summary>
    public const string Hidden = "hidden";

    /// <summary>Withdrawn by a representative, or by closing the organisation; shown nowhere, a new one may follow.</summary>
    public const string Withdrawn = "withdrawn";

    /// <summary>The statuses that hold the organisation's one statement on a bill (a partial unique index).</summary>
    public static readonly IReadOnlyList<string> Holding = [Pending, Published, Hidden];
}
