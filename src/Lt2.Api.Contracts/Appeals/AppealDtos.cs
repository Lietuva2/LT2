namespace Lt2.Api.Contracts.Appeals;

// Design 8.8, 8.12. Codes are lower-case English words: side for | against; status open | closed | rejected (taking part
// closed by the moderators); reason status pending | published | hidden | merged; threshold set by default | moderator;
// description source initiator | moderator | ai-draft | ai (made by the summary job, read by nobody; design 8.13).
// Every active bill is open: the answers for one nobody has taken part on are the empty open state (no id, zero counts).
// No response carries another person's position or tells who took part first: the only side of a person any answer holds
// is the caller's own, in MyBillRecordResponse.

/// <summary>The limits of taking part, the same numbers the server enforces (design 8.2, <c>AGENTS.md</c>).</summary>
public static class AppealLimits
{
    public const int DescriptionMaxLength = 1500;
    public const int StatementMaxLength = 1000;
    public const int ReportTextMaxLength = 1000;

    public static readonly IReadOnlyList<string> Sides = ["for", "against"];

    public static readonly IReadOnlyList<string> ReportGrounds = ["illegal", "personal-data", "insult", "spam", "other"];

    /// <summary>A bill's description may also be reported as inaccurate ("Pranešti apie klaidą", design 8.13); an argument may not.</summary>
    public static readonly IReadOnlyList<string> DescriptionReportGrounds = ["inaccurate", .. ReportGrounds];

    /// <summary>An organisation or its statement may also be reported as "Apsimeta kita organizacija" (design 8.14.9).</summary>
    public static readonly IReadOnlyList<string> OrganisationReportGrounds = ["impersonation", .. ReportGrounds];

    /// <summary>The grounds a report on this target may give.</summary>
    public static IReadOnlyList<string> ReportGroundsFor(string targetKind) => targetKind switch
    {
        "record" => DescriptionReportGrounds,
        "organisation" or "statement" => OrganisationReportGrounds,
        _ => ReportGrounds,
    };

    /// <summary>
    /// A bill's description (<c>record</c>), an argument, an organisation or an organisation's statement (design 8.14.14), or an
    /// MP's response (design 8.5.5), or a comment (design 8.23.8).
    /// </summary>
    public static readonly IReadOnlyList<string> ReportTargets = ["record", "reason", "organisation", "statement", "response", "comment"];

    /// <summary>The longest official text's address a description may carry (<c>record.description_url</c>, design 8.13).</summary>
    public const int DescriptionUrlMaxLength = 500;

    /// <summary>The pasted official text for an AI draft (design 8.11.4): never cut, so the form says the length instead.</summary>
    public const int DraftSourceMaxLength = 20_000;

    /// <summary>A date flag's link to its source and its note (design 8.4).</summary>
    public const int FlagLinkMaxLength = 500;

    public const int FlagNoteMaxLength = 1000;
}

/// <summary>
/// The official text's address a moderator may record with a description (design 8.13, decided by the owner on
/// 2026-11-14): empty, or an <c>https</c> address whose host is <c>lrs.lt</c> or under it (<c>e-seimas.lrs.lt</c>,
/// <c>www.lrs.lt</c>), with no user name or password, at most 500 characters. One rule for the form and the server.
/// </summary>
public static class OfficialTextAddress
{
    private const string Host = "lrs.lt";

