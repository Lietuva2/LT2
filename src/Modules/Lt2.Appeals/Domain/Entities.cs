namespace Lt2.Appeals.Domain;

/// <summary>
/// <c>appeals.bill_record</c>: the bill's record, the Appeals module's own record of what people did on one Seimas bill (design
/// 5.3, 8.12.1). One per bill (open, closed or hidden), created when first needed: the first side, argument or description,
/// or a moderator's action on the bill. Nobody proposes, opens or approves it, and nothing public says who was first.
/// </summary>
internal sealed class BillRecord
{
    public Guid Id { get; set; }

    /// <summary>The Seimas bill number, the natural key of a bill (design 3). One record per bill (design 8.12.1).</summary>
    public string BillNumber { get; set; } = "";

    /// <summary>
    /// Only on records from before 2026-11-14: the member whose proposal was published. Shown beside the description they
    /// wrote ("Aprašymą parašė …") while it is theirs, and gives them no rights (design 8.12.4). Null on every new record.
    /// </summary>
    public Guid? InitiatorUserId { get; set; }

    /// <summary>What the bill is about (design 8.13); empty while no description has been made.</summary>
    public string Description { get; set; } = "";

    /// <summary>Who made the description: see <see cref="DescriptionSources"/>; null while there is none.</summary>
    public string? DescriptionSource { get; set; }

    /// <summary>
    /// The address of the official text the description was made from, when known (design 8.13): set by the summary job,
    /// kept while a moderator confirms or corrects that text, cleared when it is removed. Null for a moderator's own text.
    /// </summary>
    public string? DescriptionUrl { get; set; }

    /// <summary>When the description's text was last made or changed (design 8.13: "the day it was made"); null without one.</summary>
    public DateTimeOffset? DescriptionAt { get; set; }

    /// <summary>See <see cref="BillRecordStatuses"/>.</summary>
    public string Status { get; set; } = BillRecordStatuses.Open;

    /// <summary>When the record was created.</summary>
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>
    /// Since when people could take part: the creation time on a new record, the publication time on one from before
    /// 2026-11-14. The daily aggregates start from it.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>The final Seimas vote that closed it.</summary>
    public long? ClosingVoteId { get; set; }

    /// <summary>The counts frozen when the record closed (design 8.1); null while open.</summary>
    public int? ClosedFor { get; set; }

    public int? ClosedForOlderThan30Days { get; set; }

    public int? ClosedAgainst { get; set; }

    public int? ClosedAgainstOlderThan30Days { get; set; }

    /// <summary><c>automatic</c> or <c>held</c> (design 8.4; used by the summaries).</summary>
    public string Sending { get; set; } = "automatic";

    /// <summary>A moderator's own number for this bill (design 8.12.2); null = the default threshold of all bills.</summary>
    public int? Threshold { get; set; }

    /// <summary>See <see cref="ThresholdSetters"/>.</summary>
    public string ThresholdSetBy { get; set; } = ThresholdSetters.Default;

    /// <summary>A moderator's public statement for setting the bill's own threshold, shown on the bill's page (design 8.4).</summary>
    public string? ThresholdNote { get; set; }

    /// <summary>When the bill first entered a summary really sent to MPs; from then on it stays eligible (design 8.12.2).</summary>
    public DateTimeOffset? FirstSummarisedAt { get; set; }

    /// <summary>
    /// The bill's last activity (design 8.12.5, owner 2026-11-17: "Naujausia veikla"): the latest side taken, changed or
    /// withdrawn, argument published, comment written, organisation's statement made public or changed, or MP's response made
    /// public or changed. Set by <see cref="Application.RecordActivity"/> in the same transaction as each; one time per bill,
    /// nothing per person. Null while there was none.
    /// </summary>
    public DateTimeOffset? LastActivityAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

internal static class BillRecordStatuses
{
    /// <summary>People can take a side, write and mark arguments.</summary>
    public const string Open = "open";

    /// <summary>The Seimas took the final vote: the counts are frozen (design 8.1).</summary>
    public const string Closed = "closed";

    /// <summary>
    /// Hidden by a moderator: taking part on the bill is closed; sides and arguments are kept unseen and come back with
    /// "Atkurti" (design 8.12.3).
    /// </summary>
    public const string Rejected = "rejected";

    /// <summary>
    /// Only rows from before 2026-11-14 that are no longer a bill's record: proposals that were waiting, returned or closed
    /// when the proposals went (design 8.12.4), and older hidden appeals on a bill that has another record. Shown to nobody.
    /// </summary>
    public const string Superseded = "superseded";
}

/// <summary>Who set the threshold that applies to a bill (design 8.12.2).</summary>
internal static class ThresholdSetters
{
    /// <summary>The default threshold of all bills, <c>Digest:ThresholdDefault</c>, read when it is used.</summary>
    public const string Default = "default";

