namespace Lt2.Web.Client.Agenda;

/// <summary>
/// One sitting or voting day of a list as a page shows it (design 8.7, 8.11.1): the rows shown at once, the rest of the
/// day's known rows (fetched by "Rodyti likusius", AgendaPaging), the bills the open data does not know yet (folded into
/// one line), and how many the day holds in all.
/// </summary>
public sealed record AgendaDayView<T>(IReadOnlyList<T> Shown, IReadOnlyList<T> Rest, IReadOnlyList<T> Unknown)
{
    /// <summary>Every row of the day, known or not.</summary>
    public int Count => Shown.Count + Rest.Count + Unknown.Count;

    /// <summary>The day's known rows, shown and the rest.</summary>
    public int Known => Shown.Count + Rest.Count;
}

/// <summary>
/// The order and the lengths of the agenda's lists (design 8.11.1, 8.7; owner, 2026-11-18). The agenda says nothing about
/// the order or the time within a sitting day, so the rows of a day are ordered by what people do on them: activity first
/// (participants on both sides together plus published arguments, the plain sum of <c>/projektai</c>, design 8.12.5), then
/// how far the bill may go that day (priėmimas, svarstymas, pateikimas, none), then the bill's number. A bill the open data
/// does not know yet comes after every known one. Nothing else ranks a bill: not how many follow it (design 8.11.2), and
/// not whether the reader follows it (the server's page is the same for everyone). The numbers are the design's, not
/// settings.
/// </summary>
public static class AgendaRules
{
    /// <summary>The front page shows the next this many sitting days, and the last this many voting days.</summary>
    public const int FrontDays = 2;

    /// <summary>The front page shows this many rows of each day.</summary>
    public const int FrontPerDay = 5;

    /// <summary>The agenda page shows this many rows of each day before "Rodyti likusius".</summary>
    public const int PagePerDay = 10;

    /// <summary>Activity: participants on both sides together plus published arguments (design 8.12.5).</summary>
    public static int Activity(int participants, int arguments) => participants + arguments;

    /// <summary>The stage's place in a day: priėmimas first, then svarstymas, then pateikimas, then none.</summary>
    public static int StageRank(string? stage) => stage switch
    {
        "adoption" => 0,
        "consideration" => 1,
        "presentation" => 2,
        _ => 3,
    };

    /// <summary>The rows of one day in the agenda's order: known bills first, by activity, stage and number.</summary>
    public static IReadOnlyList<T> WithinDay<T>(
        IEnumerable<T> rows, Func<T, bool> known, Func<T, int> activity, Func<T, string?> stage, Func<T, string> number)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows
            .OrderByDescending(known)
            .ThenByDescending(activity)
            .ThenBy(r => StageRank(stage(r)))
            .ThenBy(number, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The bills of a timeline's rows (the front page's, the agenda page's), each once, that its members' islands ask about:
    /// every row shown, in the order shown. The front page shows <see cref="FrontDays"/> × <see cref="FrontPerDay"/> × 2 rows,
    /// one request, the agenda page <see cref="PagePerDay"/> a day; a day opened whole ("Rodyti likusius", owner 2026-11-18)
    /// adds its rows, asked in requests of
    /// <see cref="Lt2.Api.Contracts.Me.MyBillsQuery.MaxNumbers"/> (<see cref="MyAgendaState.Requests"/>). "Jūsų projektai"
    /// asks about the member's other followed bills itself.
    /// </summary>
    public static IReadOnlyList<string> Asked(IEnumerable<string> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        return numbers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// A day's rows, already in the order of <see cref="WithinDay{T}"/>, cut for a page: the first <paramref name="shown"/>
    /// known ones, the other known ones, and the unknown ones apart.
    /// </summary>
    public static AgendaDayView<T> Day<T>(IReadOnlyList<T> ordered, Func<T, bool> known, int shown)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        var knownRows = ordered.Where(known).ToList();
        return new AgendaDayView<T>(knownRows.Take(shown).ToList(), knownRows.Skip(shown).ToList(), ordered.Where(r => !known(r)).ToList());
    }
}