    /// <summary>What is stored: the trimmed address, or null for an empty field (which clears the address).</summary>
    public static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool IsAcceptable(string? value)
    {
        var address = Normalise(value);
        if (address is null)
        {
            return true;
        }

        if (address.Length > AppealLimits.DescriptionUrlMaxLength || address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\'))
        {
            return false;
        }

        const string Prefix = "https://";
        if (!address.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // No user name or password, not even an empty one: no "@" before the path.
        var authority = address[Prefix.Length..];
        var end = authority.IndexOfAny(['/', '?', '#']);
        if ((end < 0 ? authority : authority[..end]).Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0)
        {
            return false;
        }

        // The host itself or a name under it; "evil-lrs.lt" and "lrs.lt.evil.example" are other hosts.
        var host = uri.IdnHost;
        return host.Equals(Host, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Both sides, always together, each with the count for accounts older than 30 days (design 6.6).</summary>
public sealed record SideCountsResponse(int For, int ForOlderThan30Days, int Against, int AgainstOlderThan30Days);

/// <summary>The bill's public threshold and the progress towards it (design 8.12.2).</summary>
/// <param name="SetBy"><c>default</c> (the same for all bills) or <c>moderator</c> (this bill's own, with <c>Note</c>).</param>
public sealed record ThresholdResponse(int Value, string SetBy, string? Note, int Participants, bool Reached, bool EnteredSummaries);

public sealed record BillRefResponse(string Number, string Title);

public sealed record ReasonResponse(Guid Id, string Side, string Text, string? AuthorName, int Endorsements, string Status, DateTimeOffset CreatedAt)
{
    /// <summary>How many comments the argument shows ("Aptarimas (n)", design 8.23.6, 8.23.14); never counted anywhere else.</summary>
    public int Comments { get; init; }
}

/// <summary>
/// A bill as people take part on it (design 8.12.1). <c>Id</c> is the bill's record, null while it has none (nobody took
/// part and no moderator acted). <c>Description</c> is empty while there is none; <c>DescriptionAuthor</c> is set only for a
/// description an initiator wrote before 2026-11-14. A hidden record (<c>rejected</c>) carries only the moderators'
/// <c>HiddenStatement</c>. <c>DescriptionUrl</c> is the official text the description was made from, when known, and
/// <c>DescriptionAt</c> when its text was made or last changed (design 8.13).
/// </summary>
public sealed record BillRecordResponse(
    Guid? Id,
    BillRefResponse Bill,
    string Status,
    string Description,
    string? DescriptionSource,
    string? DescriptionAuthor,
    SideCountsResponse Counts,
    ThresholdResponse Threshold,
    IReadOnlyList<ReasonResponse> Reasons,
    SurgeResponse? Surge = null,
    long? ClosingVoteId = null,
    DateTimeOffset? ClosedAt = null,
    PlannedDateResponse? Planned = null,
    string? HiddenStatement = null,
    string? DescriptionUrl = null,
    DateTimeOffset? DescriptionAt = null);

/// <summary>The bill's planned date on the Seimas agenda (design 8.4): shown as "Planuojama …", never a countdown.</summary>
public sealed record PlannedDateResponse(DateOnly Date, string? Stage, string? SourceUrl, bool SetByModerator);

/// <summary>"The date is wrong" (<c>date-wrong</c>) or "this is being voted sooner" (<c>sooner</c>), with a link.</summary>
public sealed record FlagRequest
{
    public string Kind { get; init; } = "";

    public string Link { get; init; } = "";

    public string? Note { get; init; }
}

/// <summary>The surge note (design 6.6): in the last hours, this share of positions came from accounts made in those hours.</summary>
public sealed record SurgeResponse(int SharePercent, int WindowHours);

/// <summary>
/// A bill in <c>/projektai</c> (design 8.12.5); <c>Id</c> null while it has no record; <c>Arguments</c> its published
/// arguments.
/// </summary>
public sealed record BillListItemResponse(Guid? Id, BillRefResponse Bill, string Status, SideCountsResponse Counts, ThresholdResponse Threshold, PlannedDateResponse? Planned, int Arguments);

public sealed record BillListResponse(IReadOnlyList<BillListItemResponse> Items, int Total, string? NextCursor);

/// <summary>The caller's own part on a bill. Never cached, never shown to anyone else.</summary>
public sealed record MyBillRecordResponse(
    string? Side,
    IReadOnlyList<Guid> Endorsed,
    int? EndorsementsLeft,
    IReadOnlyList<ReasonResponse> MyReasons,
    int? ReasonsLeft,
    bool HasPositionConsent);

/// <summary>
/// The numbers the forms show (design 8.2, 8.12.2). <c>ReasonsPerPerson</c> and <c>EndorsementsPerPerson</c> are the
/// settings' current limits per person per bill; 0 means no limit. <c>TextMaxLength</c> is the setting
/// <c>Texts:MaxLength</c>, the longest text of an argument, an organisation's statement or an MP's response, in characters;
/// 0 means no limit but <see cref="MemberTextLimits.Ceiling"/>. In <see cref="MyBillRecordResponse"/> a <c>null</c>
/// "left" means no limit. <c>AiSummaries</c>: the summary job can make descriptions (design 8.13: a provider configured,
/// official texts readable, <c>Ai:SummariesPerDay</c> not 0); while false nothing promises one.
/// </summary>
public sealed record AppealRulesResponse(int ThresholdDefault, int ThresholdMinimum, int TextMaxLength, int ReasonsPerPerson, int EndorsementsPerPerson, int DescriptionMaxLength, bool AiSummaries = false);

public sealed record ReasonCreatedResponse(Guid Id, string Status);

/// <summary>
/// The caller's own writing restriction (design 8.3), never anyone else's: whether one holds, its last day (null = until
/// lifted) and the moderator's statement.
/// </summary>
public sealed record MyRestrictionResponse(bool Restricted, DateOnly? Until, string? Statement);

public sealed record PositionRequest
{
    public string Side { get; init; } = "";
}

public sealed record ReasonRequest
{
    public string Side { get; init; } = "";

    public string Text { get; init; } = "";
}

/// <summary>A report (design 8.3, Digital Services Act notice and action) on a bill's description or an argument.</summary>
public sealed record ReportRequest
{
    /// <summary><c>record</c> (the bill's description) or <c>reason</c>.</summary>
    public string TargetKind { get; init; } = "";

    public Guid TargetId { get; init; }

    /// <summary>One of <see cref="AppealLimits.ReportGrounds"/>.</summary>
    public string Ground { get; init; } = "";

    public string? Text { get; init; }
}

/// <summary>One edit of a bill's description (design 8.2: it keeps its edit history). Who edited is not shown.</summary>
public sealed record DescriptionRevisionResponse(DateTimeOffset ChangedAt, string? OldText, string? NewText);
