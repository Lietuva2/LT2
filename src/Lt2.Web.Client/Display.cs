using System.Globalization;

namespace Lt2.Web.Client;

/// <summary>
/// How every page writes numbers and dates (<c>AGENTS.md</c> "Wording", design/ui/README.md): tabular figures with a
/// no-break space for thousands; dates in numbers as <c>yyyy-MM-dd</c>, with <c>HH:mm</c> in Vilnius time; a typed date
/// is a text field in <c>yyyy-MM-dd</c>, never the browser's date control. The one formatting class of the interface,
/// used by the WebAssembly islands and the server-rendered pages alike (the browser cannot use
/// <c>Lt2.SharedKernel.VilniusTime</c>, so the zone is looked up here for the interface).
/// </summary>
public static class Display
{
    /// <summary>The placeholder of a typed date field.</summary>
    public const string DateExample = "2026-11-19";

    /// <summary>The pattern a typed date field checks before it is sent (the server checks again).</summary>
    public const string DatePattern = @"\d{4}-\d{2}-\d{2}";

    private const char NoBreak = ' ';
    private static readonly NumberFormatInfo Numbers = new() { NumberGroupSeparator = NoBreak.ToString(), NumberGroupSizes = [3] };

    /// <summary>The <c>Europe/Vilnius</c> zone.</summary>
    public static TimeZoneInfo Vilnius { get; } = FindVilnius();

    /// <summary>A whole number, "1 234"; a dash when there is none.</summary>
    public static string Number(int? value) => value is { } n ? n.ToString("#,0", Numbers) : "–";

    /// <summary>Whole per cent, rounded half away from zero; null when there is nothing to count.</summary>
    public static string? Percent(decimal? share) =>
        share is { } s ? Number((int)Math.Round(s * 100, MidpointRounding.AwayFromZero)) + NoBreak + "%" : null;

    /// <summary>The local Vilnius date and time of an instant.</summary>
    public static DateTime InVilnius(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Vilnius).DateTime;

    /// <summary>The Vilnius calendar day an instant falls on.</summary>
    public static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(InVilnius(at));

    /// <summary>Today in Vilnius.</summary>
    public static DateOnly Today(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DayOf(clock.GetUtcNow());
    }

    public static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The Vilnius day of a moment.</summary>
    public static string Day(DateTimeOffset at) => Day(DayOf(at));

    /// <summary>The Vilnius day and time of a moment.</summary>
    public static string DayTime(DateTimeOffset at) => InVilnius(at).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The time of day of a moment in Vilnius, <c>HH:mm</c>.</summary>
    public static string Time(DateTimeOffset at) => InVilnius(at).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Reads a typed <c>yyyy-MM-dd</c> date; spaces around it are ignored, nothing else is accepted.</summary>
    public static bool TryReadDate(string? text, out DateOnly day) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>
    /// The system's zone; where a runtime has no zone data, the same rule built by hand (UTC+2, summer time from the last
    /// Sunday of March 03:00 to the last Sunday of October 04:00, as in the EU), never UTC.
    /// </summary>
    private static TimeZoneInfo FindVilnius()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 3, 5, DayOfWeek.Sunday);
            var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 4, 0, 0), 10, 5, DayOfWeek.Sunday);
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
            return TimeZoneInfo.CreateCustomTimeZone("Europe/Vilnius", TimeSpan.FromHours(2), "Europe/Vilnius", "EET", "EEST", [rule]);
        }
    }
}
