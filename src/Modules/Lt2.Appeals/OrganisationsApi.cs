using Lt2.SharedKernel;

namespace Lt2.Appeals;

// Organisations and their statements on bills (design 8.14, stage A of 8.14.14). Codes are lower-case English words:
// organisation status active | blocked | closed; confirmed by domain | moderator (null: "Nepatvirtinta"); stance for |
// against | remarks; statement status pending | published | hidden | withdrawn.
//
// Privacy (design 8.14.2, 8.14.14): who represents an organisation is told only to its other representatives
// (OrganisationManagement), and to a moderator only through restricting a statement's writer. Nothing public carries a
// representative, who registered an organisation or who wrote a statement, and nothing ties a representative's own side
// on a bill to the organisation. A statement is not a position: no count, threshold or list reads it.

/// <summary>
/// What a reader is told about an organisation's confirmation (design 8.14.2): <paramref name="ConfirmedBy"/> null is
/// "Nepatvirtinta", <c>domain</c> "Patvirtinta per el. pašto adresą @<paramref name="Domain"/>", <c>moderator</c>
/// "Patvirtino moderatorius".
/// </summary>
public sealed record OrganisationLabel(string? ConfirmedBy, string? Domain)
{
    public static readonly OrganisationLabel None = new(null, null);
}

/// <summary>An organisation in <c>/organizacijos</c> and on the account page.</summary>
public sealed record OrganisationSummary(string Slug, string Name, OrganisationLabel Label, string Status);

public sealed record OrganisationListPage(IReadOnlyList<OrganisationSummary> Items, int Total);

/// <summary>An earlier name or website, public on the profile (design 8.14.14). Who changed it is not shown.</summary>
/// <param name="Field"><c>name</c> or <c>website</c>.</param>
public sealed record ProfileChange(DateTimeOffset At, string Field, string? OldValue, string? NewValue);

/// <summary>An earlier version of a statement, public under it on the profile (design 8.14.14).</summary>
/// <param name="ReplacedAt">When this version was replaced by the next one.</param>
public sealed record StatementVersion(DateTimeOffset ReplacedAt, string Stance, string Text, string? Link, int? Goal);

/// <summary>
/// A statement as it is shown. <paramref name="LinkHost"/> is the link's host, shown beside it; <paramref name="Progress"/>
/// the participants on the organisation's side as the bill's page counts them, with a goal (design 8.14.5).
/// </summary>
public sealed record StatementInfo(
    Guid Id, string Stance, string Text, string? Link, string? LinkHost, int? Goal, int? Progress, DateTimeOffset PublishedAt, DateTimeOffset? EditedAt, string Status);

/// <summary>A statement on the profile, by bill, with its earlier versions (oldest first).</summary>
public sealed record ProfileStatement(BillFacts Bill, StatementInfo Statement, bool BillOpen, IReadOnlyList<StatementVersion> Earlier);

/// <summary>
/// The public profile <c>/o/{slug}</c> (design 8.14.2). A blocked organisation shows the block's statement and no
/// statements; a closed one says so and shows none.
/// </summary>
public sealed record OrganisationProfile(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    string? Website,
    string? LegalCode,
    bool DeclaredLobbyist,
    OrganisationLabel Label,
    string Status,
    string? BlockStatement,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<ProfileChange> History,
    IReadOnlyList<ProfileStatement> Statements);

/// <summary>A statement in "Organizacijų pozicijos" on the bill's page, with its organisation.</summary>
public sealed record BillStatement(Guid OrganisationId, string Slug, string Name, OrganisationLabel Label, StatementInfo Statement);

/// <summary>
/// The bill's statements as everyone sees them (design 8.14.3), in the order published. <paramref name="Open"/>: the bill
/// takes statements (active, record not hidden or closed); otherwise those shown are frozen. A hidden record shows none.
/// </summary>
public sealed record BillStatements(string BillNumber, bool Open, IReadOnlyList<BillStatement> Items);

