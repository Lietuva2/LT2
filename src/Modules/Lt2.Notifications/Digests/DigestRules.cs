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

    /// <summary>The arguments per side a summary holds (design 8.4): the most endorsed.</summary>
    public const int ReasonsPerSide = 2;

    /// <summary>The Monday whose weekly summary is the next to go: today if it is Monday, otherwise the coming one.</summary>
    public static DateOnly WeeklySendingDay(DateOnly today) => today.AddDays((8 - (int)today.DayOfWeek) % 7);

    /// <summary>A Vilnius day at a Vilnius time, as a moment.</summary>
    public static DateTimeOffset At(DateOnly day, TimeOnly time) => FromVilnius(day.ToDateTime(time));

    /// <summary>A Vilnius wall-clock time as a moment; one the clocks skip in spring is read an hour later.</summary>
    private static DateTimeOffset FromVilnius(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (VilniusTime.Zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, VilniusTime.Zone), TimeSpan.Zero);
    }

    /// <summary>
    /// When a summary that goes at <paramref name="sendAt"/> is prepared (design 8.19.2, owner 2026-11-17):
    /// <paramref name="reviewHours"/> earlier on the Vilnius clock, so Monday 07:00 is prepared on Sunday 07:00 whatever the
    /// clocks did that night.
    /// </summary>
    public static DateTimeOffset PreparedAt(DateTimeOffset sendAt, int reviewHours) =>
        FromVilnius(TimeZoneInfo.ConvertTime(sendAt, VilniusTime.Zone).DateTime.AddHours(-reviewHours));

    /// <summary>
    /// A weekly or before-vote summary is prepared from its preparation time until its sending day ends in Vilnius, once
    /// (design 8.19.2). One found after its sending time (the worker was down) is prepared and goes in the same pass.
    /// </summary>
    public static bool PreparationDue(DateTimeOffset now, DateTimeOffset sendAt, int reviewHours, bool alreadyMade) =>
        !alreadyMade && now >= PreparedAt(sendAt, reviewHours) && VilniusTime.DateOf(now) <= VilniusTime.DateOf(sendAt);

    /// <summary>A prepared summary goes once its time has come; nobody approves it (owner, 2026-11-17).</summary>
    public static bool SendingDue(DateTimeOffset now, DateTimeOffset? dueAt) => dueAt is not { } at || now >= at;

    /// <summary>A record enters summaries when it reached its own threshold and its sending is not held.</summary>
    public static bool Eligible(DigestCandidate candidate) => candidate.ReachedThreshold && !candidate.Held;

    /// <summary>
    /// "Changed notably since the last summary" (design 8.4, decided 2026-11-13): the record was never in a summary, or its
    /// participants (both sides) changed by at least <paramref name="notableShare"/> (setting
    /// <c>Digest:NotableChange</c>) since the last summary that held it.
    /// </summary>
    public static bool ChangedNotably(int participantsNow, int? participantsAtLastSummary, decimal notableShare) =>
        participantsAtLastSummary is not { } last
        || (last == 0 ? participantsNow > 0 : Math.Abs(participantsNow - last) >= notableShare * last);

    /// <summary>The weekly summary lists eligible records on that week's agenda or changed notably since their last summary.</summary>
    public static IReadOnlyList<DigestCandidate> WeeklyItems(
        IEnumerable<DigestCandidate> candidates, DateOnly weekStart, IReadOnlyDictionary<Guid, int> participantsAtLastSummary, decimal notableShare) =>
        Order(candidates.Where(c => Eligible(c)
            && ((c.Planned is { } p && p.Date >= weekStart && p.Date < weekStart.AddDays(7))
                || ChangedNotably(c.For + c.Against, participantsAtLastSummary.TryGetValue(c.RecordId, out var last) ? last : null, notableShare))));

    /// <summary>
    /// The before-vote summary: eligible records whose bill is planned for that sitting at a stage that is voted on, unless
    /// that week's Monday summary already covered the same sitting for them.
    /// </summary>
    public static IReadOnlyList<DigestCandidate> BeforeVoteItems(
        IEnumerable<DigestCandidate> candidates, DateOnly sitting, IReadOnlySet<Guid> coveredByMonday) =>
        Order(candidates.Where(c => Eligible(c) && c.Planned is { } p && p.Date == sitting && p.Stage is not null && !coveredByMonday.Contains(c.RecordId)));

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
