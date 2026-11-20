using System.Globalization;
using Lt2.Api.Contracts.Appeals;

namespace Lt2.Web.Client.Appeals;

/// <summary>
/// The arguments a page at a time (design 8.12.1, owner 2026-11-19). Each side is a column: the bill's answer brings its first
/// <see cref="ReasonLimits.First"/>, and "Rodyti daugiau argumentų" under the column adds <see cref="ReasonLimits.Step"/> more
/// from the API, in place, or without script by the page's address (<c>?argumentai-uz=22</c>, <see cref="ReasonLimits.Parameter"/>).
/// A column is kept as a <see cref="ReasonPageResponse"/>: its <c>Items</c> in the order shown, its side's <c>Total</c>, and its
/// <c>NextCursor</c>, where the next page of the server's order starts. An argument shown out of that order (a linked one, one just
/// published, a new one under "Rodyti tik naujus") is "pinned": it stands where it was put and a later page that brings it again
/// adds nothing twice.
/// </summary>
public static class ReasonPaging
{
    /// <summary>The most one address asks a side to show (a value beyond it shows this many).</summary>
    public const int Cap = 1000;

    /// <summary>How many of a side's arguments the address asks for: a whole number from the first two up to <see cref="Cap"/>, else the first two.</summary>
    public static int Asked(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > ReasonLimits.First ? Math.Min(n, Cap) : ReasonLimits.First;

    /// <summary>A column from the bill's answer: its first arguments of the side, the side's count, and where the next page starts.</summary>
    public static ReasonPageResponse FromRecord(BillRecordResponse record, string side)
    {
        ArgumentNullException.ThrowIfNull(record);
        var items = record.Reasons.Where(r => r.Side == side).ToList();
        var total = Math.Max(record.ReasonTotals?.Of(side) ?? items.Count, items.Count);
        return new ReasonPageResponse(side, items, total, Next(items.Count, total), record.Cards ?? []);
    }

    /// <summary>How many of the server's order the column has read: where its next page starts.</summary>
    public static int Read(ReasonPageResponse column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return int.TryParse(column.NextCursor, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : column.Total;
    }

    /// <summary>Whether "Rodyti daugiau argumentų" stands under the column: arguments of the side are still not shown.</summary>
    public static bool More(ReasonPageResponse column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column.NextCursor is not null && column.Items.Count < column.Total;
    }

    /// <summary>The column with the next page added after what it shows; an argument it shows already is not added twice.</summary>
    public static ReasonPageResponse Append(ReasonPageResponse column, ReasonPageResponse page)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(page);
        var shown = column.Items.Select(r => r.Id).ToHashSet();
        return column with
        {
            Items = [.. column.Items, .. page.Items.Where(r => shown.Add(r.Id))],
            Total = page.Total,
            NextCursor = page.NextCursor,
            Cards = Cards(column.Cards, page.Cards),
        };
    }

    /// <summary>
    /// The column read again (after the person's own action, owner 2026-11-17: a mark moves nothing on the page): what it shows
    /// keeps its place with the fresh numbers; an argument gone from the fresh pages goes, unless it is pinned (it stands outside
    /// them); one new to the column comes last. <paramref name="fresh"/> is the side's order from the first argument through as
    /// many as the column had read.
    /// </summary>
    public static ReasonPageResponse Refresh(ReasonPageResponse column, ReasonPageResponse fresh, IReadOnlySet<Guid> pinned)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(pinned);
        var byId = fresh.Items.ToDictionary(r => r.Id);
        var kept = column.Items
            .Where(r => byId.ContainsKey(r.Id) || pinned.Contains(r.Id))
            .Select(r => byId.GetValueOrDefault(r.Id) ?? r)
            .ToList();
        var shown = kept.Select(r => r.Id).ToHashSet();
        return column with
        {
            Items = [.. kept, .. fresh.Items.Where(r => shown.Add(r.Id))],
            Total = fresh.Total,
            NextCursor = fresh.NextCursor,
            Cards = Cards(column.Cards, fresh.Cards),
        };
    }

    /// <summary>
    /// The column with one argument first (the one just published, owner 2026-11-17; a linked one, design 8.12.1): moved there
    /// when the column shows it, put there otherwise. With <paramref name="first"/> false it is only added, last, when not shown
    /// (a new one under "Rodyti tik naujus").
    /// </summary>
    public static ReasonPageResponse Pin(ReasonPageResponse column, ReasonResponse reason, IReadOnlyList<AuthorCardResponse>? cards = null, bool first = true)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(reason);
        var others = column.Items.Where(r => r.Id != reason.Id).ToList();
        var items = first ? [reason, .. others]
            : others.Count < column.Items.Count ? column.Items.Select(r => r.Id == reason.Id ? reason : r).ToList()
            : [.. others, reason];
        return column with { Items = items, Total = Math.Max(column.Total, items.Count), Cards = Cards(column.Cards, cards) };
    }

    /// <summary>The column without one argument (withdrawn, or gone from the public pages).</summary>
    public static ReasonPageResponse Without(ReasonPageResponse column, Guid reasonId)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column.Items.Any(r => r.Id == reasonId) ? column with { Items = column.Items.Where(r => r.Id != reasonId).ToList() } : column;
    }

    /// <summary>The address's next value for the side: as many as it shows now and a <see cref="ReasonLimits.Step"/> more of the server's order.</summary>
    public static int NextAsked(ReasonPageResponse column) => Math.Min(Read(column) + ReasonLimits.Step, Cap);

    /// <summary>
    /// The anchor of the <paramref name="position"/>-th argument shown in a side's column (1-based): "Rodyti daugiau argumentų"
    /// without script lands on the first one added, whose own id the link cannot know (as the comments do, design 8.23.14).
    /// </summary>
    public static string PositionAnchor(string side, int position) =>
        "argumentai-" + (side == Sides.For ? "uz" : "pries") + "-" + position.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The page's own address with the side's count set to <paramref name="count"/> and every other parameter kept, landing on
    /// <paramref name="anchor"/>: a path from the site's root (the pages carry <c>&lt;base href="/"&gt;</c>), never a bare fragment.
    /// <paramref name="absoluteWithCount"/> is the page's absolute address with the count already set; <paramref name="baseUri"/> the site's.
    /// </summary>
    public static string Address(string absoluteWithCount, string baseUri, string anchor)
    {
        ArgumentNullException.ThrowIfNull(absoluteWithCount);
        ArgumentNullException.ThrowIfNull(baseUri);
        var withoutFragment = absoluteWithCount.Split('#')[0];
        var relative = withoutFragment.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase) ? withoutFragment[baseUri.Length..] : withoutFragment;
        return "/" + relative.TrimStart('/') + "#" + anchor;
    }

    private static string? Next(int read, int total) => read < total ? read.ToString(CultureInfo.InvariantCulture) : null;

    private static List<AuthorCardResponse> Cards(IReadOnlyList<AuthorCardResponse>? shown, IReadOnlyList<AuthorCardResponse>? more) =>
        (shown ?? []).Concat(more ?? []).DistinctBy(c => c.ProfileId).ToList();
}