/// <summary>
/// An organisation with a public statement on a bill, as a name with its stance (design 8.7, the front page's agenda): no
/// text, no count. <c>Stance</c> is <c>for</c>, <c>against</c> or <c>remarks</c>.
/// </summary>
public sealed record BillOrganisation(string Slug, string Name, OrganisationLabel Label, string Stance);

/// <summary>One of the caller's organisations ("Mano organizacijos").</summary>
public sealed record MyOrganisation(string Slug, string Name, string Status, OrganisationLabel Label);

/// <summary>A representative as the organisation's other representatives see them: the display name only.</summary>
public sealed record RepresentativeInfo(Guid Id, string? DisplayName, bool IsMe, DateTimeOffset Since);

/// <summary>A statement of the organisation as its representatives see it, whatever its status (withdrawn ones excepted).</summary>
public sealed record RepresentedStatement(BillFacts Bill, StatementInfo Statement);

/// <summary>
/// What a representative's controls on the profile need; the caller's own answer, never cached. For anyone else
/// <paramref name="IsRepresentative"/> is false and the rest empty. <paramref name="DomainConfirmation"/> is null when a
/// code may be asked for, otherwise why not (for example <c>confirm.shared-website</c>).
/// </summary>
public sealed record OrganisationManagement(
    bool IsRepresentative,
    IReadOnlyList<RepresentativeInfo> Representatives,
    string? DomainConfirmation,
    string? WebsiteHost,
    IReadOnlyList<RepresentedStatement> Statements,
    string? Refusal);

/// <summary>
/// One organisation the caller represents, on a bill: its statement there (any status but withdrawn) and, when it cannot
/// publish now, why (<c>organisation.blocked</c>, <c>writing.restricted</c>, <c>statement.bill-not-open</c>).
/// </summary>
public sealed record MyBillOrganisation(Guid OrganisationId, string Slug, string Name, string Status, OrganisationLabel Label, StatementInfo? Statement, string? Refusal);

public sealed record NewOrganisation(string Name, string? Description, string? Website, string? LegalCode, bool DeclaredLobbyist);

public sealed record OrganisationCreated(Guid Id, string Slug);

public sealed record NewStatement(string Stance, string Text, string? Link, int? Goal);

/// <summary>A saved statement: <c>pending</c> when pre-screening flagged it, otherwise <c>published</c>.</summary>
public sealed record StatementSaved(Guid Id, string Status);

/// <summary>A representative's one-time link: the token is shown once and only its hash is kept.</summary>
public sealed record InviteCreated(string Token, DateTimeOffset ExpiresAt);

/// <summary>What the one-time link's page shows the member who opens it.</summary>
public sealed record InviteView(string Slug, string Name, bool Usable, bool AlreadyRepresentative);

/// <summary>A new or changed organisation in the moderators' queue (design 8.14.9): looked at, not approved.</summary>
public sealed record QueueOrganisation(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    string? Website,
    string? LegalCode,
    bool DeclaredLobbyist,
    OrganisationLabel Label,
    string Status,
    bool Seen,
    bool Changed,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReport> Reports,
    IReadOnlyList<ProfileChange> History,
    string? ConfirmationNote,
    bool DomainBarred,
    DateTimeOffset RegisteredAt);

/// <summary>A statement for a moderator: waiting, flagged, reported, or not yet seen. Who wrote it is not shown.</summary>
public sealed record QueueStatement(
    Guid Id,
    BillFacts Bill,
    Guid OrganisationId,
    string Slug,
    string OrganisationName,
    OrganisationLabel Label,
    string Stance,
    string Text,
    string? Link,
    int? Goal,
    string Status,
    bool Seen,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReport> Reports,
    DateTimeOffset PublishedAt,
    bool BillFinished = false);

public sealed record OrganisationModerationItems(IReadOnlyList<QueueOrganisation> Organisations, IReadOnlyList<QueueStatement> Statements);

/// <summary>A blocked organisation on the moderators' list of hidden items; "Atkurti" lifts the block.</summary>
public sealed record HiddenOrganisation(Guid Id, string Slug, string Name, DateTimeOffset? BlockedAt, string? BlockedBy, string? Statement);

