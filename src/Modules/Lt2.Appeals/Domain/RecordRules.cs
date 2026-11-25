using Lt2.SharedKernel;

namespace Lt2.Appeals.Domain;

/// <summary>
/// Design 8.12 as pure functions: every active bill is open, its record is created when first needed, and moderation acts
/// afterwards. Each check returns the error code a request gets when it breaks the rule, or <c>null</c>.
/// </summary>
internal static class RecordRules
{
    /// <summary>The longest official text's address (<c>record.description_url</c>).</summary>
    public const int DescriptionUrlMaxLength = 500;

    /// <summary>A row in one of these statuses is the bill's record; at most one per bill (a unique index stands behind it).</summary>
    public static readonly string[] RecordStatuses = [BillRecordStatuses.Open, BillRecordStatuses.Closed, BillRecordStatuses.Rejected];

    public static bool IsRecord(string status) => RecordStatuses.Contains(status);

    /// <summary>
    /// Whether a member may take part on a bill now (design 8.12.1, 6.8): on its open record, or on an active bill that has
    /// no record yet (the record is then created). A closed record, a hidden one (taking part closed by the moderators) and
    /// a finished bill refuse with <c>record.not-open</c>, the last from the moment its final vote is recorded, whether or
    /// not the hourly job has closed the record yet.
    /// </summary>
    /// <param name="recordStatus">The record's status, or null when the bill has none.</param>
    /// <param name="billFinished">The Seimas has finished the bill (its final vote is in the open data).</param>
    public static string? CheckParticipation(string? recordStatus, bool billFinished) =>
        !billFinished && recordStatus is null or BillRecordStatuses.Open ? null : "record.not-open";

    /// <summary>
    /// Whether the Seimas has finished the bill (design 6.8: the same moment for everyone): its record is closed, or its
    /// final vote is recorded.
    /// </summary>
    public static bool IsFinished(string? recordStatus, bool finalVoteRecorded) => recordStatus == BillRecordStatuses.Closed || finalVoteRecorded;

    /// <summary>A hidden record comes back as it was (design 8.12.3): closed if it had closed, otherwise open.</summary>
    public static string RestoredStatus(DateTimeOffset? closedAt) => closedAt is not null ? BillRecordStatuses.Closed : BillRecordStatuses.Open;

    /// <summary>Only a hidden record can be restored; there is one record per bill, so nothing else on the bill blocks it.</summary>
    public static string? CheckRestore(string status) => status == BillRecordStatuses.Rejected ? null : "moderation.wrong-state";

    /// <summary>
    /// What a moderator may still do on a closed record (design 6.9, "Reports after the final vote"): answer a report with
    /// "Palikti", and correct or remove the description. The record's arguments have their own rule,
    /// <see cref="CheckReasonAction"/>. Everything else on a closed record stays refused; on an open record every action
    /// is allowed here (each has its own checks besides).
    /// </summary>
    public static string? CheckRecordAction(string recordStatus, string action) =>
        recordStatus == BillRecordStatuses.Open || (recordStatus == BillRecordStatuses.Closed && action is "keep" or "describe" or "undescribe")
            ? null
            : "moderation.wrong-state";

    /// <summary>
    /// What a moderator may do with an argument by its record's status (design 6.9): anything on an open record; on a closed
    /// one only hide and restore it, and answer a report on a published one with "Palikti" (publishing a published
    /// argument marks it seen). Publishing a waiting one, merging, and anything on a hidden record are refused.
    /// </summary>
    public static string? CheckReasonAction(string recordStatus, string action, string reasonStatus) =>
        recordStatus == BillRecordStatuses.Open
        || (recordStatus == BillRecordStatuses.Closed && (action is "hide" or "restore" || (action == "publish" && reasonStatus == ReasonStatuses.Published)))
            ? null
            : "moderation.wrong-state";