    public const string Moderator = "moderator";
}

/// <summary>
/// Who made a bill's description (design 5.3, 8.13): an initiator before 2026-11-14 (approved by a moderator), a moderator,
/// a moderator starting from AI text, or the summary job alone. An <see cref="AiDraft"/> description keeps its label
/// "Sukurta DI · taisė žmonės" when a moderator corrects it later; an <see cref="Ai"/> one becomes <see cref="AiDraft"/>
/// once a moderator confirms or corrects it.
/// </summary>
internal static class DescriptionSources
{
    public const string Initiator = "initiator";
    public const string Moderator = "moderator";
    public const string AiDraft = "ai-draft";

    /// <summary>Made by the summary job from official text; no person has read it ("Sukurta DI · žmonių neperžiūrėta").</summary>
    public const string Ai = "ai";
}

/// <summary>
/// <c>appeals.summary_attempt</c>: the summary job's last try on a bill (design 8.13), one row per bill: when, how it
/// ended, and when the bill may be tried again. Never a text. A bill gets a row even while it has no record.
/// </summary>
internal sealed class SummaryAttempt
{
    public string BillNumber { get; set; } = "";

    public DateTimeOffset TriedAt { get; set; }

    /// <summary>See <see cref="SummaryOutcomes"/>.</summary>
    public string Outcome { get; set; } = "";

    /// <summary>Whether the provider was asked: only those tries count towards <c>Ai:SummariesPerDay</c>.</summary>
    public bool AskedProvider { get; set; }

    public DateTimeOffset RetryAfter { get; set; }
}

/// <summary>How a summary job's try on a bill ended (design 8.13).</summary>
internal static class SummaryOutcomes
{
    /// <summary>A summary was stored as the bill's description.</summary>
    public const string Made = "made";

    /// <summary>The official text could not be read (or was longer than the provider is given).</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The provider said the text is not enough to tell what the bill changes; nothing was stored.</summary>
    public const string Insufficient = "insufficient";

    /// <summary>The provider refused, cut off or failed; nothing was stored.</summary>
    public const string Failed = "failed";

    /// <summary>A person wrote or removed the description meanwhile; nothing was stored.</summary>
    public const string Overtaken = "overtaken";
}
/// <summary>
/// <c>appeals.ai_draft_request</c>: that a moderator asked for an AI draft of a description (design 8.11.4): who, which
/// bill, when; never the texts. For the hourly limit and for accepting a description as started from a draft.
/// </summary>
internal sealed class AiDraftRequest
{
    public Guid Id { get; set; }

    public Guid ModeratorUserId { get; set; }

    public string BillNumber { get; set; } = "";

    public DateTimeOffset RequestedAt { get; set; }
}

/// <summary>
/// <c>appeals.position</c>: one person's side on one bill (design 5.3). One row per person per record; changing the side
/// overwrites it, and no history of earlier sides is kept anywhere.
/// </summary>
internal sealed class Position
{
    public Guid RecordId { get; set; }

    public Guid UserId { get; set; }

    /// <summary><c>for</c> or <c>against</c>, see <see cref="Sides"/>.</summary>
    public string Side { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>
    /// When the person's registration was completed, copied from Identity when the position is taken, so the count for
    /// accounts older than 30 days (design 6.6) is read from this table alone.
    /// </summary>
    public DateTimeOffset MemberSince { get; set; }
}

/// <summary><c>appeals.reason</c>: a public argument for one side ("argumentas", design 5.3).</summary>
internal sealed class Reason
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    public string Side { get; set; } = "";

    public Guid AuthorUserId { get; set; }

    public string Text { get; set; } = "";

    /// <summary>See <see cref="ReasonStatuses"/>.</summary>
    public string Status { get; set; } = ReasonStatuses.Published;

    public Guid? MergedIntoId { get; set; }

    /// <summary>When a moderator last looked at it; only seen arguments may appear in a summary (design 8.3).</summary>
    public DateTimeOffset? SeenAt { get; set; }

    public string[] ScreeningFlags { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When it first became public (design 8.25.5: written unflagged, or published by a moderator); null while it never was.
    /// A restore keeps it.
    /// </summary>
    public DateTimeOffset? FirstPublicAt { get; set; }

    /// <summary>
    /// When its author last edited it (design 8.2, owner 2026-11-17: only until someone reacts); null while never. No earlier
    /// text is kept. An edit is new to readers again (design 8.11.8).
    /// </summary>
    public DateTimeOffset? EditedAt { get; set; }
}

internal static class ReasonStatuses
{
    /// <summary>Flagged by pre-screening: waits for a moderator, visible to its author.</summary>
    public const string Pending = "pending";

