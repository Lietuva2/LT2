namespace Lt2.Appeals.Domain;

/// <summary><c>appeals.appeal</c>: the one object per bill where people take sides (design 5.3, 8.1).</summary>
internal sealed class Appeal
{
    public Guid Id { get; set; }

    /// <summary>The Seimas bill number, the natural key of a bill (design 3). One appeal per bill, except rejected ones.</summary>
    public string BillNumber { get; set; } = "";

    public Guid InitiatorUserId { get; set; }

    /// <summary>The initiator's neutral description of what the bill changes; approved by a moderator (design 8.2).</summary>
    public string Description { get; set; } = "";

    /// <summary>See <see cref="AppealStatuses"/>.</summary>
    public string Status { get; set; } = AppealStatuses.Pending;

    /// <summary>When the initiator opened it.</summary>
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>When a moderator published it.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>The final Seimas vote that closed it.</summary>
    public long? ClosingVoteId { get; set; }

    /// <summary>The counts frozen when the appeal closed (design 8.1); null while open.</summary>
    public int? ClosedFor { get; set; }

    public int? ClosedForOlderThan30Days { get; set; }

    public int? ClosedAgainst { get; set; }

    public int? ClosedAgainstOlderThan30Days { get; set; }

    /// <summary><c>automatic</c> or <c>held</c> (design 8.4; used by the summaries).</summary>
    public string Sending { get; set; } = "automatic";

    public int Threshold { get; set; }

    /// <summary>See <see cref="ThresholdSetters"/>.</summary>
    public string ThresholdSetBy { get; set; } = ThresholdSetters.Initiator;

    /// <summary>A moderator's reason for changing the threshold, shown on the appeal page (design 8.4).</summary>
    public string? ThresholdNote { get; set; }

    /// <summary>When the appeal first entered a summary; from then on the initiator cannot change the threshold.</summary>
    public DateTimeOffset? FirstSummarisedAt { get; set; }

    /// <summary>The moderator's note when the appeal was returned to its initiator.</summary>
    public string? ReturnNote { get; set; }

    /// <summary>What pre-screening flagged in the description, for the moderator.</summary>
    public string[] ScreeningFlags { get; set; } = [];

    public DateTimeOffset ChangedAt { get; set; }
}

internal static class AppealStatuses
{
    /// <summary>Waiting for a moderator; visible only to the initiator.</summary>
    public const string Pending = "pending";

    /// <summary>Returned to the initiator with a note; visible only to the initiator, who may edit and send it again.</summary>
    public const string Returned = "returned";

    /// <summary>Published: anyone can read it, members can take a side.</summary>
    public const string Open = "open";

    /// <summary>The Seimas took the final vote (second half of M2a).</summary>
    public const string Closed = "closed";

    /// <summary>Hidden by a moderator. The bill may get a new appeal.</summary>
    public const string Rejected = "rejected";
}

internal static class ThresholdSetters
{
    public const string Initiator = "initiator";
    public const string Moderator = "moderator";
}

/// <summary>
/// <c>appeals.position</c>: one person's side on one bill (design 5.3). One row per person per appeal; changing the side
/// overwrites it, and no history of earlier sides is kept anywhere.
/// </summary>
internal sealed class Position
{
    public Guid AppealId { get; set; }

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

    public Guid AppealId { get; set; }

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
}

internal static class ReasonStatuses
{
    /// <summary>Flagged by pre-screening, or the initiator's first argument: waits for a moderator, visible to its author.</summary>
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

    /// <summary>The appeal of the argument, so the three-per-bill limit is one indexed count.</summary>
    public Guid AppealId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary><c>appeals.report</c>: notice and action under the Digital Services Act (design 5.3, 8.3).</summary>
internal sealed class Report
{
    public Guid Id { get; set; }

    /// <summary><c>appeal</c> or <c>reason</c>.</summary>
    public string TargetKind { get; set; } = "";

    public Guid TargetId { get; set; }

    /// <summary>The appeal the target belongs to.</summary>
    public Guid AppealId { get; set; }

    public Guid ReporterUserId { get; set; }

    /// <summary>One of <see cref="ReportGrounds"/>.</summary>
    public string Ground { get; set; } = "";

    public string? Text { get; set; }

    /// <summary><c>open</c> until a moderator acts on the target, then <c>closed</c>.</summary>
    public string Status { get; set; } = "open";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }
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
/// <c>appeals.daily_stat</c>: the daily aggregate counts per appeal (design 5.3), for the trend chart and surge
/// detection. Filled by the daily aggregation job (second half of M2a). The only source of trends: no per-person
/// history is stored.
/// </summary>
internal sealed class DailyStat
{
    public Guid AppealId { get; set; }

    public DateOnly Day { get; set; }

    public int ForCount { get; set; }

    public int AgainstCount { get; set; }

    public int ForOlderThan30Days { get; set; }

    public int AgainstOlderThan30Days { get; set; }

    /// <summary>Share of that day's new positions taken by accounts younger than 30 days.</summary>
    public decimal NewAccountShare { get; set; }
}

/// <summary>
/// <c>appeals.flag</c>: a participant says the planned date is wrong or the bill is being voted sooner, with a link to
/// the source (design 8.4). A moderator corrects the date or keeps it; the flag then closes.
/// </summary>
internal sealed class Flag
{
    public Guid Id { get; set; }

    public Guid AppealId { get; set; }

    /// <summary><c>date-wrong</c> or <c>sooner</c>.</summary>
    public string Kind { get; set; } = "";

    public string Link { get; set; } = "";

    public string? Note { get; set; }

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