/// <summary>A hidden statement; <paramref name="Restorable"/> while its bill takes statements.</summary>
public sealed record HiddenStatement(
    Guid Id, BillFacts Bill, string OrganisationName, string Stance, string Text, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, bool Restorable);

public sealed record HiddenOrganisationItems(IReadOnlyList<HiddenOrganisation> Organisations, IReadOnlyList<HiddenStatement> Statements);

/// <summary>Organisations and their statements (design 8.14.14 stage A), for members and visitors.</summary>
public interface IOrganisations
{
    /// <summary><c>/organizacijos</c>: active organisations in the order of the alphabet, with a search by name.</summary>
    Task<OrganisationListPage> ListAsync(string? search, CancellationToken cancellationToken);

    /// <summary>The public profile; <c>organisation.not-found</c> for an unknown slug.</summary>
    Task<Result<OrganisationProfile>> ProfileAsync(string slug, CancellationToken cancellationToken);

    /// <summary>"Organizacijų pozicijos" on a bill; <c>bill.not-found</c> for an unknown bill.</summary>
    Task<Result<BillStatements>> OnBillAsync(string billNumber, CancellationToken cancellationToken);

    /// <summary>
    /// For each given bill, the organisations whose statement on it is public, in one read for all the bills, ordered as on
    /// the bill's page: for, against, remarks, and within each in the order published (design 8.7). Blocked and closed
    /// organisations and hidden, waiting or withdrawn statements are not there; a bill without any is missing. Whether a
    /// bill's record is hidden is the caller's to check (its page then shows none).
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<BillOrganisation>>> ShownOnBillsAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);

    /// <summary>A member registers an organisation and becomes its first representative; shown at once.</summary>
    Task<Result<OrganisationCreated>> RegisterAsync(Guid userId, NewOrganisation request, CancellationToken cancellationToken);

    /// <summary>A representative changes the profile; earlier names and websites stay in its public history.</summary>
    Task<Result<Unit>> ChangeAsync(string slug, Guid userId, NewOrganisation request, CancellationToken cancellationToken);

    /// <summary>"Mano organizacijos": the organisations the caller represents.</summary>
    Task<IReadOnlyList<MyOrganisation>> MineAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The caller's own view of an organisation's management; empty for anyone who is not its representative.</summary>
    Task<Result<OrganisationManagement>> ManagementAsync(string slug, Guid userId, CancellationToken cancellationToken);

    /// <summary>Sends a confirmation code to an address on the website's domain; only the domain is kept.</summary>
    Task<Result<Unit>> SendCodeAsync(string slug, Guid userId, string address, CancellationToken cancellationToken);

    /// <summary>Confirms the organisation by the code; <c>confirm.code-invalid</c> for any failure.</summary>
    Task<Result<Unit>> ConfirmAsync(string slug, Guid userId, string code, CancellationToken cancellationToken);

    /// <summary>A one-time link, valid seven days, that makes a signed-in member who accepts it a representative.</summary>
    Task<Result<InviteCreated>> InviteAsync(string slug, Guid userId, CancellationToken cancellationToken);

    Task<Result<InviteView>> InviteViewAsync(string token, Guid userId, CancellationToken cancellationToken);

    /// <summary>Accepts a one-time link; returns the organisation's slug.</summary>
    Task<Result<string>> AcceptInviteAsync(string token, Guid userId, CancellationToken cancellationToken);

    /// <summary>A representative removes another, by the representative's id (never an account id).</summary>
    Task<Result<Unit>> RemoveRepresentativeAsync(string slug, Guid representativeId, Guid userId, CancellationToken cancellationToken);

    /// <summary>A representative leaves; the last one cannot and closes the organisation instead.</summary>
    Task<Result<Unit>> LeaveAsync(string slug, Guid userId, CancellationToken cancellationToken);

    /// <summary>The last representative closes the organisation: statements on open bills are withdrawn.</summary>
    Task<Result<Unit>> CloseAsync(string slug, Guid userId, CancellationToken cancellationToken);

    /// <summary>The organisations the caller represents and their statements on one bill; the caller's own answer.</summary>
    Task<Result<IReadOnlyList<MyBillOrganisation>>> MineOnBillAsync(string billNumber, Guid userId, CancellationToken cancellationToken);

    /// <summary>Publishes or changes the organisation's one statement on the bill.</summary>
    Task<Result<StatementSaved>> SaveStatementAsync(string billNumber, string slug, Guid userId, NewStatement request, CancellationToken cancellationToken);

    /// <summary>Withdraws the organisation's statement while the bill is open.</summary>
    Task<Result<Unit>> WithdrawStatementAsync(string billNumber, string slug, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// For the organisation's invitation code (design 8.14.14, stage B), to its representatives only
    /// (<c>organisation.representatives-only</c> for anyone else): the organisation's id, whether it is active, and the bills it
    /// has a public statement on, for a link per bill.
    /// </summary>
    Task<Result<OrganisationCodeAccess>> CodeAccessAsync(string slug, Guid userId, CancellationToken cancellationToken);

    /// <summary>Organisations' names by id, for the administrators' invitations page (design 8.14.14, stage B).</summary>
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>What the host needs for a representative's view of the organisation's invitation code (design 8.14.14, stage B).</summary>
/// <param name="Bills">The bills the organisation has a public statement on, newest statement first.</param>
public sealed record OrganisationCodeAccess(Guid OrganisationId, bool Active, IReadOnlyList<BillFacts> Bills);

/// <summary>
/// Moderation of organisations and statements after the fact (design 8.14.9, 8.14.14). Every action has a statement (a
/// note for "Palikti" and confirming), is recorded, audited and emailed to the organisation's representatives with the
/// moderators' <c>contact</c> address.
/// </summary>
public interface IOrganisationModeration
{
    Task<OrganisationModerationItems> QueueAsync(CancellationToken cancellationToken);

    /// <summary>The bill's statements that wait, are flagged or reported, for the "Moderavimas" panel on its page.</summary>
    Task<Result<IReadOnlyList<QueueStatement>>> OnBillAsync(string billNumber, CancellationToken cancellationToken);

    Task<HiddenOrganisationItems> HiddenAsync(CancellationToken cancellationToken);

    /// <summary>"Palikti": the organisation was looked at and stays as it is; its open reports close.</summary>
    Task<Result<Unit>> KeepAsync(Guid organisationId, string? note, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    Task<Result<Unit>> BlockAsync(Guid organisationId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>Lifts a block: exactly the statements that were shown before come back.</summary>
    Task<Result<Unit>> UnblockAsync(Guid organisationId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>Confirms by hand, with a required note of what was checked: "Patvirtino moderatorius".</summary>
    Task<Result<Unit>> ConfirmAsync(Guid organisationId, string note, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>Withdraws any confirmation; after that only a moderator can confirm the organisation.</summary>
    Task<Result<Unit>> WithdrawConfirmationAsync(Guid organisationId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>Publishes a waiting statement, or marks a published one seen ("Palikti"); its open reports close.</summary>
    Task<Result<Unit>> PublishStatementAsync(Guid statementId, string? statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>Design 6.9: "Palikti" on a statement that still waited when the bill closed; it stays unpublished.</summary>
    Task<Result<Unit>> KeepStatementAsync(Guid statementId, string? statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    Task<Result<Unit>> HideStatementAsync(Guid statementId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>"Atkurti": a hidden statement comes back as published, while its bill takes statements.</summary>
    Task<Result<Unit>> RestoreStatementAsync(Guid statementId, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);

    /// <summary>What a moderator sees before restricting a statement's writer, as for an argument's author (design 8.3).</summary>
    Task<Result<AuthorView>> WriterOfStatementAsync(Guid statementId, CancellationToken cancellationToken);

    Task<Result<Guid>> RestrictWriterOfStatementAsync(Guid statementId, DateOnly? until, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken);
}