    public const string Published = "published";
    public const string Hidden = "hidden";
    public const string Merged = "merged";
}

/// <summary><c>appeals.endorsement</c>: "Man svarbu" on an argument of the person's own side (design 5.3).</summary>
internal sealed class Endorsement
{
    public Guid ReasonId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The record of the argument, so the three-per-bill limit is one indexed count.</summary>
    public Guid RecordId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>appeals.report</c>: notice and action under the Digital Services Act (design 5.3, 8.3).</summary>
internal sealed class Report
{
    public Guid Id { get; set; }

    /// <summary><c>record</c>, <c>reason</c>, <c>organisation</c> or <c>statement</c> (design 8.14.14).</summary>
    public string TargetKind { get; set; } = "";

    public Guid TargetId { get; set; }

    /// <summary>The bill's record the target belongs to; null for an organisation or its statement (design 8.14.14).</summary>
    public Guid? RecordId { get; set; }

    public Guid ReporterUserId { get; set; }

    /// <summary>One of <see cref="ReportGrounds"/>.</summary>
    public string Ground { get; set; } = "";

    public string? Text { get; set; }

    /// <summary><c>open</c> until a moderator acts on the target, then <c>closed</c>.</summary>
    public string Status { get; set; } = "open";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>
/// <c>appeals.writing_restriction</c>: a moderator's restriction on an account's writing (design 8.3). It lives with the
/// writing rules it changes. <see cref="EndsAt"/> null means until lifted. Deleted with the account; no address is kept.
/// </summary>
internal sealed class WritingRestriction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The first moment the person may write again; null = until lifted.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    public string Statement { get; set; } = "";

    public Guid SetByUserId { get; set; }

    public DateTimeOffset SetAt { get; set; }

    public DateTimeOffset? LiftedAt { get; set; }

    public Guid? LiftedByUserId { get; set; }

    public string? LiftStatement { get; set; }
}

/// <summary><c>appeals.moderation_action</c>: what a moderator did, with the statement sent to the author (design 5.3).</summary>
internal sealed class ModerationAction
{
    public Guid Id { get; set; }

    public Guid ModeratorUserId { get; set; }

    public string TargetKind { get; set; } = "";

    public Guid TargetId { get; set; }

    public string Action { get; set; } = "";

    public string? Statement { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// <c>appeals.revision</c>: edit history of descriptions, arguments and thresholds (design 5.3). Never used for
/// positions: a changed side leaves no trace.
/// </summary>
internal sealed class Revision
{
    public Guid Id { get; set; }

    public string EntityKind { get; set; } = "";

    public Guid EntityId { get; set; }

    public string Field { get; set; } = "";

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public Guid? ChangedByUserId { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// <c>appeals.daily_stat</c>: the daily aggregate counts per record (design 5.3), for the trend chart and surge
/// detection. Filled by the daily aggregation job. The only source of trends: no per-person history is stored.
/// </summary>
internal sealed class DailyStat
{
    public Guid RecordId { get; set; }

    public DateOnly Day { get; set; }

    public int ForCount { get; set; }

    public int AgainstCount { get; set; }

    public int ForOlderThan30Days { get; set; }

    public int AgainstOlderThan30Days { get; set; }

    /// <summary>Share of that day's new positions taken by accounts younger than 30 days.</summary>
    public decimal NewAccountShare { get; set; }

    /// <summary>When the row was computed: a row computed after its day ended is final (<see cref="DailyStatRules"/>).</summary>
    public DateTimeOffset? ComputedAt { get; set; }
}

/// <summary>
/// <c>appeals.flag</c>: a participant says the planned date is wrong or the bill is being voted sooner, with a link to
/// the source (design 8.4). A moderator corrects the date or keeps it; the flag then closes.
/// </summary>
internal sealed class Flag
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    /// <summary><c>date-wrong</c> or <c>sooner</c>.</summary>
    public string Kind { get; set; } = "";

    public string Link { get; set; } = "";

    public string? Note { get; set; }

    /// <summary>The date the participant says is right, when they know it ("Tikroji data", owner, 2026-11-17); null if not given.</summary>
    public DateOnly? ProposedDate { get; set; }

    public Guid ReporterUserId { get; set; }

    public string Status { get; set; } = "open";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
}

internal static class FlagKinds
{
    public const string DateWrong = "date-wrong";
    public const string Sooner = "sooner";

    public static readonly IReadOnlyList<string> All = [DateWrong, Sooner];

    /// <summary>A flag carries a link to where the right date can be seen: http or https, at most 500 characters.</summary>
    public static bool IsLink(string? link) =>
        link is { Length: > 0 and <= 500 } && Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}