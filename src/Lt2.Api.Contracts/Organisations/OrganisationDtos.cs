using Lt2.Api.Contracts.Appeals;

namespace Lt2.Api.Contracts.Organisations;

// Design 8.14.14, stage A: organisations and their statements on bills. Codes are lower-case English words: status active |
// blocked | closed; confirmedBy domain | moderator (null: "Nepatvirtinta"); stance for | against | remarks; statement status
// pending | published | hidden | withdrawn. No public answer names a representative or who wrote a statement; the only
// answers that hold representatives are the caller's own (OrganisationManagementResponse, to a representative only).

/// <summary>The limits of organisations and statements, the same numbers the server enforces (design 8.14.14).</summary>
public static class OrganisationLimits
{
    public const int NameMinLength = 3;
    public const int NameMaxLength = 120;

    /// <summary>A legal entity code: 7 to 9 digits (optional).</summary>
    public const int LegalCodeMinLength = 7;

    public const int LegalCodeMaxLength = 9;
    public const int DescriptionMaxLength = 500;
    public const int WebsiteMaxLength = 200;
    public const int StatementMaxLength = 1500;
    public const int LinkMaxLength = 500;
    public const int GoalMax = 1_000_000;
    public const int RepresentativesMax = 10;

    /// <summary>The page a representative's one-time link opens, without the token.</summary>
    public const string InvitePage = "/organizacijos/kvietimas";

    public static readonly IReadOnlyList<string> Stances = ["for", "against", "remarks"];

    /// <summary>The stances that may carry a goal (design 8.14.5).</summary>
    public static readonly IReadOnlyList<string> GoalStances = ["for", "against"];

    /// <summary>The page a one-time link opens (design 8.14.14); the token is after the <c>#</c>, so the browser never sends it (design 6.8).</summary>
    public static string InvitePath(string token) => InvitePage + "#" + Uri.EscapeDataString(token);
}

