namespace Lt2.SharedKernel;

/// <summary>
/// Days as people in Lithuania count them (<c>AGENTS.md</c>: times are UTC in the database; schedules and display use
/// <c>Europe/Vilnius</c>). The one place the zone is looked up; take "now" from a <see cref="TimeProvider"/>.
/// </summary>
public static class VilniusTime
{
    /// <summary>The <c>Europe/Vilnius</c> zone.</summary>
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    /// <summary>The Vilnius calendar day an instant falls on.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    /// <summary>Today in Vilnius.</summary>
    public static DateOnly Today(TimeProvider clock) => DateOf(clock.GetUtcNow());

    /// <summary>The instant (UTC) a Vilnius calendar day begins: its local midnight.</summary>
    public static DateTimeOffset StartOf(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight)).ToUniversalTime();
    }
}
