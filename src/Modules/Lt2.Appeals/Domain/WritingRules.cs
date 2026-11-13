namespace Lt2.Appeals.Domain;

/// <summary>
/// A moderator's restriction on writing (design 8.3, decided 2026-11-13), as pure functions. A restriction is until a
/// date (inclusive, Vilnius time) or until lifted; a dated one ends by itself, checked on use.
/// </summary>
internal static class WritingRules
{
    public static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    /// <summary>Whether one restriction holds at <paramref name="now"/>: not lifted, and either open-ended or not yet over.</summary>
    public static bool Holds(DateTimeOffset? endsAt, DateTimeOffset? liftedAt, DateTimeOffset now) =>
        liftedAt is null && (endsAt is null || now < endsAt);

    /// <summary>May the person write now: no restriction of theirs holds.</summary>
    public static bool MayWrite(IEnumerable<(DateTimeOffset? EndsAt, DateTimeOffset? LiftedAt)> restrictions, DateTimeOffset now) =>
        !restrictions.Any(r => Holds(r.EndsAt, r.LiftedAt, now));

    /// <summary>"Until 20 November" means the whole of that day in Vilnius: it ends at the start of the next day.</summary>
    public static DateTimeOffset EndsAt(DateOnly untilInclusive)
    {
        var local = untilInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, Vilnius.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>The last day of a dated restriction, as the person and the moderator read it.</summary>
    public static DateOnly? Until(DateTimeOffset? endsAt) =>
        endsAt is { } end ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end, Vilnius).DateTime).AddDays(-1) : null;

    /// <summary>A date in the past would restrict nothing: the last day must be today or later.</summary>
    public static string? CheckUntil(DateOnly? untilInclusive, DateTimeOffset now) =>
        untilInclusive is { } day && day < DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Vilnius).DateTime) ? "restriction.until-past" : null;
}
