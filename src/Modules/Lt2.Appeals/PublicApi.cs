using Lt2.SharedKernel;

namespace Lt2.Appeals;

// What the Appeals module answers and accepts. Codes are lower-case English words, stable for the API: side for |
// against; appeal status pending | returned | open | closed | rejected; reason status pending | published | hidden |
// merged; threshold set by initiator | moderator; report target appeal | reason.
//
// Privacy (design 6.1, 6.3): nothing here carries another person's position. The only side of a person any operation
// returns is the caller's own, in MyAppeal.

/// <summary>
/// Both sides together, each with the count for accounts older than 30 days (design 6.6). Built only through the
/// module's count rule, so the two numbers never travel apart.
/// </summary>
public sealed record SideCounts(int For, int ForOlderThan30Days, int Against, int AgainstOlderThan30Days)
{
    public int Total => For + Against;
}

/// <summary>The threshold as everyone sees it (design 8.4).</summary>
/// <param name="Value">The threshold that applies today (never below the current minimum unless already in summaries).</param>
/// <param name="SetBy"><c>initiator</c> or <c>moderator</c>.</param>
/// <param name="Note">A moderator's reason for changing it.</param>
/// <param name="Participants">Positions on both sides together.</param>
/// <param name="EnteredSummaries">The appeal has entered a summary sent to MPs.</param>
public sealed record ThresholdInfo(int Value, string SetBy, string? Note, int Participants, bool Reached, bool EnteredSummaries);

/// <summary>A public argument. <paramref name="AuthorName"/> is <c>null</c> when the author gave no display name.</summary>
public sealed record ReasonInfo(Guid Id, string Side, string Text, string? AuthorName, int Endorsements, string Status, DateTimeOffset CreatedAt);

/// <summary>An appeal as the public sees it, or as its initiator sees it before publication.</summary>
public sealed record AppealInfo(
    Guid Id,
    BillFacts Bill,
    string Status,
    string? InitiatorName,
    string Description,
    DateTimeOffset OpenedAt,
    DateTimeOffset? PublishedAt,
    SideCounts Counts,
    ThresholdInfo Threshold,
    IReadOnlyList<ReasonInfo> Reasons,
    string? ReturnNote,
    SurgeInfo? Surge = null,
    long? ClosingVoteId = null,
    DateTimeOffset? ClosedAt = null,
    PlannedDate? Planned = null);

/// <summary>
/// The surge note (design 6.6): in the last <paramref name="WindowHours"/> hours, <paramref name="SharePercent"/> % of the
/// positions taken came from accounts that became members within the same hours. An aggregate; never a person.
/// </summary>
public sealed record SurgeInfo(int SharePercent, int WindowHours, int RecentPositions, int FromNewAccounts);

public sealed record AppealListItem(Guid Id, BillFacts Bill, SideCounts Counts, ThresholdInfo Threshold, DateTimeOffset? PublishedAt, PlannedDate? Planned = null);

public sealed record AppealPage(IReadOnlyList<AppealListItem> Items, int Total, string? NextCursor);

/// <summary>
/// The caller's own part in one appeal: their side (only ever their own), their "Man svarbu" marks and arguments, and
/// what they may still do.
/// </summary>
public sealed record MyAppeal(
    string? Side,
    IReadOnlyList<Guid> Endorsed,
    int EndorsementsLeft,
    IReadOnlyList<ReasonInfo> MyReasons,
    int ReasonsLeft,
    bool IsInitiator,
    bool MayChangeThreshold,
    bool HasPositionConsent);

/// <summary>The numbers the forms need (design 8.4): the threshold offered, and the minimum.</summary>
public sealed record AppealRulesInfo(int ThresholdDefault, int ThresholdMinimum, int ReasonMaxLength, int ReasonsPerPerson, int EndorsementsPerPerson, int DescriptionMaxLength);

/// <summary>What the initiator gives when opening an appeal (design 8.1).</summary>
public sealed record OpenAppeal(string Side, string FirstReason, string Description, int Threshold);

/// <summary>A returned appeal, edited by its initiator and sent to the moderators again.</summary>
public sealed record EditAppeal(string Description, string FirstReason);

public sealed record NewReason(string Side, string Text);

/// <summary>A new argument: <c>pending</c> when pre-screening flagged it for a moderator, otherwise <c>published</c>.</summary>
public sealed record ReasonCreated(Guid Id, string Status);

public sealed record NewReport(string TargetKind, Guid TargetId, string Ground, string? Text);

/// <summary>Who acts as a moderator, for the audit log (design 8.3).</summary>
public sealed record ModeratorActor(Guid UserId, string Role, string? IpHash);

public sealed record QueueReport(Guid Id, string Ground, string? Text, DateTimeOffset CreatedAt);

public sealed record QueueAppeal(
    Guid Id,
    BillFacts Bill,
    string Status,
    string? InitiatorName,
    string Description,
    ReasonInfo? FirstReason,
    int Threshold,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReport> Reports,
    DateTimeOffset OpenedAt,
    SurgeInfo? Surge = null,
    PlannedDate? Planned = null,
    IReadOnlyList<QueueFlag>? DateFlags = null,
    bool UnderThresholdOnAgenda = false,
    bool Held = false);

/// <summary>A participant's flag about the planned date (design 8.4). Who flagged is not shown.</summary>
public sealed record QueueFlag(Guid Id, string Kind, string Link, string? Note, DateTimeOffset CreatedAt);

public sealed record NewFlag(string Kind, string Link, string? Note);

