using System.Globalization;
using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Client.Parliament;

/// <param name="Query">The search as used (trimmed), null when none.</param>
/// <param name="Kind">The word of the votes chosen in the address (<c>etapai</c>, <c>priemimai</c>, <c>visi</c>).</param>
/// <param name="Until">The last day shown, null for "up to the latest vote".</param>
/// <param name="Shown">How many votes to show.</param>
public sealed record VoteHistoryState(string? Query, string Kind, DateOnly? Until, int Shown)
{
    /// <summary>The kind as the API names it.</summary>
    public string ApiKind => VoteHistory.Kinds.First(k => k.Slug == Kind).ApiKind;

    /// <summary>Anything chosen in the form (a search, a kind other than the default, a day).</summary>
    public bool Filtered => Query is not null || Kind != VoteHistory.DefaultKind || Until is not null;
}

/// <summary>One row of the list: its place (1 for the first) and the vote.</summary>
public sealed record VoteHistoryRow(int Number, VoteListItem Vote);

/// <summary>One Vilnius day of the list with its votes, latest first.</summary>
public sealed record VoteHistoryDay(DateOnly Day, IReadOnlyList<VoteHistoryRow> Rows);

/// <summary>
/// Design 8.18: the history of the Seimas's votes, <c>/seimas/balsavimai</c>. The state is the page's address
/// (<c>?q=</c>, <c>?kas=</c>, <c>?iki=</c>, <c>?rodyti=</c>); a value the page would not make falls back to the default,
/// never to an error. The votes come from the API a part at a time; this decides what to ask for, how the rows fall into
/// days, and where "Rodyti daugiau" and "Senesni balsavimai" lead.
/// </summary>
public static class VoteHistory
{
    public const string Path = "/seimas/balsavimai";

    /// <summary>The votes shown first.</summary>
    public const int First = 30;

    /// <summary>The votes "Rodyti daugiau" adds (owner, 2026-11-19: a list of rows, 50 a press; the last before the cap fewer).</summary>
    public const int Step = Lt2.Api.Contracts.ListPaging.RowStep;

    /// <summary>The most one page shows; after it the link is "Senesni balsavimai".</summary>
    public const int Cap = VoteListQuery.MaxLimit;

    public const string DefaultKind = "etapai";

    /// <summary>The id of the list's sheet, where "Senesni balsavimai" lands.</summary>
    public const string ListId = "balsavimai";

    /// <summary>The choices of the dropdown in its order, with their words in the address and the API's kinds.</summary>
    public static IReadOnlyList<(string Slug, string ApiKind)> Kinds { get; } =
        [(DefaultKind, VoteListQuery.StageResults), ("priemimai", VoteListQuery.Adoptions), ("visi", VoteListQuery.All)];

    /// <summary>The state from the address; anything the page would not make is the default.</summary>
    public static VoteHistoryState Read(string? q, string? kas, string? iki, string? rodyti)
    {
        var kind = Kinds.Any(k => k.Slug == kas) ? kas! : DefaultKind;
        var shown = int.TryParse(rodyti, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= First && n <= Cap ? n : First;
        return new VoteHistoryState(VoteListQuery.Query(q), kind, VoteListQuery.Until(iki), shown);
    }

    /// <summary>The page's address with this state, landing on <paramref name="fragment"/>; defaults are left out.</summary>
    public static string Address(VoteHistoryState state, string? fragment = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var parts = new List<string>();
        if (state.Query is { } q)
        {
            parts.Add("q=" + Uri.EscapeDataString(q));
        }

        if (state.Kind != DefaultKind)
        {
            parts.Add("kas=" + state.Kind);
        }

        if (state.Until is { } until)
        {
            parts.Add("iki=" + Display.Day(until));
        }

        if (state.Shown != First)
        {
            parts.Add("rodyti=" + state.Shown.ToString(CultureInfo.InvariantCulture));
        }

        var address = parts.Count == 0 ? Path : Path + "?" + string.Join("&", parts);
        return fragment is null ? address : address + "#" + fragment;
    }

    /// <summary>The rows by Vilnius day, newest day first, each day once (the votes come newest first).</summary>
    public static IReadOnlyList<VoteHistoryDay> Days(IReadOnlyList<VoteListItem> votes)
    {
        ArgumentNullException.ThrowIfNull(votes);
        var days = new List<VoteHistoryDay>();
        List<VoteHistoryRow>? rows = null;
        DateOnly? current = null;
        for (var i = 0; i < votes.Count; i++)
        {
            var day = Display.DayOf(votes[i].Summary.Vote.HeldAt);
            if (day != current)
            {
                rows = [];
                days.Add(new VoteHistoryDay(day, rows));
                current = day;
            }

            rows!.Add(new VoteHistoryRow(i + 1, votes[i]));
        }

        return days;
    }

    /// <summary>
    /// "Rodyti daugiau": the same list with <see cref="Step"/> more (at most 300), landing on the first row added; null when every vote found is shown
    /// or the page holds as many as it may.
    /// </summary>
    public static string? More(VoteHistoryState state, int shown, int total)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (shown >= total || shown < state.Shown || state.Shown >= Cap)
        {
            return null;
        }

        return Address(state with { Shown = Lt2.Api.Contracts.ListPaging.Next(state.Shown, Step, Cap) }, Row(shown + 1));
    }

    /// <summary>
    /// "Senesni balsavimai": once the page holds 300 and more are found, the same search from the day of the last vote
    /// shown ("Iki" set to it), from the first 30 again. When all 300 fall on one day, it starts from the day before, or
    /// it would show the same votes again.
    /// </summary>
    public static string? Older(VoteHistoryState state, IReadOnlyList<VoteListItem> shown, int total)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(shown);
        if (state.Shown < Cap || shown.Count < Cap || total <= shown.Count)
        {
            return null;
        }

        var first = Display.DayOf(shown[0].Summary.Vote.HeldAt);
        var last = Display.DayOf(shown[^1].Summary.Vote.HeldAt);
        return Address(state with { Until = last == first ? last.AddDays(-1) : last, Shown = First }, ListId);
    }

    /// <summary>The id of the <paramref name="number"/>-th row, where "Rodyti daugiau" lands.</summary>
    public static string Row(int number) => "balsavimas-" + number.ToString(CultureInfo.InvariantCulture);
}
