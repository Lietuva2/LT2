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
/// owner, 2026-11-18): each day shows its first rows (<see cref="AgendaRules.PagePerDay"/>, on the front page
/// <see cref="AgendaRules.FrontPerDay"/>), and "Rodyti likusius" leads to the same page with that day whole, by the address
/// (<c>?diena=2026-11-24</c> for a planned day, <c>?balsuota=2026-11-17</c> for a voted one), landing on the first row
/// added. The served page holds only the rows it shows. The days already opened stay open (the parameter repeats), so
/// nothing the reader saw goes away; a value the page would not make is left out, never an error.
/// </summary>
public static class AgendaPaging
{
    /// <summary>The planned days shown whole.</summary>
    public const string PlannedParameter = "diena";

    /// <summary>The voted days shown whole.</summary>
    public const string VotedParameter = "balsuota";

    /// <summary>At most this many days of each kind are read from the address (the agenda reaches 10–12 days ahead, the votes seven back).</summary>
    public const int MaxDays = 31;

    /// <summary>The agenda page, <c>/darbotvarke</c>: <c>#diena-…</c>, <c>#balsuota-…</c>.</summary>
    public static readonly AgendaPagingPage Agenda = new("/darbotvarke", "diena-", "balsuota-");

    /// <summary>The front page's timeline: <c>#home-day-…</c>, <c>#home-voted-…</c>.</summary>
    public static readonly AgendaPagingPage Front = new("/", "home-day-", "home-voted-");

    /// <summary>The list of a day's rows after the first ones, where "Rodyti likusius" lands.</summary>
    public static string RestId(string dayId) => dayId + "-likusieji";

    /// <summary>The days in the address: well-formed <c>yyyy-MM-dd</c> only, each once, at most <see cref="MaxDays"/>.</summary>
    public static IReadOnlySet<DateOnly> Read(IEnumerable<string?>? values)
    {
        var days = new SortedSet<DateOnly>();
        foreach (var value in values ?? [])
        {
            if (days.Count < MaxDays && Display.TryReadDate(value, out var day))
            {
                days.Add(day);
            }
        }

        return days;
    }

    /// <summary>The page with these days whole, landing on <paramref name="fragment"/>; no parameter for none.</summary>
    public static string Address(AgendaPagingPage page, IEnumerable<DateOnly> planned, IEnumerable<DateOnly> voted, string? fragment = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(voted);
        var parts = planned.Distinct().Order().Select(d => PlannedParameter + "=" + Display.Day(d))
            .Concat(voted.Distinct().Order().Select(d => VotedParameter + "=" + Display.Day(d)))
            .ToList();
        var address = parts.Count == 0 ? page.Path : page.Path + "?" + string.Join("&", parts);
        return fragment is null ? address : address + "#" + fragment;
    }

    /// <summary>"Rodyti likusius" under a planned day: the page as it is, with that day whole too, landing on its first row added.</summary>
    public static string RestOfPlanned(AgendaPagingPage page, IReadOnlySet<DateOnly> planned, IReadOnlySet<DateOnly> voted, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Address(page, planned.Append(day), voted, RestId(page.PlannedDayId(day)));
    }

    /// <summary>"Rodyti likusius" under a voted day.</summary>
    public static string RestOfVoted(AgendaPagingPage page, IReadOnlySet<DateOnly> planned, IReadOnlySet<DateOnly> voted, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Address(page, planned, voted.Append(day), RestId(page.VotedDayId(day)));
    }
}
