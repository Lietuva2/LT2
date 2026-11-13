namespace Lt2.Api.Contracts.Appeals;

public sealed record QueueReportResponse(Guid Id, string Ground, string? Text, DateTimeOffset CreatedAt);

/// <summary>An appeal waiting for a moderator, or a published one that was reported.</summary>
public sealed record QueueAppealResponse(
    Guid Id,
    BillRefResponse Bill,
    string Status,
    string? InitiatorName,
    string Description,
    int Threshold,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReportResponse> Reports,
    DateTimeOffset OpenedAt,
    SurgeResponse? Surge = null,
    PlannedDateResponse? Planned = null,
    IReadOnlyList<QueueFlagResponse>? DateFlags = null,
    bool UnderThresholdOnAgenda = false,
    bool Held = false,
    int ProposalsOnBill = 0,
    int Participants = 0,
    int EffectiveThreshold = 0);

public sealed record QueueFlagResponse(Guid Id, string Kind, string Link, string? Note, DateTimeOffset CreatedAt);

/// <summary>A moderator's planned date or stage for the appeal's bill (design 8.4), with the reason.</summary>
public sealed record ModeratorPlannedRequest
{
    public DateOnly Date { get; init; }

    /// <summary><c>presentation</c>, <c>consideration</c>, <c>adoption</c>, or null.</summary>
    public string? Stage { get; init; }

    public string Statement { get; init; } = "";
}

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

/// <summary>
/// A hidden appeal (design 8.3): who hid it, when and why. <c>RestoresAs</c> is the status "Atkurti" gives it (open,
/// closed or pending); <c>RefusedBecause</c> the code a restore would be refused with now, or null.
/// </summary>
public sealed record HiddenAppealResponse(
    Guid Id, BillRefResponse Bill, string? InitiatorName, string Description, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement,
    string RestoresAs, string? RefusedBecause);

/// <summary>A hidden reason; <c>Restorable</c> while its appeal is open.</summary>
public sealed record HiddenReasonResponse(
    Guid Id, BillRefResponse Bill, string Side, string Text, string? AuthorName, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, bool Restorable);

/// <summary>The moderators' list of hidden appeals and reasons (design 8.3), newest hide first. Holds no position.</summary>
public sealed record HiddenItemsResponse(IReadOnlyList<HiddenAppealResponse> Appeals, IReadOnlyList<HiddenReasonResponse> Reasons);

/// <summary>A proposed description change (design 8.1): the approved text and the proposed one, side by side.</summary>
public sealed record QueueDescriptionChangeResponse(
    Guid Id, Guid AppealId, BillRefResponse Bill, string? InitiatorName, string CurrentText, string ProposedText, IReadOnlyList<string> Flags, DateTimeOffset CreatedAt);

/// <summary>The moderators' queue (design 8.3). Holds no position of anyone.</summary>
public sealed record ModerationQueueResponse(
    IReadOnlyList<QueueAppealResponse> Appeals,
    IReadOnlyList<QueueReasonResponse> Reasons,
    IReadOnlyList<QueueDescriptionChangeResponse>? DescriptionChanges = null);

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

/// <summary>A moderator's correction of an open appeal's description (design 8.1); the statement goes to the initiator.</summary>
public sealed record ModeratorDescriptionRequest
{
    public string Description { get; init; } = "";

    public string Statement { get; init; } = "";
}

/// <summary>
/// The state a moderator's controls on the appeal page need (design 8.3): which actions apply. Holds no position and no
/// person.
/// </summary>
public sealed record ModerationAppealResponse(
    Guid Id, string Status, string Description, int Threshold, bool Held, PlannedDateResponse? Planned, bool HasOpenItems);

public sealed record MergeReasonRequest
{
    public Guid IntoReasonId { get; init; }

    public string Statement { get; init; } = "";
}

/// <summary>
/// Restricting an account from writing (design 8.3): until a day (inclusive, Vilnius time), or with no day until lifted.
/// The statement is required and sent to the person.
/// </summary>
public sealed record RestrictionRequest
{
    public DateOnly? Until { get; init; }

    public string Statement { get; init; } = "";
}

public sealed record RestrictionCreatedResponse(Guid Id);

/// <summary>An argument on the author's screen for a moderator. Its side is the argument's, public anyway; never the person's.</summary>
public sealed record AuthorReasonResponse(Guid Id, string BillNumber, string Side, string Text, string Status, DateTimeOffset CreatedAt);

public sealed record AuthorAppealResponse(Guid Id, string BillNumber, string Status, DateTimeOffset OpenedAt);

/// <summary>An earlier moderation action on the person's arguments, appeals or restrictions.</summary>
public sealed record ModerationEntryResponse(string TargetKind, string Action, string? Statement, DateTimeOffset At);

/// <summary>A writing restriction for moderators; <c>Holds</c> is whether it applies now; <c>Until</c> null = until lifted.</summary>
public sealed record RestrictionResponse(Guid Id, string? DisplayName, DateOnly? Until, string Statement, DateTimeOffset SetAt, bool Holds, DateTimeOffset? LiftedAt, string? LiftStatement);

/// <summary>
/// What a moderator sees before restricting a person (design 8.3): the display name, their arguments and appeals, earlier
/// actions and restrictions. No address, no side of the person, no mark, no account id.
/// </summary>
public sealed record AuthorViewResponse(
    string? DisplayName,
    IReadOnlyList<AuthorReasonResponse> Reasons,
    IReadOnlyList<AuthorAppealResponse> Appeals,
    IReadOnlyList<ModerationEntryResponse> Actions,
    IReadOnlyList<RestrictionResponse> Restrictions);
