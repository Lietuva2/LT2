using System.Globalization;

namespace Lt2.Web.Client.Appeals;

/// <summary>
/// How <c>/projektai</c> grows (design 8.12.5, owner 2026-11-18), as the history of votes does (8.18): the first
/// <see cref="First"/> bills, then "Rodyti daugiau" adds <see cref="Step"/> in place by the address (<c>?rodyti=</c>),
/// landing on the first bill added, up to <see cref="Cap"/> on one page; after that "Tolesni projektai" starts the next page
/// there (<c>?nuo=</c>). A value the page would not make falls back to the default, never to an error.
/// </summary>
public static class BillListPaging
{
    public const int First = 20;

    /// <summary>The bills "Rodyti daugiau" adds (owner, 2026-11-19: a list of rows, 50 a press).</summary>
    public const int Step = Lt2.Api.Contracts.ListPaging.RowStep;

    /// <summary>The most one page shows (the API's <c>limit</c> at most).</summary>
    public const int Cap = Lt2.Api.Contracts.Appeals.BillListLimits.Max;

    /// <summary>How many the page shows: <c>rodyti</c> when it is a whole number from <see cref="First"/> to <see cref="Cap"/>.</summary>
    public static int Shown(string? show) =>
        int.TryParse(show, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= First && n <= Cap ? n : First;

    /// <summary>Where the page starts: <c>nuo</c> when it is a whole number, else the first bill.</summary>
    public static int Offset(string? from) => int.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>How many to ask for: one more than shown while the page may grow, so "Rodyti daugiau" can land on the next bill.</summary>
    public static int Asked(int shown) => shown < Cap ? shown + 1 : shown;

    /// <summary>"Rodyti daugiau": the next value of <c>rodyti</c>, null when every bill found is shown or the page is full.</summary>
    public static int? More(int shown, int offset, int total) =>
        shown < Cap && offset + shown < total ? Lt2.Api.Contracts.ListPaging.Next(shown, Step, Cap) : null;

    /// <summary>"Tolesni projektai": the next page's <c>nuo</c> once this one holds <see cref="Cap"/> and more are found.</summary>
    public static int? Further(int shown, int offset, int total) => shown >= Cap && offset + shown < total ? offset + shown : null;
}
