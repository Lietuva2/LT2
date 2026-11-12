namespace Lt2.Api.Contracts.Appeals;

// Design 8.8. Codes are lower-case English words: side for | against; appeal status pending | returned | open | closed |
// rejected; reason status pending | published | hidden | merged; threshold set by initiator | moderator.
// No response carries another person's position: the only side of a person any answer holds is the caller's own, in
// MyAppealResponse.

/// <summary>The limits of appeals, the same numbers the server enforces (design 8.2, <c>AGENTS.md</c>).</summary>
public static class AppealLimits
{
    public const int ReasonMaxLength = 500;
    public const int ReasonsPerPerson = 2;
    public const int EndorsementsPerPerson = 3;
    public const int DescriptionMaxLength = 1500;
    public const int StatementMaxLength = 1000;
    public const int ReportTextMaxLength = 1000;

    public static readonly IReadOnlyList<string> Sides = ["for", "against"];

    public static readonly IReadOnlyList<string> ReportGrounds = ["illegal", "personal-data", "insult", "spam", "other"];

    public static readonly IReadOnlyList<string> ReportTargets = ["appeal", "reason"];
}

/// <summary>Both sides, always together, each with the count for accounts older than 30 days (design 6.6).</summary>
public sealed record SideCountsResponse(int For, int ForOlderThan30Days, int Against, int AgainstOlderThan30Days);

/// <summary>The appeal's public threshold and the progress towards it (design 8.4).</summary>
public sealed record ThresholdResponse(int Value, string SetBy, string? Note, int Participants, bool Reached, bool EnteredSummaries);

public sealed record BillRefResponse(string Number, string Title);

public sealed record ReasonResponse(Guid Id, string Side, string Text, string? AuthorName, int Endorsements, string Status, DateTimeOffset CreatedAt);

public sealed record AppealResponse(
    Guid Id,
    BillRefResponse Bill,
    string Status,
    string? InitiatorName,
    string Description,
    DateTimeOffset OpenedAt,
    DateTimeOffset? PublishedAt,
    SideCountsResponse Counts,
    ThresholdResponse Threshold,
    IReadOnlyList<ReasonResponse> Reasons,
    string? ReturnNote,
    SurgeResponse? Surge = null,
    long? ClosingVoteId = null,
    DateTimeOffset? ClosedAt = null,
    PlannedDateResponse? Planned = null);

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

public sealed record AppealListItemResponse(Guid Id, BillRefResponse Bill, SideCountsResponse Counts, ThresholdResponse Threshold, DateTimeOffset? PublishedAt, PlannedDateResponse? Planned = null);

public sealed record AppealListResponse(IReadOnlyList<AppealListItemResponse> Items, int Total, string? NextCursor);

/// <summary>The caller's own part in an appeal. Never cached, never shown to anyone else.</summary>
public sealed record MyAppealResponse(
    string? Side,
    IReadOnlyList<Guid> Endorsed,
    int EndorsementsLeft,
    IReadOnlyList<ReasonResponse> MyReasons,
    int ReasonsLeft,
    bool IsInitiator,
    bool MayChangeThreshold,
    bool HasPositionConsent);

public sealed record AppealRulesResponse(int ThresholdDefault, int ThresholdMinimum, int ReasonMaxLength, int ReasonsPerPerson, int EndorsementsPerPerson, int DescriptionMaxLength);

public sealed record ReasonCreatedResponse(Guid Id, string Status);

/// <summary>Opening the appeal on a bill (design 8.1): a side, the first argument, a neutral description, a threshold.</summary>
public sealed record OpenAppealRequest
{
    public string Side { get; init; } = "";

    public string FirstReason { get; init; } = "";

    public string Description { get; init; } = "";

    public int Threshold { get; init; }
}

/// <summary>A returned appeal, corrected by its initiator and sent to the moderators again.</summary>
public sealed record EditAppealRequest
{
    public string Description { get; init; } = "";

    public string FirstReason { get; init; } = "";
}

public sealed record ThresholdRequest
{
    public int Threshold { get; init; }
}

public sealed record PositionRequest
{
    public string Side { get; init; } = "";
}

public sealed record ReasonRequest
{
    public string Side { get; init; } = "";

    public string Text { get; init; } = "";
}

/// <summary>A report (design 8.3, Digital Services Act notice and action) on an appeal or an argument.</summary>
public sealed record ReportRequest
{
    /// <summary><c>appeal</c> or <c>reason</c>.</summary>
    public string TargetKind { get; init; } = "";

    public Guid TargetId { get; init; }

    /// <summary>One of <see cref="AppealLimits.ReportGrounds"/>.</summary>
    public string Ground { get; init; } = "";

    public string? Text { get; init; }
}