/// <summary>An argument for a moderator: flagged, reported, or not yet seen.</summary>
public sealed record QueueReason(
    Guid Id,
    Guid AppealId,
    BillFacts Bill,
    string Side,
    string Text,
    string? AuthorName,
    string Status,
    bool Seen,
    IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReport> Reports,
    IReadOnlyList<ReasonInfo> MergeCandidates,
    DateTimeOffset CreatedAt);

/// <summary>The moderators' queue (design 8.3) for this half: new appeals, flagged or reported arguments, reported appeals, unseen arguments.</summary>
public sealed record ModerationItems(IReadOnlyList<QueueAppeal> Appeals, IReadOnlyList<QueueReason> Reasons);

/// <summary>
/// The Appeals module (design 8). Every operation that acts for a person takes their account id; moderation operations
/// take the moderator. No operation returns another person's position.
/// </summary>
public interface IAppealsModule
{
    Task<AppealRulesInfo> RulesAsync(CancellationToken cancellationToken);

    /// <param name="sort"><c>participants</c> (default) or <c>newest</c>.</param>
    Task<AppealPage> ListAsync(string? sort, string? cursor, CancellationToken cancellationToken);

    /// <summary>
    /// The appeal on a bill: an open or closed one for anyone; a pending or returned one only for its initiator
    /// (<paramref name="viewer"/>). Otherwise <c>appeal.not-found</c>.
    /// </summary>
    Task<Result<AppealInfo>> GetAsync(string billNumber, Guid? viewer, CancellationToken cancellationToken);

    Task<Result<MyAppeal>> MineAsync(string billNumber, Guid userId, CancellationToken cancellationToken);

    /// <summary>The description's edits (oldest first), public for a published appeal; no editor is named.</summary>
    Task<Result<IReadOnlyList<(DateTimeOffset ChangedAt, string? OldText, string? NewText)>>> HistoryAsync(string billNumber, CancellationToken cancellationToken);

    /// <summary>Opens the appeal on a bill; <c>appeal.exists</c> if the bill already has one.</summary>
    Task<Result<AppealInfo>> OpenAsync(string billNumber, Guid userId, OpenAppeal request, CancellationToken cancellationToken);

    Task<Result<Unit>> EditAsync(string billNumber, Guid userId, EditAppeal request, CancellationToken cancellationToken);

    Task<Result<Unit>> ChangeThresholdAsync(string billNumber, Guid userId, int threshold, CancellationToken cancellationToken);

    Task<Result<Unit>> TakePositionAsync(string billNumber, Guid userId, string side, CancellationToken cancellationToken);

    Task<Result<Unit>> ClearPositionAsync(string billNumber, Guid userId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ReasonInfo>>> FindSimilarAsync(string billNumber, NewReason request, CancellationToken cancellationToken);

    Task<Result<ReasonCreated>> WriteReasonAsync(string billNumber, Guid userId, NewReason request, CancellationToken cancellationToken);

    Task<Result<Unit>> EndorseAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken);

    Task<Result<Unit>> RemoveEndorsementAsync(Guid reasonId, Guid userId, CancellationToken cancellationToken);

    Task<Result<Unit>> ReportAsync(Guid userId, NewReport request, CancellationToken cancellationToken);

    /// <summary>"The date is wrong" or "this is being voted sooner", with a link (design 8.4); initiator and participants.</summary>
    Task<Result<Unit>> FlagDateAsync(string billNumber, Guid userId, NewFlag request, CancellationToken cancellationToken);
}

/// <summary>Moderation of appeals and arguments (design 8.3). Every action is audited and the author is told why.</summary>
public interface IAppealsModeration
{
    Task<ModerationItems> QueueAsync(CancellationToken cancellationToken);

    Task<Result<Unit>> PublishAppealAsync(Guid appealId, string? statement, ModeratorActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> ReturnAppealAsync(Guid appealId, string statement, ModeratorActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> HideAppealAsync(Guid appealId, string statement, ModeratorActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> SetThresholdAsync(Guid appealId, int threshold, string statement, ModeratorActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> PublishReasonAsync(Guid reasonId, string? statement, ModeratorActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> HideReasonAsync(Guid reasonId, string statement, ModeratorActor moderator, CancellationToken cancellationToken);

    Task<Result<Unit>> MergeReasonAsync(Guid reasonId, Guid intoReasonId, string statement, ModeratorActor moderator, CancellationToken cancellationToken);

    /// <summary>Holds sending for the appeal's bill (design 8.4): it enters no summary until released.</summary>
    Task<Result<Unit>> HoldAsync(Guid appealId, string statement, ModeratorActor moderator, CancellationToken cancellationToken);

    /// <summary>Releases a held appeal: summaries take it again by their rules.</summary>
    Task<Result<Unit>> ReleaseAsync(Guid appealId, string statement, ModeratorActor moderator, CancellationToken cancellationToken);

    /// <summary>Corrects the planned date or stage of the appeal's bill (design 8.4); closes its open date flags.</summary>
    Task<Result<Unit>> SetPlannedAsync(Guid appealId, DateOnly date, string? stage, string statement, ModeratorActor moderator, CancellationToken cancellationToken);
}

/// <summary>Scheduled work of the Appeals module, run by the worker.</summary>
public interface IAppealsMaintenance
{
    /// <summary>Writes (or rewrites) the daily aggregate of every published appeal for one Vilnius day; returns how many.</summary>
    Task<int> AggregateDayAsync(DateOnly day, CancellationToken cancellationToken);

    /// <summary>Closes every open appeal whose bill had its final Seimas vote, freezing the counts; returns how many.</summary>
    Task<int> CloseFinishedAsync(CancellationToken cancellationToken);
}