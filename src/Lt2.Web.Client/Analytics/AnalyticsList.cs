using System.Globalization;
using Lt2.Api.Contracts.Analytics;

namespace Lt2.Web.Client.Analytics;

/// <summary>
/// The state of <c>/statistika</c> from its address (design 8.26.1). The period and the accounts counted (<c>metai</c>,
/// <c>paskyros</c>) apply to the whole page; the list of closed bills has its own filters (<c>nuo</c>, <c>iki</c>,
/// <c>q</c>, <c>rezultatas</c>, <c>dauguma</c>, <c>rikiuoti</c>) and length (<c>rodyti</c>).
/// </summary>
/// <param name="Year">The year asked for (the API decides whether it is one of the term); null for the whole term.</param>
/// <param name="Older">Only accounts older than 30 days.</param>
/// <param name="From">The first day of a closing vote, as typed (shown again in its field, even when it is not a date).</param>
/// <param name="To">The last such day, as typed.</param>
/// <param name="Query">The search, trimmed; null when none.</param>
/// <param name="Result">The word of the result in the address (<c>sutapo</c>, <c>skyresi</c>); empty for every bill.</param>
/// <param name="Majority">The word of the Seimas's majority (<c>uz</c>, <c>pries</c>, <c>nera</c>); empty for every bill.</param>
/// <param name="Sort">The word of the order (<c>skirtumas</c>, <c>pozicijos</c>); empty for the newest first.</param>
/// <param name="Shown">How many bills the list shows.</param>
public sealed record AnalyticsListState(
    int? Year, bool Older, string? From, string? To, string? Query, string Result, string Majority, string Sort, int Shown)
{
    /// <summary>The first day as a date; null when blank or not a <c>yyyy-MM-dd</c> date.</summary>
    public DateOnly? FromDay => AnalyticsBillQuery.Day(From);

    /// <summary>The last day as a date; null when blank or not a <c>yyyy-MM-dd</c> date.</summary>
    public DateOnly? ToDay => AnalyticsBillQuery.Day(To);

    /// <summary>A day was typed that is not a <c>yyyy-MM-dd</c> date: it is not used, and its field says so.</summary>
    public bool FromInvalid => !string.IsNullOrWhiteSpace(From) && FromDay is null;

    /// <summary>The last day typed is not a date.</summary>
    public bool ToInvalid => !string.IsNullOrWhiteSpace(To) && ToDay is null;

    /// <summary>Both days are dates, but the range ends before it starts: neither is used, and the page says so.</summary>
    public bool Reversed => FromDay is { } from && ToDay is { } to && to < from;

    /// <summary>Anything chosen in the list's form.</summary>
    public bool Filtered => Active > 0;

    /// <summary>
    /// How many of the list's fields are set (a day typed, even one that is not a date; the search; the result; the majority;
    /// an order other than the newest first): the count in "Filtrai (2)", whose form is open when it is not 0.
    /// </summary>
    public int Active =>
        new[] { !string.IsNullOrWhiteSpace(From), !string.IsNullOrWhiteSpace(To), Query is not null, Result != "", Majority != "", Sort != "" }.Count(set => set);
}

/// <summary>
/// Design 8.26.1 item 2 (owner, 2026-11-18): the list of closed bills on <c>/statistika</c>. The first <see cref="First"/>
/// bills, then "Rodyti daugiau" adds <see cref="Step"/> in place by the address (<c>?rodyti=</c>), up to <see cref="Cap"/>.
/// The filters narrow a longer list. Unknown values fall back to the defaults. The words in the address are Lithuanian; the API's are its own.
/// </summary>
public static class AnalyticsList
{
    public const string Path = "/statistika";

    /// <summary>The id of the comparison section.</summary>
    public const string Anchor = "comparison";

    public const int First = 10;

    /// <summary>The bills "Rodyti daugiau" adds (owner, 2026-11-19: a list of rows, 50 a press; the last before the cap fewer).</summary>
    public const int Step = Lt2.Api.Contracts.ListPaging.RowStep;

    public const int Cap = AnalyticsBillQuery.MaxLimit;

    /// <summary>The value of <c>paskyros</c> that counts only accounts older than 30 days.</summary>
    public const string OlderValue = "senesnes";

    /// <summary>The result's choices after "every bill", with their words in the address and the API's values.</summary>
    public static IReadOnlyList<(string Slug, string ApiValue)> Results { get; } =
        [("sutapo", AnalyticsBillQuery.Agreed), ("skyresi", AnalyticsBillQuery.Differed)];

