using System.Globalization;

namespace Lt2.Api.Contracts.Parliament;

/// <summary>
/// Design 8.18: the parameters of <c>GET /api/v1/votes?q=&amp;kind=&amp;until=&amp;limit=&amp;cursor=</c>, read the same way
/// by the endpoint and the page. A value the API would not make falls back to its default, never to an error: an unknown
/// kind to the stage results, a malformed date to none, a limit out of range to the nearest one allowed.
/// </summary>
public static class VoteListQuery
{
    /// <summary>The stage results on bills, what the start page's "Balsuota" shows (the default).</summary>
    public const string StageResults = "stage-results";

    /// <summary>The stage results of adoption.</summary>
    public const string Adoptions = "adoptions";

    /// <summary>Every vote: also amendments, procedural and other votes, with or without a bill.</summary>
    public const string All = "all";

    public const int DefaultLimit = 30;

    /// <summary>The most one answer holds, as many as the page shows at once.</summary>
    public const int MaxLimit = 300;

    /// <summary>A longer search is not one the page's field makes; it is ignored.</summary>
    public const int MaxQueryLength = 100;

    public static string Kind(string? kind) => kind is Adoptions or All ? kind : StageResults;

    /// <summary>The search trimmed; null when blank or longer than <see cref="MaxQueryLength"/>.</summary>
    public static string? Query(string? q) =>
        string.IsNullOrWhiteSpace(q) || q.Trim().Length > MaxQueryLength ? null : q.Trim();

    /// <summary>A <c>yyyy-MM-dd</c> day; null for anything else.</summary>
    public static DateOnly? Until(string? until) =>
        DateOnly.TryParseExact(until?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    /// <summary>A whole number from 1 up, at most <see cref="MaxLimit"/>; <see cref="DefaultLimit"/> for anything else.</summary>
    public static int Limit(string? limit) =>
        int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 ? Math.Min(n, MaxLimit) : DefaultLimit;
}
