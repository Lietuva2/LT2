namespace Lt2.Appeals.Domain;

/// <summary>
/// Design 8.13 as pure functions: which bills the summary job takes, in which order, how many a day, and when a bill is
/// tried again.
/// </summary>
internal static class SummaryRules
{
    /// <summary>A bill whose official text was unavailable or not enough is tried again after this, not sooner.</summary>
    public static readonly TimeSpan RetryWhenNoText = TimeSpan.FromDays(7);

    /// <summary>
    /// A bill on which the provider failed (a refusal, a cut-off answer, an error) is tried again the next day: the text
    /// may well be fine.
    /// </summary>
    public static readonly TimeSpan RetryWhenFailed = TimeSpan.FromDays(1);

    /// <summary>
    /// What both public prompts (<c>Ai/description-summary.prompt.md</c>, the job's, and <c>Ai/description-draft.prompt.md</c>)
    /// tell the model to answer when the source is not enough. Such an answer is never stored as a description.
    /// </summary>
    public const string NotEnoughAnswer = "Iš pateikto teksto neaišku, ką projektas keičia.";

    /// <summary>
    /// Whether the job may make a summary for a bill now: it has no record, or an open one without a description that no
    /// person ever wrote or removed, and its last try allows another.
    /// </summary>
    /// <param name="recordStatus">The record's status, or null when the bill has none.</param>
    /// <param name="currentText">The description now (empty or null for none).</param>
    /// <param name="touchedByPerson">A person wrote, corrected or removed the description at some time.</param>
    /// <param name="retryAfter">The earliest next try after the last one, or null when never tried.</param>
    public static bool IsDue(string? recordStatus, string? currentText, bool touchedByPerson, DateTimeOffset? retryAfter, DateTimeOffset now) =>
        recordStatus is null or BillRecordStatuses.Open
        && string.IsNullOrEmpty(currentText)
        && !touchedByPerson
        && (retryAfter is null || retryAfter <= now);

    /// <summary>
    /// The order bills are taken in (design 8.13): those with a planned date first, soonest first; then the most
    /// participants; then by number, so the order is the same on every run.
    /// </summary>
    public static IReadOnlyList<T> Order<T>(IEnumerable<T> items, Func<T, DateOnly?> planned, Func<T, int> participants, Func<T, string> number) =>
        items.OrderBy(i => planned(i) is null ? 1 : 0)
            .ThenBy(i => planned(i) ?? DateOnly.MaxValue)
            .ThenByDescending(participants)
            .ThenBy(number, StringComparer.Ordinal)
            .ToList();

    /// <summary>How many more the provider may be asked for today: <c>Ai:SummariesPerDay</c> less those asked already; 0 = off.</summary>
    public static int LeftToday(int perDay, int askedToday) => Math.Max(0, perDay - askedToday);

    /// <summary>When a bill may be tried again after a try that ended so.</summary>
    public static DateTimeOffset RetryAfter(string outcome, DateTimeOffset triedAt) =>
        triedAt + (outcome == SummaryOutcomes.Failed ? RetryWhenFailed : RetryWhenNoText);

    /// <summary>The model's answer says the source is not enough (the prompt's fixed sentence, with or without quotes).</summary>
    public static bool IsNotEnough(string answer) =>
        answer.Contains(NotEnoughAnswer.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
}
