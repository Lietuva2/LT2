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
    /// A moderator writes or corrects the description of an active bill's open record (design 8.12.3, 8.13), with a
    /// required statement; the text or the official text's address must differ from what is shown.
    /// </summary>
    public static string? CheckDescriptionWrite(string recordStatus, string currentText, string newText, string? statement, string? currentUrl = null, string? newUrl = null)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return "statement.required";
        }

        if (recordStatus != BillRecordStatuses.Open)
        {
            return "moderation.wrong-state";
        }

        return currentText == newText && currentUrl == newUrl ? "description.unchanged" : null;
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
    /// A moderator removes a description (design 8.13), whatever made it, with a required statement; the bill then says it
    /// has none, and the summary job never writes it again.
    /// </summary>
    public static string? CheckRemove(string recordStatus, string currentText, string? statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            return "statement.required";
        }

        return recordStatus == BillRecordStatuses.Open && currentText.Length > 0 ? null : "moderation.wrong-state";
    }

    /// <summary>
    /// A bill enters the moderators' queue as "under its threshold and on the agenda" only when someone takes part on it
    /// (design 8.12.2): at least one participant, a planned date, and the threshold not reached.
    /// </summary>
    public static bool UnderThresholdOnAgenda(int participants, bool planned, bool reached) => participants > 0 && planned && !reached;
}

/// <summary>
/// The orders of <c>/projektai</c> and the front page's cards (design 8.12.5, decided 2026-11-14). Activity is the bill's
/// participants on both sides together plus its published arguments, a plain sum. Activity, participants and arguments
/// put the most first; the planned date the soonest first. Ties go by activity, then by planned date (soonest first,
/// bills without one last), then by number, so bills with nothing come last, by planned date.
/// </summary>
internal static class BillListRules
{
    public static int Activity(int participants, int arguments) => participants + arguments;

    public static IReadOnlyList<T> Order<T>(
        IEnumerable<T> items, string sort, Func<T, int> participants, Func<T, int> arguments, Func<T, DateOnly?> planned, Func<T, string> number)
    {
        int ActivityOf(T i) => Activity(participants(i), arguments(i));
        var first = sort switch
        {
            BillSorts.Participants => items.OrderByDescending(participants),
            BillSorts.Arguments => items.OrderByDescending(arguments),
            BillSorts.Planned => items.OrderBy(i => planned(i) ?? DateOnly.MaxValue),
            _ => items.OrderByDescending(ActivityOf),
        };
        return first
            .ThenByDescending(ActivityOf)
            .ThenBy(i => planned(i) ?? DateOnly.MaxValue)
            .ThenBy(number, StringComparer.Ordinal)
            .ToList();
    }
    /// <summary>
    /// The search by number or title (design 8.12.5): every word of the query, ignoring case, in the number or the title.
    /// A blank query matches everything.
    /// </summary>
    public static bool Matches(string? query, string number, string title)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var haystack = number + " " + title;
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
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
