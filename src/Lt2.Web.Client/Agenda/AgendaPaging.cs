using System.Globalization;

namespace Lt2.Web.Client.Agenda;

/// <summary>A page whose days grow by the address: its path and the ids of its day blocks (a date follows the prefix).</summary>
public sealed record AgendaPagingPage(string Path, string PlannedPrefix, string VotedPrefix)
{
    /// <summary>A planned day's block.</summary>
    public string PlannedDayId(DateOnly day) => PlannedPrefix + Display.Day(day);

    /// <summary>A voting day's block.</summary>
    public string VotedDayId(DateOnly day) => VotedPrefix + Display.Day(day);
}

/// <summary>
/// The days of the agenda page and the front page and how a day grows (design 8.11.1, "Days and their length", and 8.7;
/// owner, 2026-11-19: one way for every list). Each day shows its first rows (<see cref="AgendaRules.PagePerDay"/>, on the
/// front page <see cref="AgendaRules.FrontPerDay"/>), and the shared "Rodyti daugiau" (ShowMore, "Rodoma N iš M") leads to
/// the same page with that day grown by <see cref="AgendaRules.MorePerDay"/>, by the address: <c>?diena=2026-11-24:60</c> for
/// a planned day, <c>?balsuota=2026-11-17:60</c> for a voted one (the day, a colon, how many of its known rows to show),
/// and the served page holds only the rows it shows. The days already grown stay so (the parameter repeats), so nothing the reader saw goes away; a
/// value the page would not make is left out, never an error.
/// </summary>
public static class AgendaPaging
{
    /// <summary>The planned days grown: <c>yyyy-MM-dd:N</c>.</summary>
    public const string PlannedParameter = "diena";

    /// <summary>The voted days grown: <c>yyyy-MM-dd:N</c>.</summary>
    public const string VotedParameter = "balsuota";

    /// <summary>At most this many days of each kind are read from the address (the agenda reaches 10–12 days ahead, the votes seven back).</summary>
    public const int MaxDays = 31;

    /// <summary>The agenda page, <c>/darbotvarke</c>: <c>#day-…</c>, <c>#voted-day-…</c>.</summary>
    public static readonly AgendaPagingPage Agenda = new("/darbotvarke", "day-", "voted-day-");

    /// <summary>The front page's timeline: <c>#home-day-…</c>, <c>#home-voted-…</c>.</summary>
    public static readonly AgendaPagingPage Front = new("/", "home-day-", "home-voted-");

    /// <summary>A planned row's id (the same on both pages, and in the line of unknown bills).</summary>
    public static string PlannedRowId(DateOnly day, string number) => "agenda-" + Display.Day(day) + "-" + number;

    /// <summary>A voted row's id.</summary>
    public static string VotedRowId(long voteId) => "recent-" + voteId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The days in the address with how many known rows each asks for: <c>yyyy-MM-dd:N</c> with a well-formed day and a whole
    /// number above zero, at most <see cref="MaxDays"/> days; a day named twice keeps the larger number. Anything else (a bare
    /// day, a word, a negative number) is left out. Whether the number is more than the page's first rows is the page's to
    /// say (<see cref="AgendaRules.ShownKnown"/>).
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, int> Read(IEnumerable<string?>? values)
    {
        var days = new SortedDictionary<DateOnly, int>();
        foreach (var value in values ?? [])
        {
            var parts = value?.Trim().Split(':');
            if (parts is not { Length: 2 }
                || !Display.TryReadDate(parts[0], out var day)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || count <= 0)
            {
                continue;
            }

            if (days.TryGetValue(day, out var before))
            {
                days[day] = Math.Max(before, count);
            }
            else if (days.Count < MaxDays)
            {
                days[day] = count;
            }
        }

        return days;
    }

    /// <summary>The page with these days grown. Days at their initial length are omitted.</summary>
    public static string Address(
        AgendaPagingPage page, IReadOnlyDictionary<DateOnly, int> planned, IReadOnlyDictionary<DateOnly, int> voted)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(voted);
        var parts = planned.OrderBy(d => d.Key).Select(d => PlannedParameter + "=" + Value(d.Key, d.Value))
            .Concat(voted.OrderBy(d => d.Key).Select(d => VotedParameter + "=" + Value(d.Key, d.Value)))
            .ToList();
        return parts.Count == 0 ? page.Path : page.Path + "?" + string.Join("&", parts);
    }

    /// <summary>"Rodyti daugiau" under a planned day: the page as it is, with that day grown to <paramref name="asked"/> rows.</summary>
    public static string MorePlanned(
        AgendaPagingPage page, IReadOnlyDictionary<DateOnly, int> planned, IReadOnlyDictionary<DateOnly, int> voted, DateOnly day, int asked) =>
        Address(page, With(planned, day, asked), voted);

    /// <summary>"Rodyti daugiau" under a voted day.</summary>
    public static string MoreVoted(
        AgendaPagingPage page, IReadOnlyDictionary<DateOnly, int> planned, IReadOnlyDictionary<DateOnly, int> voted, DateOnly day, int asked) =>
        Address(page, planned, With(voted, day, asked));

    /// <summary>
    /// The days a page shows grown beyond their first rows, each with the known rows it shows: what its links carry on, so a
    /// day no longer on the page or a number the page would not make is not repeated.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, int> Grown(IEnumerable<(DateOnly Day, int Shown)> days, int first) =>
        days.Where(d => d.Shown > first).GroupBy(d => d.Day).ToDictionary(g => g.Key, g => g.Max(d => d.Shown));

    private static string Value(DateOnly day, int count) => Display.Day(day) + ":" + count.ToString(CultureInfo.InvariantCulture);

    private static Dictionary<DateOnly, int> With(IReadOnlyDictionary<DateOnly, int> days, DateOnly day, int asked)
    {
        ArgumentNullException.ThrowIfNull(days);
        return new Dictionary<DateOnly, int>(days) { [day] = asked };
    }
}
