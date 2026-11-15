namespace Lt2.Api.Contracts.Appeals;

public sealed record QueueReportResponse(Guid Id, string Ground, string? Text, DateTimeOffset CreatedAt);

/// <summary>
/// A bill's record in the moderators' queue (design 8.12.3): reported, flagged about its date, surging, held, or under its
/// threshold on the agenda with at least one participant. <c>Threshold</c> is the one that applies. A report on the record is
/// about its description; <c>DescriptionSource</c> (<c>ai</c> for an AI summary nobody has read) decides its actions, and
/// <c>DescriptionUrl</c> is the address the description form starts from (design 8.13).
/// </summary>
public sealed record QueueRecordResponse(
    Guid Id,
    BillRefResponse Bill,
    string Description,
    string? DescriptionSource,
    int Threshold,
    IReadOnlyList<QueueReportResponse> Reports,
    SurgeResponse? Surge,
    PlannedDateResponse? Planned,
    IReadOnlyList<QueueFlagResponse> DateFlags,
    bool UnderThresholdOnAgenda,
    bool Held,
    int Participants,
    string? DescriptionUrl);

public sealed record QueueFlagResponse(Guid Id, string Kind, string Link, string? Note, DateTimeOffset CreatedAt);

/// <summary>A moderator's planned date or stage for the record's bill (design 8.4), with the reason.</summary>
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
    Guid RecordId,
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
/// A hidden bill record (design 8.12.3): who hid it, when and why. <c>RestoresAs</c> is the status "Atkurti" gives it (open
/// or closed).
/// </summary>
public sealed record HiddenRecordResponse(
    Guid Id, BillRefResponse Bill, string Description, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, string RestoresAs);

/// <summary>A hidden reason; <c>Restorable</c> while its record is open.</summary>
public sealed record HiddenReasonResponse(
    Guid Id, BillRefResponse Bill, string Side, string Text, string? AuthorName, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, bool Restorable);

/// <summary>The moderators' list of hidden records and reasons (design 8.3), newest hide first. Holds no position.</summary>
public sealed record HiddenItemsResponse(IReadOnlyList<HiddenRecordResponse> Records, IReadOnlyList<HiddenReasonResponse> Reasons);

/// <summary>The moderators' queue (design 8.12.3). Holds no position of anyone.</summary>
public sealed record ModerationQueueResponse(IReadOnlyList<QueueRecordResponse> Records, IReadOnlyList<QueueReasonResponse> Reasons);

/// <summary>Publishing an argument or keeping something as it is: the statement is optional.</summary>
public sealed record ModerationRequest
{
    public string? Statement { get; init; }
}

/// <summary>Hiding, restoring and the other actions: the statement of reasons is required (design 8.3).</summary>
public sealed record StatementRequest
{
    public string Statement { get; init; } = "";
}

public sealed record ModeratorThresholdRequest
{
    public int Threshold { get; init; }

    /// <summary>Shown on the bill's page with its own number (design 8.12.2).</summary>
    public string Statement { get; init; } = "";
}

/// <summary>
/// A moderator writes or corrects a bill's description (design 8.12.3, 8.13), on any active bill. The statement is
/// required and audited. <c>FromDraft</c>: the text started from an AI draft asked for on this bill (8.11.4); the page then
/// says "Sukurta DI · taisė žmonės". <c>DescriptionUrl</c> is the official text's address ("Oficialaus teksto adresas",
/// decided by the owner on 2026-11-14): it becomes the record's address, and empty or absent clears it; the form fills it in
/// advance with the address the record has, so leaving it alone keeps it (<see cref="OfficialTextAddress"/>).
/// </summary>
public sealed record ModeratorDescriptionRequest
{
    public string Description { get; init; } = "";

    public string Statement { get; init; } = "";

    public bool FromDraft { get; init; }

    public string? DescriptionUrl { get; init; }
}

/// <summary>
/// The state a moderator's controls on the bill's page need (design 8.12.3): which actions apply, and the bill's waiting
/// and published arguments as the queue shows them (arguments are moderated on the bill's page too, decided 2026-11-14).
/// <c>Id</c> is the bill's record, null while it has none (an action creates it). <c>DescriptionUrl</c> is the address the
/// description form starts from; <c>DescriptionReports</c> the open reports about the description, as the queue shows them
/// (decided by the owner on 2026-11-14). Moderators only; holds no position and nothing about who reported.
/// </summary>
public sealed record ModerationBillResponse(
    Guid? Id, string Number, string Status, string Description, string? DescriptionSource, int Threshold, string ThresholdSetBy, bool Held,
    PlannedDateResponse? Planned, bool HasOpenItems, IReadOnlyList<QueueReasonResponse> Reasons,
    string? DescriptionUrl, IReadOnlyList<QueueReportResponse> DescriptionReports);

/// <summary>The bill's record a moderator acts on (design 8.12.1: found, or created on an active bill).</summary>
public sealed record BillRecordIdResponse(Guid Id);

/// <summary>Whether an AI draft can be asked for (design 8.11.4): false until a provider is configured.</summary>
public sealed record AiStatusResponse(bool DraftAvailable);

/// <summary>The official text a moderator pasted (the relevant part of the explanatory note), at most 20 000 characters.</summary>
public sealed record DescriptionDraftRequest
{
    public string SourceText { get; init; } = "";
}

public sealed record DescriptionDraftResponse(string Draft);

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

/// <summary>An earlier moderation action on the person's arguments or restrictions.</summary>
public sealed record ModerationEntryResponse(string TargetKind, string Action, string? Statement, DateTimeOffset At);

/// <summary>A writing restriction for moderators; <c>Holds</c> is whether it applies now; <c>Until</c> null = until lifted.</summary>
public sealed record RestrictionResponse(Guid Id, string? DisplayName, DateOnly? Until, string Statement, DateTimeOffset SetAt, bool Holds, DateTimeOffset? LiftedAt, string? LiftStatement);

/// <summary>
/// What a moderator sees before restricting a person (design 8.3): the display name, their arguments, earlier actions and
/// restrictions. No address, no side of the person, no mark, no account id.
/// </summary>
public sealed record AuthorViewResponse(
    string? DisplayName,
    IReadOnlyList<AuthorReasonResponse> Reasons,
    IReadOnlyList<ModerationEntryResponse> Actions,
    IReadOnlyList<RestrictionResponse> Restrictions);
