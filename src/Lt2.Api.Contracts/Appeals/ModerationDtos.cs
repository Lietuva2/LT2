namespace Lt2.Api.Contracts.Appeals;

public sealed record QueueReportResponse(Guid Id, string Ground, string? Text, DateTimeOffset CreatedAt);

/// <summary>An appeal waiting for a moderator, or a published one that was reported.</summary>
public sealed record QueueAppealResponse(
    Guid Id,
    BillRefResponse Bill,
    string Status,
    string? InitiatorName,
    string Description,
    ReasonResponse? FirstReason,
    int Threshold,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReportResponse> Reports,
    DateTimeOffset OpenedAt);

/// <summary>An argument for a moderator: flagged by pre-screening, reported, or not yet seen.</summary>
public sealed record QueueReasonResponse(
    Guid Id,
    Guid AppealId,
    BillRefResponse Bill,
    string Side,
    string Text,
    string? AuthorName,
    string Status,
    bool Seen,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReportResponse> Reports,
    IReadOnlyList<ReasonResponse> MergeCandidates,
    DateTimeOffset CreatedAt);

/// <summary>The moderators' queue (design 8.3). Holds no position of anyone.</summary>
public sealed record ModerationQueueResponse(IReadOnlyList<QueueAppealResponse> Appeals, IReadOnlyList<QueueReasonResponse> Reasons);

/// <summary>Publishing: the statement to the author is optional.</summary>
public sealed record ModerationRequest
{
    public string? Statement { get; init; }
}

/// <summary>Returning or hiding: the statement of reasons sent to the author is required (design 8.3).</summary>
public sealed record StatementRequest
{
    public string Statement { get; init; } = "";
}

public sealed record ModeratorThresholdRequest
{
    public int Threshold { get; init; }

    /// <summary>Shown on the appeal page with the new number (design 8.4).</summary>
    public string Statement { get; init; } = "";
}

public sealed record MergeReasonRequest
{
    public Guid IntoReasonId { get; init; }

    public string Statement { get; init; } = "";
}
