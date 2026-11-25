using Lt2.Api.Contracts.Parliament;
using Lt2.Api.Contracts;

namespace Lt2.Web.Client.Agenda;

/// <summary>
/// One sitting or voting day of a list as a page shows it (design 8.7, 8.11.1): the known rows shown, the known rows not
/// shown yet (the next pages, "Rodyti daugiau", AgendaPaging), the bills the open data does not know yet (folded into one
/// line after the last known row), and how many the day holds in all.
/// </summary>
public sealed record AgendaDayView<T>(IReadOnlyList<T> Shown, IReadOnlyList<T> Rest, IReadOnlyList<T> Unknown)
{
    /// <summary>Every row of the day, known or not: the "M" of "Rodoma N iš M", the same as the day's head.</summary>
    public int Count => Shown.Count + Rest.Count + Unknown.Count;

    /// <summary>The day's known rows, shown and the rest.</summary>
    public int Known => Shown.Count + Rest.Count;

    /// <summary>The line of bills the open data does not know yet stands in the page: every known row is shown.</summary>
    public bool UnknownShown => Rest.Count == 0 && Unknown.Count > 0;

    /// <summary>
    /// The "N" of "Rodoma N iš M" (owner, 2026-11-19): the known rows shown, and the bills the open data does not know yet
    /// once their line is shown (it comes after the last known row), so the last page reads "Rodoma 40 iš 40".
    /// </summary>
    public int ShownCount => Shown.Count + (Rest.Count == 0 ? Unknown.Count : 0);
}

/// <summary>
/// The order and the lengths of the agenda's lists (design 8.11.1, 8.7; owner, 2026-11-18). The agenda says nothing about
/// the order or the time within a sitting day, so the rows of a day are ordered by what people do on them: activity first
/// (participants on both sides together plus published arguments plus followers, the plain sum of <c>/projektai</c>, design
/// 8.12.5, which the Appeals module counts), then how far the bill may go that day (priėmimas, svarstymas, pateikimas,
/// none), then the bill's number. A bill the open data does not know yet comes after every known one. Nothing else ranks a
/// bill: not whether the reader follows it (the server's page is the same for everyone). The numbers are the design's, not
/// settings.
/// </summary>
public static class AgendaRules
{
    /// <summary>The front page shows the next this many sitting days, and the last this many voting days.</summary>
    public const int FrontDays = 2;

    /// <summary>The front page shows this many rows of each day.</summary>
    public const int FrontPerDay = 5;

    /// <summary>The agenda page shows this many rows of each day before "Rodyti daugiau".</summary>
    public const int PagePerDay = 10;

    /// <summary>
    /// "Rodyti daugiau" under a day adds this many rows, on both pages (owner, 2026-11-19: a page at a time, the first small and
    /// each further one 50 rows, <see cref="ListPaging.RowStep"/>).
    /// </summary>
    public const int MorePerDay = ListPaging.RowStep;

    /// <summary>The most known rows of a day an address asks for (a larger number shows this many).</summary>
    public const int MaxAsked = 1000;

    /// <summary>
    /// How many of a day's known rows a page shows (owner, 2026-11-19): its first <paramref name="first"/>, or as many as the
    /// address asks when that is more (a number at or below the first is not one the page makes, so it is left out), and
    /// never more than the day has.
    /// </summary>
    public static int ShownKnown(int first, int? asked, int known) =>
        Math.Max(0, Math.Min(asked is { } n && n > first ? Math.Min(n, MaxAsked) : first, known));

    /// <summary>
    /// What "Rodyti daugiau" under a day asks for: the rows shown and <see cref="MorePerDay"/> more; null when every known row
    /// is shown (no link: the line of unknown bills, if any, is in the page already).
    /// </summary>
    public static int? NextAsked(int shownKnown, int known) => shownKnown < known ? Math.Min(shownKnown + MorePerDay, MaxAsked) : null;

    /// <summary>
    /// Whether a day carries the "Rodoma N iš M" line: its known rows do not all fit on its first page (a day whose rows all
    /// fit has none). Once it grows it keeps the line, with no link on the last page.
    /// </summary>
    public static bool Paged(int first, int known) => known > first;

    /// <summary>The stage's place in a day: priėmimas first, then svarstymas, then pateikimas, then none.</summary>
    public static int StageRank(string? stage) => stage switch
    {
        BillStages.Adoption => 0,
        BillStages.Consideration => 1,
        BillStages.Presentation => 2,
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
    /// one request, the agenda page <see cref="PagePerDay"/> a day; a day grown by "Rodyti daugiau" (owner 2026-11-19) adds
    /// its rows, asked in requests of
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
    /// known ones (<see cref="ShownKnown"/>), the other known ones, and the unknown ones apart.
    /// </summary>
    public static AgendaDayView<T> Day<T>(IReadOnlyList<T> ordered, Func<T, bool> known, int shown)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        var knownRows = ordered.Where(known).ToList();
        return new AgendaDayView<T>(knownRows.Take(shown).ToList(), knownRows.Skip(shown).ToList(), ordered.Where(r => !known(r)).ToList());
    }
}
