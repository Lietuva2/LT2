using System.Globalization;
using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Client.Parliament;

/// <summary>One Vilnius day of an MP's votes, newest first, each with its place in the list (1 for the first).</summary>
public sealed record MpVoteDay(DateOnly Day, IReadOnlyList<(int Number, MpVoteResponse Vote)> Rows);

/// <summary>
/// An MP's votes, <c>/p/{slug}/balsavimai</c> (owner, 2026-11-18), as the history of votes grows (design 8.18): by day, the
/// first <see cref="First"/>, then "Rodyti daugiau" adds <see cref="Step"/> in place by the address (<c>?rodyti=</c>), up to <see cref="Cap"/> on one page; after that "Ankstesni balsavimai" starts the next page where
/// this one ends (<c>?nuo=</c>). A value the page would not make falls back to the default, never to an error.
/// </summary>
public static class MpVotePaging
{
    public const int First = 50;

    /// <summary>The votes "Rodyti daugiau" adds (owner, 2026-11-19: a list of rows, 50 a press).</summary>
    public const int Step = Lt2.Api.Contracts.ListPaging.RowStep;

    /// <summary>The most one page shows (the API's <c>limit</c> at most).</summary>
    public const int Cap = MpVoteLimits.Max;

    /// <summary>How many the page shows: <c>rodyti</c> when it is a whole number from <see cref="First"/> to <see cref="Cap"/>.</summary>
    public static int Shown(string? show) =>
        int.TryParse(show, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= First && n <= Cap ? n : First;

    /// <summary>"Rodyti daugiau": the next value of <c>rodyti</c>; null when the API has no more or the page is full.</summary>
    public static int? More(int shown, bool hasMore) => hasMore && shown < Cap ? Lt2.Api.Contracts.ListPaging.Next(shown, Step, Cap) : null;

    /// <summary>"Ankstesni balsavimai": the next page's cursor once this one is full and the API has more.</summary>
    public static string? Older(int shown, string? nextCursor) => shown >= Cap ? nextCursor : null;

    /// <summary>The id of the <paramref name="number"/>-th row.</summary>
    public static string Row(int number) => "vote-" + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>The votes by Vilnius day, newest day first, each day once (the votes come newest first).</summary>
    public static IReadOnlyList<MpVoteDay> Days(IReadOnlyList<MpVoteResponse> votes)
    {
        ArgumentNullException.ThrowIfNull(votes);
        return votes
            .Select((v, i) => (Number: i + 1, Vote: v))
            .GroupBy(r => Display.DayOf(r.Vote.Vote.HeldAt))
            .Select(g => new MpVoteDay(g.Key, g.ToList()))
            .ToList();
    }
}
