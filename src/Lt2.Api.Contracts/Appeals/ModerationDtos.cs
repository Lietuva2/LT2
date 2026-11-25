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
    string? DescriptionUrl,
    string Status = BillRecordStatuses.Open,
    string? PlainTitle = null);

/// <summary>A participant's flag about the planned date (design 8.4), with the date they say is right when they gave one.</summary>
public sealed record QueueFlagResponse(Guid Id, string Kind, string Link, string? Note, DateTimeOffset CreatedAt, DateOnly? Date = null);

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
    DateTimeOffset CreatedAt,
    string RecordStatus = BillRecordStatuses.Open)
{
    /// <summary>How many comments go with the argument when it is merged (design 8.23.7).</summary>
    public int Comments { get; init; }
}

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

/// <summary>
/// The moderators' queue (design 8.12.3): at most a hundred records and a hundred arguments, oldest first, with how many
/// wait in all (owner, 2026-11-18). Holds no position of anyone.
/// </summary>
public sealed record ModerationQueueResponse(
    IReadOnlyList<QueueRecordResponse> Records, IReadOnlyList<QueueReasonResponse> Reasons, int RecordsTotal = 0, int ReasonsTotal = 0);

/// <summary>
/// A bill on "Projektai be aprašymo" (design 8.3): no description, or an AI summary nobody has read
/// (<c>DescriptionSource</c> <c>ai</c>; null without a description), with the numbers its page shows. <c>Bill.PlainTitle</c> is
/// the readable title of such a summary, if it has one.
/// </summary>
public sealed record UndescribedBillResponse(
    BillRefResponse Bill, string? DescriptionSource, int Participants, int Arguments, int Followers, PlannedDateResponse? Planned);

/// <summary>
/// "Projektai be aprašymo" (design 8.3, owner 2026-11-25): active bills people pay attention to that still want a
/// description, the most attention first; at most a hundred, with how many there are in all. Holds nothing about who.
/// </summary>
public sealed record UndescribedBillsResponse(IReadOnlyList<UndescribedBillResponse> Bills, int Total);

/// <summary>
/// How many items each list of the moderators' tabs holds (design 8.3, owner 2026-11-25): numbers only, each the whole
/// total, not the hundred a list shows. The work lists: the bills' records and the arguments of the queue, new and changed
/// organisations, organisations' statements, MPs' responses, reported profiles, reported comments. The reference lists:
/// "Projektai be aprašymo", every hidden item (records, arguments, blocked organisations, hidden statements, organisations
/// kept out of summaries, responses, "Apie mane" texts, comments) and the writing restrictions.
/// </summary>
public sealed record ModerationCountsResponse(
    int Records, int Reasons, int Organisations, int Statements, int Responses, int Profiles, int Comments,
    int Undescribed, int Hidden, int Restrictions);

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

/// <summary>
/// A moderator closes an open record because the Seimas rejected the bill (<c>Reason</c> <c>rejected</c>) or its initiators
/// withdrew it (<c>withdrawn</c>) (design 8.1 step 6, owner 2026-11-19): the day of that decision (not in the future), the source
/// that says so (https on lrs.lt or a host under it, at most 500 characters, the rule of <see cref="OfficialTextAddress"/>) and
/// the statement, all required.
/// </summary>
public sealed record CloseRecordRequest
{
    public string Reason { get; init; } = "";

    public DateOnly DecidedOn { get; init; }

    public string SourceUrl { get; init; } = "";

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

    /// <summary>"Trumpas pavadinimas" (design 8.13.1): one line of at most 90 characters; empty or absent leaves the record without one.</summary>
    public string? PlainTitle { get; init; }

    /// <summary>
    /// An AI agent filled in and sent the form in the moderator's session (design 8.13, owner 2026-11-25): the
    /// description is then <c>ai</c>, "Sukurta DI · žmonių neperžiūrėta", whatever it was before, until a person confirms
    /// or corrects it. The form's box for it is hidden from sight, for agents only.
    /// </summary>
    public bool WrittenByAi { get; init; }
}

/// <summary>
/// The state a moderator's controls on the bill's page need (design 8.12.3): which actions apply, and the bill's waiting
/// and published arguments as the queue shows them (arguments are moderated on the bill's page too, decided 2026-11-14).
/// <c>Id</c> is the bill's record, null while it has none (an action creates it). <c>DescriptionUrl</c> is the address the
/// description form starts from; <c>DescriptionReports</c> the open reports about the description, as the queue shows them
/// (decided by the owner on 2026-11-14). <c>ClosedReason</c> says why a closed record closed (<c>vote</c>, <c>rejected</c>,
/// <c>withdrawn</c>): only a moderator's close can be undone (design 8.1 step 6). Moderators only; holds no position and nothing
/// about who reported.
/// </summary>
public sealed record ModerationBillResponse(
    Guid? Id, string Number, string Status, string Description, string? DescriptionSource, int Threshold, string ThresholdSetBy, bool Held,
    PlannedDateResponse? Planned, bool HasOpenItems, IReadOnlyList<QueueReasonResponse> Reasons,
    string? DescriptionUrl, IReadOnlyList<QueueReportResponse> DescriptionReports, string? ClosedReason = null,
    string? PlainTitle = null);

/// <summary>The bill's record a moderator acts on (design 8.12.1: found, or created on an active bill).</summary>
public sealed record BillRecordIdResponse(Guid Id);

/// <summary>
/// Whether an AI draft can be asked for (design 8.11.4): false until a provider is configured. <c>ReadsOfficialTexts</c>:
/// a reader of the bills' official documents exists, so the draft may be asked for without pasted text.
/// </summary>
public sealed record AiStatusResponse(bool DraftAvailable, bool ReadsOfficialTexts = false);

/// <summary>
/// The official text a moderator pasted (the relevant part of the explanatory note), at most 20 000 characters; empty asks
/// for a draft from the bill's documents as read, where a reader exists.
/// </summary>
public sealed record DescriptionDraftRequest
{
    public string SourceText { get; init; } = "";
}

/// <summary>
/// The AI's draft (design 8.11.4, 8.13, 8.13.1): the description and, when the answer had them, the readable title, the
/// public statement and the address of the read document it rests on (none for pasted text).
/// </summary>
public sealed record DescriptionDraftResponse(string Draft, string? PlainTitle = null, string? Statement = null, string? DescriptionUrl = null);

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
