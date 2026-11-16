using Lt2.SharedKernel;

namespace Lt2.Notifications.Rhythm;

/// <summary>A member's choice of how often their emails come (design 8.24); the API's values.</summary>
internal static class Frequencies
{
    /// <summary>"Iš karto": within the follow job's next run, one email per run (the default).</summary>
    public const string Immediate = "immediate";

    /// <summary>"Kartą per dieną": one morning email.</summary>
    public const string Daily = "daily";

    /// <summary>"Kartą per savaitę ir artėjant balsavimui": one Monday email, and a "planned" date that cannot wait.</summary>
    public const string Weekly = "weekly";

    public static readonly IReadOnlyList<string> All = [Immediate, Daily, Weekly];

    public static bool IsValid(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>The emails of one run of the follow job (design 8.24): which of them is being made.</summary>
internal enum Pass
{
    /// <summary>The run's own emails, for "Iš karto".</summary>
    Immediate,

    /// <summary>A "planned" date that cannot wait for a daily or weekly email.</summary>
    Early,

    /// <summary>The morning email of "Kartą per dieną".</summary>
    Daily,

    /// <summary>The Monday email of "Kartą per savaitę ir artėjant balsavimui".</summary>
    Weekly,
}

/// <summary>
/// The person's choice as stored (design 8.24): the choice and when it was made, and the one before it with its own start.
/// Nothing else about the person.
/// </summary>
internal sealed record RhythmChoice(string Frequency, DateTimeOffset? Since, string? Previous, DateTimeOffset? PreviousSince)
{
    /// <summary>No row: "Iš karto", always.</summary>
    public static readonly RhythmChoice Default = new(Frequencies.Immediate, null, null, null);
}

/// <summary>Design 8.24 as pure functions: whose email an event goes in, and when the gathered emails are due.</summary>
internal static class RhythmRules
{
    /// <summary>
    /// How soon a change may be undone as if it had not been made (design 8.24): one run of the follow job. Later, the choice
    /// left may have had its own email meanwhile, and restoring it would tell twice what that email held.
    /// </summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(15);

    /// <summary>The weekly email's day (design 8.24: Monday, every week).</summary>
    public const DayOfWeek WeeklyDay = DayOfWeek.Monday;

    /// <summary>
    /// The choice an event belongs to for a person: the one they had when the job first saw it. An event seen after the
    /// choice was made belongs to it; one seen before, to the choice before it, while that held; one seen before even that,
    /// to nobody (it went already under "Iš karto", or waited for an email the person has since left twice: never sent twice).
    /// </summary>
    public static string? ChoiceAt(RhythmChoice choice, DateTimeOffset seen)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Since is not { } since || seen >= since)
        {
            return choice.Frequency;
        }

        return choice.PreviousSince is not { } previousSince || seen >= previousSince ? choice.Previous ?? Frequencies.Immediate : null;
    }

    /// <summary>
    /// The choice after a person picks <paramref name="frequency"/> at <paramref name="now"/>: the same choice changes nothing;
    /// going back within <see cref="UndoWindow"/> to the choice just left undoes the change (it holds again from its own start,
    /// so a mis-click corrected at once loses nothing that waited for it, nor anything seen meanwhile); any other keeps the one
    /// it replaces, so what waited
    /// for that one's email still comes in it (design 8.24).
    /// </summary>
    public static RhythmChoice Change(RhythmChoice current, string frequency, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Frequency == frequency)
        {
            return current;
        }

        return current.Previous == frequency && current.Since is { } since && now >= since && now - since <= UndoWindow
            ? new RhythmChoice(frequency, current.PreviousSince, null, null)
            : new RhythmChoice(frequency, now, current.Frequency, current.Since);
    }

    /// <summary>Which choice's emails a pass makes; an early pass serves the two gathered choices.</summary>
    public static bool Serves(Pass pass, string? choice) => pass switch
    {
        Pass.Immediate => choice == Frequencies.Immediate,
        Pass.Early => choice is Frequencies.Daily or Frequencies.Weekly,
        Pass.Daily => choice == Frequencies.Daily,
        Pass.Weekly => choice == Frequencies.Weekly,
        _ => false,
    };

    /// <summary>
    /// The latest daily email's moment at or before <paramref name="now"/>: <paramref name="at"/> (Vilnius time) today, or
    /// yesterday before it. The daily email holds what the job first saw before this moment and no daily email held.
    /// </summary>
    public static DateTimeOffset DailyCut(DateTimeOffset now, TimeOnly at)
    {
        var today = VilniusTime.DateOf(now);
        var cut = At(today, at);
        return cut <= now ? cut : At(today.AddDays(-1), at);
    }

    /// <summary>The latest weekly email's moment at or before <paramref name="now"/>: Monday at <paramref name="at"/> (Vilnius time).</summary>
    public static DateTimeOffset WeeklyCut(DateTimeOffset now, TimeOnly at)
    {
        var today = VilniusTime.DateOf(now);
        var monday = today.AddDays(-(((int)today.DayOfWeek - (int)WeeklyDay + 7) % 7));
        var cut = At(monday, at);
        return cut <= now ? cut : At(monday.AddDays(-7), at);
    }

    /// <summary>The first gathered email of a choice after <paramref name="seen"/>; null for "Iš karto", which gathers nothing.</summary>
    public static DateTimeOffset? NextGathered(string? choice, DateTimeOffset seen, TimeOnly at) => choice switch
    {
        Frequencies.Daily => At(VilniusTime.DateOf(DailyCut(seen, at)).AddDays(1), at),
        Frequencies.Weekly => At(VilniusTime.DateOf(WeeklyCut(seen, at)).AddDays(7), at),
        _ => null,
    };

    /// <summary>
    /// A "planned" date that cannot wait (design 8.24): on or before the Vilnius day of the person's next gathered email,
    /// counted from when the job first saw the date; the email of the sitting's own morning is too late to act on. Never for
    /// "Iš karto", which tells it at once anyway.
    /// </summary>
    public static bool CannotWait(DateOnly plannedDate, string? choice, DateTimeOffset seen, TimeOnly at) =>
        NextGathered(choice, seen, at) is { } next && plannedDate <= VilniusTime.DateOf(next);

    /// <summary>
    /// Whether a gathered email made with the cut <paramref name="cut"/> takes an event first seen at <paramref name="seen"/>:
    /// only what was seen before the cut (an event seen in the run of 08:07 waits for the next one).
    /// </summary>
    public static bool InGathered(DateTimeOffset seen, DateTimeOffset cut) => seen < cut;

    /// <summary>The instant of <paramref name="at"/> (Vilnius time) on <paramref name="day"/>.</summary>
    private static DateTimeOffset At(DateOnly day, TimeOnly at)
    {
        var local = day.ToDateTime(at);
        return new DateTimeOffset(local, VilniusTime.Zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

/// <summary>
/// <c>notify.rhythm_pass</c> (design 8.24): that the daily email of a Vilnius day, or the weekly email of a week (its Monday),
/// was made. No person in it. The pass is not made again for the same period, even when <c>Follows:ResponsesAt</c> moves to a
/// later hour the same day.
/// </summary>
internal sealed class RhythmPassMade
{
    /// <summary><c>daily</c> or <c>weekly</c>.</summary>
    public string Pass { get; set; } = "";

    /// <summary>The Vilnius day of the daily email, or the Monday of the weekly one.</summary>
    public DateOnly Period { get; set; }

    public DateTimeOffset MadeAt { get; set; }
}