    /// <summary>
    /// A moderator writes or corrects the description of a bill's open record, or of its closed one (design 8.12.3, 8.13,
    /// 6.9), with a required statement; the text, the official text's address or the readable title (8.13.1) must differ from what is shown.
    /// </summary>
    public static string? CheckDescriptionWrite(string recordStatus, string currentText, string newText, string? statement, string? currentUrl = null, string? newUrl = null, string? currentTitle = null, string? newTitle = null)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return "statement.required";
        }

        if (CheckRecordAction(recordStatus, "describe") is { } refused)
        {
            return refused;
        }

        return currentText == newText && currentUrl == newUrl && AppealRules.NormalisePlainTitle(currentTitle) == AppealRules.NormalisePlainTitle(newTitle) ? "description.unchanged" : null;
    }

    /// <summary>The address stored: trimmed, or null for an empty field, which clears it.</summary>
    public static string? NormaliseDescriptionUrl(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The official text's address a moderator records with a description (design 8.13, decided by the owner on
    /// 2026-11-14): empty, or an <c>https</c> address whose host is <c>lrs.lt</c> or under it, with no user name or password,
    /// at most 500 characters. The same rule as <c>OfficialTextAddress</c> in the API contracts, which the form and the
    /// endpoint apply; a unit test holds the two together.
    /// </summary>
    public static string? CheckDescriptionUrl(string? value)
    {
        var address = NormaliseDescriptionUrl(value);
        if (address is null)
        {
            return null;
        }

        if (!HttpsAddress.TryParse(address, DescriptionUrlMaxLength, out var uri))
        {
            return "description-url.invalid";
        }

        var host = uri.IdnHost;
        return host.Equals("lrs.lt", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".lrs.lt", StringComparison.OrdinalIgnoreCase) ? null : "description-url.invalid";
    }

    /// <summary>The longest source address of a moderator's close (<c>record.closed_source_url</c>): the description's rule.</summary>
    public const int ClosingUrlMaxLength = DescriptionUrlMaxLength;

    /// <summary>
    /// The source a moderator gives when closing a record (design 8.1 step 6, owner 2026-11-19): required, and the rule of the
    /// description's official text (<see cref="CheckDescriptionUrl"/>): an <c>https</c> address on <c>lrs.lt</c> or a host under
    /// it, no user name or password, at most 500 characters.
    /// </summary>
    public static string? CheckClosingUrl(string? value) =>
        NormaliseDescriptionUrl(value) is not null && CheckDescriptionUrl(value) is null ? null : "closing-url.invalid";

    /// <summary>
    /// A moderator closes a record because the Seimas rejected the bill or its initiators withdrew it (design 8.1 step 6, owner
    /// 2026-11-19): a required statement, the reason (<c>rejected</c> or <c>withdrawn</c>), the day of that decision (not after
    /// <paramref name="today"/>, Vilnius) and the source; then only an open record of a bill whose final vote is not recorded
    /// (that one the closing job closes by the vote).
    /// </summary>
    public static string? CheckClose(
        string recordStatus, bool billFinished, string? reason, DateOnly decidedOn, DateOnly today, string? sourceUrl, string? statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return "statement.required";
        }

        if (reason is null || !ClosingReasons.ByModerator.Contains(reason))
        {
            return "closing.reason";
        }

        if (decidedOn == default)
        {
            return "closing.date-required";
        }

        if (decidedOn > today)
        {
            return "closing.date-future";
        }

        if (CheckClosingUrl(sourceUrl) is { } badUrl)
        {
            return badUrl;
        }

        return recordStatus == BillRecordStatuses.Open && !billFinished ? null : "moderation.wrong-state";
    }

    /// <summary>
    /// A moderator undoes a wrong close (design 8.1 step 6): only of a record a moderator closed (rejected or withdrawn), never
    /// one closed by the Seimas's vote, with a required statement.
    /// </summary>
    public static string? CheckReopen(string recordStatus, string? closedReason, string? statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return "statement.required";
        }

        return MayReopen(recordStatus, closedReason) ? null : "moderation.wrong-state";
    }

    /// <summary>Whether "Atšaukti uždarymą" applies: a closed record whose reason is a moderator's (rejected or withdrawn).</summary>
    public static bool MayReopen(string recordStatus, string? closedReason) =>
        recordStatus == BillRecordStatuses.Closed && closedReason is not null && ClosingReasons.ByModerator.Contains(closedReason);

    /// <summary>
    /// The source a moderator's description gets: <c>ai-draft</c> when it started from a draft, and it stays <c>ai-draft</c>
    /// when a moderator corrects such a description later (design 8.11.4: the label is for the description's whole life).
    /// </summary>
    public static string SourceOfModeratorText(bool fromDraft, string? currentSource) =>
        fromDraft || currentSource is DescriptionSources.AiDraft or DescriptionSources.Ai ? DescriptionSources.AiDraft : DescriptionSources.Moderator;

    /// <summary>
    /// A moderator confirms an AI summary nobody has read (design 8.13): only an <c>ai</c> description of an open record. It
    /// then becomes <c>ai-draft</c> ("Sukurta DI · taisė žmonės"); the text stays as it is. The note is optional.
    /// </summary>
    public static string? CheckConfirm(string recordStatus, string? descriptionSource) =>
        recordStatus == BillRecordStatuses.Open && descriptionSource == DescriptionSources.Ai ? null : "moderation.wrong-state";

    /// <summary>
    /// A moderator removes a description (design 8.13), whatever made it, on an open or a closed record (6.9), with a
    /// required statement; the bill then says it has none, and the summary job never writes it again.
    /// </summary>
    public static string? CheckRemove(string recordStatus, string currentText, string? statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return "statement.required";
        }

        return CheckRecordAction(recordStatus, "undescribe") is null && currentText.Length > 0 ? null : "moderation.wrong-state";
    }

    /// <summary>
    /// A bill enters the moderators' queue as "under its threshold and on the agenda" only when someone takes part on it
    /// (design 8.12.2): at least one participant, a planned date, and the threshold not reached.
    /// </summary>
    public static bool UnderThresholdOnAgenda(int participants, bool planned, bool reached) => participants > 0 && planned && !reached;
}