    /// <summary>The Seimas's majority's choices after "every bill" (design 8.26.2's three), in that order.</summary>
    public static IReadOnlyList<(string Slug, string ApiValue)> Majorities { get; } =
        [("uz", "for"), ("pries", "against"), ("nera", "none")];

    /// <summary>The orders after the default (the newest closing vote first).</summary>
    public static IReadOnlyList<(string Slug, string ApiValue)> Sorts { get; } =
        [("skirtumas", AnalyticsBillQuery.Difference), ("pozicijos", AnalyticsBillQuery.Positions)];

    /// <summary>The state from the address; anything the page would not make is the default.</summary>
    public static AnalyticsListState Read(
        string? yearText, string? accounts, string? from, string? to, string? q, string? result, string? majority, string? sort, string? show)
    {
        int? year = int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : null;
        var query = string.IsNullOrWhiteSpace(q) || q.Trim().Length > AnalyticsBillQuery.MaxSearchLength ? null : q.Trim();
        var shown = int.TryParse(show, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= First && n <= Cap ? n : First;
        return new AnalyticsListState(
            year,
            string.Equals(accounts, OlderValue, StringComparison.Ordinal),
            string.IsNullOrWhiteSpace(from) ? null : from.Trim(),
            string.IsNullOrWhiteSpace(to) ? null : to.Trim(),
            query,
            Word(Results, result),
            Word(Majorities, majority),
            Word(Sorts, sort),
            shown);
    }

    /// <summary>
    /// What to ask the API for: the list's filters and how many bills to show.
    /// </summary>
    public static AnalyticsBillQuery Query(AnalyticsListState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var (from, to) = AnalyticsBillQuery.Days(state.FromDay, state.ToDay);
        return new AnalyticsBillQuery(
            from,
            to,
            state.Query,
            Api(Results, state.Result),
            Api(Majorities, state.Majority),
            Api(Sorts, state.Sort) ?? AnalyticsBillQuery.Latest,
            state.Shown);
    }

    /// <summary>
    /// The page's address with this state. Defaults are left out, and a day is
    /// written only when it is a date (one that is not was ignored and said so; the next address forgets it).
    /// </summary>
    public static string Address(AnalyticsListState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var parts = new List<string>();
        if (state.Year is { } year)
        {
            parts.Add("metai=" + year.ToString(CultureInfo.InvariantCulture));
        }

        if (state.Older)
        {
            parts.Add("paskyros=" + OlderValue);
        }

        var (from, to) = AnalyticsBillQuery.Days(state.FromDay, state.ToDay);
        if (from is { } f)
        {
            parts.Add("nuo=" + Display.Day(f));
        }

        if (to is { } t)
        {
            parts.Add("iki=" + Display.Day(t));
        }

        if (state.Query is { } q)
        {
            parts.Add("q=" + Uri.EscapeDataString(q));
        }

        foreach (var (name, value) in new[] { ("rezultatas", state.Result), ("dauguma", state.Majority), ("rikiuoti", state.Sort) })
        {
            if (value != "")
            {
                parts.Add(name + "=" + value);
            }
        }

        if (state.Shown != First)
        {
            parts.Add("rodyti=" + state.Shown.ToString(CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? Path : Path + "?" + string.Join("&", parts);
    }

    /// <summary>The page with the period and the accounts kept and the list's filters cleared ("Išvalyti").</summary>
    public static string Cleared(AnalyticsListState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Address(new AnalyticsListState(state.Year, state.Older, null, null, null, "", "", "", First));
    }

    /// <summary>The same plot and table with <see cref="Step"/> more, up to <see cref="Cap"/> or the total found.</summary>
    public static string? More(AnalyticsListState state, int found)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Shown >= Cap || found <= state.Shown)
        {
            return null;
        }

        return Address(state with { Shown = Lt2.Api.Contracts.ListPaging.Next(state.Shown, Step, Cap) });
    }

    /// <summary>The id of a bill's row in the plot.</summary>
    public static string Row(string number) => "bill-" + number;

    private static string Word(IReadOnlyList<(string Slug, string ApiValue)> choices, string? word) =>
        choices.Any(c => c.Slug == word) ? word! : "";

    private static string? Api(IReadOnlyList<(string Slug, string ApiValue)> choices, string slug) =>
        choices.FirstOrDefault(c => c.Slug == slug).ApiValue;
}
