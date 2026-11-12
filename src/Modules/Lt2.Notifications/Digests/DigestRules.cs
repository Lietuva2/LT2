using Lt2.SharedKernel;

namespace Lt2.Notifications.Digests;

/// <summary>
/// When summaries are made and what goes in them (design 8.4), as pure functions. Times are Vilnius local times.
/// </summary>
internal static class DigestRules
{
    public const string Weekly = "weekly";
    public const string BeforeVote = "before-vote";
    public const string Urgent = "urgent";

    /// <summary>
    /// "Changed notably since the last summary" is not defined by the design. Chosen here: the appeal was never in a
    /// summary, or its participants (both sides) changed by at least this share since the last summary that held it.
    /// </summary>
    public const decimal NotableChange = 0.10m;

    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Monday to Friday. Public holidays are not known to the system yet, so they count as working days.</summary>
    public static bool IsWorkingDay(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static DateOnly WorkingDayBefore(DateOnly day)
    {
        var d = day.AddDays(-1);
        while (!IsWorkingDay(d))
        {
            d = d.AddDays(-1);
        }

        return d;
    }

    /// <summary>A time setting: "HH:mm", or "off".</summary>
    public static TimeOnly? TimeOf(string setting) =>
        TimeOnly.TryParseExact(setting, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) ? t : null;

    /// <summary>The weekly summary: Monday from the set time, in a week when the Seimas sits, once a week.</summary>
    public static bool WeeklyDue(DateOnly today, TimeOnly now, TimeOnly? at, bool seimasSitsThisWeek, bool alreadyMade) =>
        at is { } time && today.DayOfWeek == DayOfWeek.Monday && now >= time && seimasSitsThisWeek && !alreadyMade;

    /// <summary>The before-vote summary: from the set time on the working day before the sitting, once per sitting.</summary>
    public static bool BeforeVoteDue(DateOnly today, TimeOnly now, TimeOnly? at, DateOnly sitting, bool alreadyMade) =>
        at is { } time && today == WorkingDayBefore(sitting) && now >= time && !alreadyMade;

    /// <summary>An appeal enters summaries when it reached its own threshold and its sending is not held.</summary>
    public static bool Eligible(DigestCandidate candidate) => candidate.ReachedThreshold && !candidate.Held;

    public static bool ChangedNotably(int participantsNow, int? participantsAtLastSummary) =>
        participantsAtLastSummary is not { } last
        || (last == 0 ? participantsNow > 0 : Math.Abs(participantsNow - last) >= NotableChange * last);

    /// <summary>The weekly summary lists eligible appeals on that week's agenda or changed notably since their last summary.</summary>
    public static IReadOnlyList<DigestCandidate> WeeklyItems(
        IEnumerable<DigestCandidate> candidates, DateOnly weekStart, IReadOnlyDictionary<Guid, int> participantsAtLastSummary) =>
        Order(candidates.Where(c => Eligible(c)
            && ((c.Planned is { } p && p.Date >= weekStart && p.Date < weekStart.AddDays(7))
                || ChangedNotably(c.For + c.Against, participantsAtLastSummary.TryGetValue(c.AppealId, out var last) ? last : null))));

    /// <summary>
    /// The before-vote summary: eligible appeals whose bill is planned for that sitting at a stage that is voted on, unless
    /// that week's Monday summary already covered the same sitting for them.
    /// </summary>
    public static IReadOnlyList<DigestCandidate> BeforeVoteItems(
        IEnumerable<DigestCandidate> candidates, DateOnly sitting, IReadOnlySet<Guid> coveredByMonday) =>
        Order(candidates.Where(c => Eligible(c) && c.Planned is { } p && p.Date == sitting && p.Stage is not null && !coveredByMonday.Contains(c.AppealId)));

    /// <summary>
    /// Most relevant first (design 8.4): bills on the agenda in the coming days by date, then the rest by participation.
    /// The MP's lead committee would come first, but bills have no lead committee without the e-seimas enrichment.
    /// </summary>
    public static IReadOnlyList<DigestCandidate> Order(IEnumerable<DigestCandidate> items) =>
        items.OrderBy(c => c.Planned is null)
            .ThenBy(c => c.Planned?.Date)
            .ThenByDescending(c => c.For + c.Against)
            .ThenBy(c => c.BillNumber, StringComparer.Ordinal)
            .ToList();

    /// <summary>The ceiling per MP: at most so many summaries a week; an urgent one may exceed it by one.</summary>
    public static bool WithinCeiling(int alreadyThisWeek, int maxPerWeek, bool urgent) =>
        alreadyThisWeek < maxPerWeek || (urgent && alreadyThisWeek < maxPerWeek + 1);
}