/// <summary>
/// The orders of lists of bills (design 8.12.5, decided 2026-11-14; followers since 2026-11-25, 8.11.2). Activity is the
/// bill's participants on both sides together plus its published arguments plus its followers, a plain sum: the one
/// definition that orders <c>/projektai</c>, a day's bills on the agenda and the front page (8.11.1) and "Projektai be
/// aprašymo" (8.3). No row shows the follower number. Activity, participants and arguments put the most first; the planned
/// date the soonest first; the last activity (owner, 2026-11-17) the latest first, bills without any last. Ties go by
/// activity, then by planned date (soonest first, bills without one last), then by number, so bills with nothing come last,
/// by planned date.
/// </summary>
internal static class BillListRules
{
    public static int Activity(int participants, int arguments, int followers) => participants + arguments + followers;

    public static IReadOnlyList<T> Order<T>(
        IEnumerable<T> items, string sort, Func<T, int> participants, Func<T, int> arguments, Func<T, int> followers, Func<T, DateOnly?> planned,
        Func<T, string> number, Func<T, DateTimeOffset?>? lastActivity = null)
    {
        int ActivityOf(T i) => Activity(participants(i), arguments(i), followers(i));
        var first = sort switch
        {
            BillSorts.Participants => items.OrderByDescending(participants),
            BillSorts.Arguments => items.OrderByDescending(arguments),
            BillSorts.Planned => items.OrderBy(i => planned(i) ?? DateOnly.MaxValue),
            BillSorts.Latest => items.OrderByDescending(i => lastActivity?.Invoke(i) ?? DateTimeOffset.MinValue),
            _ => items.OrderByDescending(ActivityOf),
        };
        return first
            .ThenByDescending(ActivityOf)
            .ThenBy(i => planned(i) ?? DateOnly.MaxValue)
            .ThenBy(number, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>Design 8.11.4 as pure functions: when a moderator may ask for an AI draft of a description.</summary>
internal static class AiDraftRules
{
    /// <summary>The most text a moderator may paste as the source (the relevant part of the explanatory note).</summary>
    public const int SourceMaxLength = 20_000;

    /// <summary>Drafts one moderator may ask for in an hour.</summary>
    public const int PerModeratorPerHour = 20;

    /// <summary>How long a draft counts as the start of a description the moderator then saves.</summary>
    public static readonly TimeSpan DraftValidFor = TimeSpan.FromHours(24);

    /// <summary>
    /// Without a source text there is no draft: the assistant never writes from the title alone. Then the hourly limit.
    /// </summary>
    public static string? CheckRequest(string? sourceText, int askedInLastHour)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return "draft.source-required";
        }

        if (sourceText.Length > SourceMaxLength)
        {
            return "draft.source-too-long";
        }

        return askedInLastHour >= PerModeratorPerHour ? "draft.too-many" : null;
    }

    /// <summary>
    /// A saved description counts as started from a draft only when the moderator asked for one on this bill within
    /// <see cref="DraftValidFor"/>: the label "Sukurta DI · taisė žmonės" cannot be claimed without a draft.
    /// </summary>
    public static string? CheckFromDraft(bool fromDraft, DateTimeOffset? lastDraftOnBill, DateTimeOffset now) =>
        !fromDraft || (lastDraftOnBill is { } at && now - at <= DraftValidFor) ? null : "draft.none";
}