/// <summary>
/// The links users give (design 8.14.14: an organisation's website, a statement's link): <c>https</c> only, a host name
/// with at least one dot, no user name or password, no white space, within a length; shown with the host visible and never
/// fetched by the server. One rule for the forms and the server (the module checks a copy, held to this by a unit test).
/// </summary>
public static class UserLink
{
    public static bool IsAcceptable(string? value, int maxLength)
    {
        var address = value?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > maxLength || address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\'))
        {
            return false;
        }

        const string Prefix = "https://";
        if (!address.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var authority = address[Prefix.Length..];
        var end = authority.IndexOfAny(['/', '?', '#']);
        if ((end < 0 ? authority : authority[..end]).Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
            && uri.HostNameType == UriHostNameType.Dns && uri.IdnHost.Trim('.').Contains('.', StringComparison.Ordinal);
    }

    /// <summary>Empty, or acceptable.</summary>
    public static bool IsEmptyOrAcceptable(string? value, int maxLength) => string.IsNullOrWhiteSpace(value) || IsAcceptable(value, maxLength);

    /// <summary>The host shown beside a link, without <c>www.</c>.</summary>
    public static string Host(string address)
    {
        var host = Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : address;
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}

/// <summary>
/// The organisation's confirmation as a reader sees it: <c>ConfirmedBy</c> null is "Nepatvirtinta", <c>domain</c> "Patvirtinta
/// per el. pašto adresą @<c>Domain</c>", <c>moderator</c> "Patvirtino moderatorius".
/// </summary>
public sealed record OrganisationLabelResponse(string? ConfirmedBy, string? Domain);

public sealed record OrganisationSummaryResponse(string Slug, string Name, OrganisationLabelResponse Label, string Status);

public sealed record OrganisationListResponse(IReadOnlyList<OrganisationSummaryResponse> Items, int Total);

/// <summary>An earlier name or website (<c>Field</c>: <c>name</c> or <c>website</c>), public on the profile; who changed it is not shown.</summary>
public sealed record ProfileChangeResponse(DateTimeOffset At, string Field, string? OldValue, string? NewValue);

/// <summary>An earlier version of a statement, replaced at <c>ReplacedAt</c>.</summary>
public sealed record StatementVersionResponse(DateTimeOffset ReplacedAt, string Stance, string Text, string? Link, int? Goal);

/// <summary>
/// A statement. <c>LinkHost</c> is shown beside the link; <c>Progress</c> is the participants on the organisation's side as
/// the bill's page counts them, with a goal ("Siekiame {N} dalyvių · dabar {n}"). Never who wrote it.
/// </summary>
public sealed record StatementResponse(
    Guid Id, string Stance, string Text, string? Link, string? LinkHost, int? Goal, int? Progress, DateTimeOffset PublishedAt, DateTimeOffset? EditedAt, string Status);

public sealed record ProfileStatementResponse(BillRefResponse Bill, StatementResponse Statement, bool BillOpen, IReadOnlyList<StatementVersionResponse> Earlier);

/// <summary>
/// The public profile <c>/o/{slug}</c>. A blocked organisation carries <c>BlockStatement</c> and no statements; a closed one
/// <c>ClosedAt</c> and no statements. <c>WebsiteHost</c> is shown with the website.
/// </summary>
public sealed record OrganisationProfileResponse(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    string? Website,
    string? WebsiteHost,
    string? LegalCode,
    bool DeclaredLobbyist,
    OrganisationLabelResponse Label,
    string Status,
    string? BlockStatement,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<ProfileChangeResponse> History,
    IReadOnlyList<ProfileStatementResponse> Statements);

public sealed record BillStatementResponse(Guid OrganisationId, string Slug, string Name, OrganisationLabelResponse Label, StatementResponse Statement);

/// <summary>"Organizacijų pozicijos" on a bill, in the order published; <c>Open</c>: the bill takes statements.</summary>
public sealed record BillStatementsResponse(string BillNumber, bool Open, IReadOnlyList<BillStatementResponse> Items);

public sealed record MyOrganisationResponse(string Slug, string Name, string Status, OrganisationLabelResponse Label);

/// <summary>A representative as the organisation's other representatives see them.</summary>
public sealed record RepresentativeResponse(Guid Id, string? DisplayName, bool IsMe, DateTimeOffset Since);

public sealed record RepresentedStatementResponse(BillRefResponse Bill, StatementResponse Statement);

/// <summary>
/// The caller's own answer for an organisation's management (never cached). For anyone not its representative
/// <c>IsRepresentative</c> is false and the rest empty. <c>DomainConfirmation</c> is null when a code may be asked for,
/// otherwise the reason; <c>Refusal</c> why the representative cannot change or publish now.
/// </summary>
public sealed record OrganisationManagementResponse(
    bool IsRepresentative,
    IReadOnlyList<RepresentativeResponse> Representatives,
    string? DomainConfirmation,
    string? WebsiteHost,
    IReadOnlyList<RepresentedStatementResponse> Statements,
    string? Refusal);

/// <summary>One organisation the caller represents on a bill: its statement there (if any) and why it cannot publish now.</summary>
public sealed record MyBillOrganisationResponse(Guid OrganisationId, string Slug, string Name, string Status, OrganisationLabelResponse Label, StatementResponse? Statement, string? Refusal);

public sealed record OrganisationCreatedResponse(Guid Id, string Slug);

/// <summary>A representative's one-time link accepted: the organisation's address, where the page goes next.</summary>
public sealed record InviteAcceptedResponse(string Slug);

public sealed record StatementSavedResponse(Guid Id, string Status);

/// <summary>A one-time link: shown once, valid until <c>ExpiresAt</c>; <c>Path</c> is the page that accepts it.</summary>
public sealed record InviteCreatedResponse(string Token, string Path, DateTimeOffset ExpiresAt);

public sealed record InviteResponse(string Slug, string Name, bool Usable, bool AlreadyRepresentative);

/// <summary>A bill the organisation has a public statement on, with its own registration link (design 8.14.14, stage B).</summary>
/// <param name="Link">The registration link with the organisation's code and <c>tikslas</c> set to the bill's page.</param>
public sealed record OrganisationBillLinkResponse(BillRefResponse Bill, string Link);

/// <summary>
/// The organisation's invitation code as its representatives see it (design 8.14.14, stage B), never public and never cached:
/// the code and the link to the registration form (both null while none was made, as for a blocked organisation that never
/// asked), a link per bill it has a public statement on, and one number: how many completed registration with its codes,
/// never broken down. <c>Usable</c>: the code lets a registration through now; otherwise no link is offered. <c>On</c>: an
/// administrator has not switched it off (<c>Contact</c> is then the moderators' address); <c>OffWithOrganisation</c>: it is
/// off because the organisation is blocked or closed.
/// </summary>
public sealed record OrganisationInvitationResponse(
    string? Code,
    string? Link,
    int Completed,
    bool Usable,
    bool On,
    bool OffWithOrganisation,
    IReadOnlyList<OrganisationBillLinkResponse> Bills,
    string? Contact);

/// <summary>Registering an organisation or changing its profile (design 8.14.14).</summary>
public sealed record OrganisationRequest
{
    public string Name { get; init; } = "";

    public string? Description { get; init; }

    /// <summary>An https address, at most 200 characters; optional.</summary>
    public string? Website { get; init; }

    /// <summary>7 to 9 digits; optional.</summary>
    public string? LegalCode { get; init; }

    /// <summary>"Esame registruoti lobistai": the organisation's own declaration.</summary>
    public bool DeclaredLobbyist { get; init; }
}

/// <summary>An organisation's statement on a bill (design 8.14.3).</summary>
public sealed record OrganisationStatementRequest
{
    /// <summary><c>for</c>, <c>against</c> or <c>remarks</c>.</summary>
    public string Stance { get; init; } = "";

    public string Text { get; init; } = "";

    /// <summary>An https address, at most 500 characters; optional.</summary>
    public string? Link { get; init; }

    /// <summary>With <c>for</c> or <c>against</c>: 1 to 1 000 000 participants; optional.</summary>
    public int? Goal { get; init; }
}

/// <summary>An address on the organisation's domain, to send the confirmation code to; only the domain is kept.</summary>
public sealed record ConfirmationCodeRequest
{
    public string Address { get; init; } = "";
}

public sealed record ConfirmationRequest
{
    public string Code { get; init; } = "";
}

/// <summary>A representative's one-time token (design 8.14.14), sent in the body of the request (design 6.8).</summary>
public sealed record InviteTokenRequest
{
    public string Token { get; init; } = "";
}

/// <summary>A new or changed organisation in the moderators' queue (design 8.14.9).</summary>
public sealed record QueueOrganisationResponse(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    string? Website,
    string? LegalCode,
    bool DeclaredLobbyist,
    OrganisationLabelResponse Label,
    string Status,
    bool Seen,
    bool Changed,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReportResponse> Reports,
    IReadOnlyList<ProfileChangeResponse> History,
    string? ConfirmationNote,
    bool DomainBarred,
    DateTimeOffset RegisteredAt);

/// <summary>A statement for a moderator: waiting, flagged, reported or not yet seen. Never who wrote it.</summary>
public sealed record QueueStatementResponse(
    Guid Id,
    BillRefResponse Bill,
    Guid OrganisationId,
    string Slug,
    string OrganisationName,
    OrganisationLabelResponse Label,
    string Stance,
    string Text,
    string? Link,
    int? Goal,
    string Status,
    bool Seen,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReportResponse> Reports,
    DateTimeOffset PublishedAt,
    bool BillFinished = false);

public sealed record OrganisationModerationResponse(IReadOnlyList<QueueOrganisationResponse> Organisations, IReadOnlyList<QueueStatementResponse> Statements);

public sealed record HiddenOrganisationResponse(Guid Id, string Slug, string Name, DateTimeOffset? BlockedAt, string? BlockedBy, string? Statement);

public sealed record HiddenStatementResponse(
    Guid Id, BillRefResponse Bill, string OrganisationName, string Stance, string Text, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, bool Restorable);

public sealed record HiddenOrganisationsResponse(IReadOnlyList<HiddenOrganisationResponse> Organisations, IReadOnlyList<HiddenStatementResponse> Statements);
