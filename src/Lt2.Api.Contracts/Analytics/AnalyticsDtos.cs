using System.Globalization;

namespace Lt2.Api.Contracts.Analytics;

// Design 8.26: the analytics page. Shares are fractions (0–1), null where the rule shows none (a sample under 20, a vote
// by consensus, nothing to count). Choices and positions use the parliament codes: for | against | abstain | free.

/// <summary>Design 8.26.6: the routes of the analytics page.</summary>
public static class AnalyticsRoutes
{
    public const string Analytics = ApiRoutes.Prefix + "/analytics";

    /// <summary>The caller's own comparison (design 8.26.4).</summary>
    public const string MyAgreement = ApiRoutes.Prefix + "/me/analytics/agreement";

    /// <summary>The value of <c>accounts</c> that counts only accounts older than 30 days.</summary>
    public const string OlderAccounts = "older";

    /// <summary>
    /// <c>GET /analytics</c> for a year of the term (null: the whole term), the accounts counted and the list's filters
    /// (none: every closed bill of the period, newest first, the first <see cref="AnalyticsBillQuery.DefaultLimit"/>).
    /// </summary>
    public static string Of(int? year, bool olderAccounts, AnalyticsBillQuery? bills = null)
    {
        var query = new List<string>();
        if (year is { } y)
        {
            query.Add("year=" + y.ToString(CultureInfo.InvariantCulture));
        }

        if (olderAccounts)
        {
            query.Add("accounts=" + OlderAccounts);
        }

        if (bills is not null)
        {
            query.AddRange(bills.Parameters());
        }

        return query.Count == 0 ? Analytics : Analytics + "?" + string.Join("&", query);
    }
}

/// <summary>
/// Design 8.26.1 item 2 (owner, 2026-11-18): the filters, order and length of the list of closed bills, as the API reads
/// them (<c>from</c>, <c>to</c>, <c>q</c>, <c>result</c>, <c>majority</c>, <c>sort</c>, <c>limit</c>). They apply to the
/// list only: the four figures and faction unity follow the period and the accounts counted. A value the API would not
/// make falls back to its default, never to an error: a malformed day to none, a range that ends before it starts to no
/// days at all, an unknown choice to every bill or the newest first, a limit out of range to the nearest one allowed.
/// </summary>
/// <param name="From">The first Vilnius day of a closing vote listed; null: from the period's start.</param>
/// <param name="To">The last such day; null: to the period's end.</param>
/// <param name="Search">Every word in the bill's number or title, ignoring case; null for none.</param>
/// <param name="Result"><see cref="Agreed"/> | <see cref="Differed"/> (only compared bills have either); null: every bill.</param>
/// <param name="Majority">The Seimas's majority of those who voted, <c>for</c> | <c>against</c> | <c>none</c>; null: every bill.</param>
/// <param name="Sort"><see cref="Latest"/> (the default) | <see cref="Difference"/> | <see cref="Positions"/>.</param>
/// <param name="Limit">How many of the bills found the answer holds, from 1 to <see cref="MaxLimit"/>.</param>
public sealed record AnalyticsBillQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    string? Result = null,
    string? Majority = null,
    string Sort = AnalyticsBillQuery.Latest,
    int Limit = AnalyticsBillQuery.DefaultLimit)
{
    public const string Agreed = "agreed";
    public const string Differed = "differed";
    public const string Latest = "latest";
    public const string Difference = "difference";
    public const string Positions = "positions";
    public const int DefaultLimit = 10;

    /// <summary>The most one answer holds (as the other long lists, 8.18).</summary>
    public const int MaxLimit = 300;

    /// <summary>A longer search is not one the page's field makes; it is ignored.</summary>
    public const int MaxSearchLength = 100;

    private static readonly string[] Majorities = ["for", "against", "none"];

    /// <summary>The query from the API's parameters; anything the API would not make is the default.</summary>
    public static AnalyticsBillQuery Read(string? from, string? to, string? q, string? result, string? majority, string? sort, string? limit)
    {
        var (first, last) = Days(Day(from), Day(to));
        return new AnalyticsBillQuery(
            first,
            last,
            string.IsNullOrWhiteSpace(q) || q.Trim().Length > MaxSearchLength ? null : q.Trim(),
            result is Agreed or Differed ? result : null,
            Majorities.Contains(majority) ? majority : null,
            sort is Difference or Positions ? sort : Latest,
            int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 ? Math.Min(n, MaxLimit) : DefaultLimit);
    }

    /// <summary>A <c>yyyy-MM-dd</c> day; null for anything else.</summary>
    public static DateOnly? Day(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    /// <summary>
    /// The list's typed days: a range that ends before it starts is not used at all (both null), so the list never comes
    /// out empty only because the two fields were swapped; the page says so.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) Days(DateOnly? from, DateOnly? to) =>
        from is { } f && to is { } t && t < f ? (null, null) : (from, to);

    /// <summary>This query as the API's parameters, defaults left out.</summary>
    public IEnumerable<string> Parameters()
    {
        if (From is { } from)
        {
            yield return "from=" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (To is { } to)
        {
            yield return "to=" + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (Search is { } q)
        {
            yield return "q=" + Uri.EscapeDataString(q);
        }

        if (Result is { } result)
        {
            yield return "result=" + result;
        }

        if (Majority is { } majority)
        {
            yield return "majority=" + majority;
        }

        if (Sort != Latest)
        {
            yield return "sort=" + Sort;
        }

        if (Limit != DefaultLimit)
        {
            yield return "limit=" + Limit.ToString(CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>
/// Design 8.26.1: the public analytics, the same for everyone. <paramref name="Year"/> is the year shown, null for the whole
/// term; <paramref name="Years"/> are the years the period may be (newest first). <paramref name="SmallSample"/> is the
/// sample under which no share is shown (20). <paramref name="CountedAt"/>: when these numbers were counted (they are kept
/// for up to an hour, design 8.26.6). <paramref name="BillsQuery"/> is the list's query as applied;
/// <paramref name="BillsClosed"/> the closed bills of the period with at least one position, <paramref name="BillsFound"/>
/// those of them the list's filters match, and <paramref name="Bills"/> the first of those in the list's order (at most the
/// query's limit). The figures and the factions do not follow the list's filters.
/// </summary>
public sealed record AnalyticsResponse(
    string TermName,
    IReadOnlyList<int> Years,
    int? Year,
    bool OlderAccounts,
    int SmallSample,
    DateTimeOffset CountedAt,
    AnalyticsFiguresResponse Figures,
    AnalyticsBillQuery BillsQuery,
    int BillsClosed,
    int BillsFound,
    IReadOnlyList<BillComparisonResponse> Bills,
    IReadOnlyList<FactionUnityResponse> Factions);

/// <summary>The four figures (design 8.26.1). <paramref name="AgreedShare"/> only from 20 compared bills.</summary>
public sealed record AnalyticsFiguresResponse(
    int BillsTakenPart, int BillsDiscussed, int Positions, int Agreed, int Compared, decimal? AgreedShare, int MpsTakenOver, int MpsSitting);

/// <summary>
/// A closed bill of the period with at least one position (design 8.26.1–2). <paramref name="MembersShareFor"/> only from
/// 20 positions; <paramref name="Difference"/>: the difference of the two shares "for" (0–1), only when both are shown;
/// <paramref name="OfficialNote"/>: the Seimas's own note on the result (for example that per-MP votes do not match the totals).
/// <paramref name="SeimasMajority"/>: the majority of those who voted, <c>for</c> (a consensus too) | <c>against</c> | <c>none</c>
/// (neither has more than half of for, against and abstain); null for a vote without numbers.
/// </summary>
public sealed record BillComparisonResponse(
    string Number,
    string Title,
    long VoteId,
    DateTimeOffset VotedAt,
    bool IsConsensus,
    decimal? SeimasShareFor,
    string? SeimasMajority,
    int MembersFor,
    int MembersAgainst,
    decimal? MembersShareFor,
    bool Compared,
    bool? Agreed,
    decimal? Difference,
    string? OfficialNote);

/// <summary>
/// A faction's unity over the period (design 8.26.3). <paramref name="SmallSample"/>: fewer than 20 votes with a position, and
/// then <paramref name="Share"/> is null (only the counts are shown, with "maža imtis").
/// </summary>
public sealed record FactionUnityResponse(
    string Slug, string Name, int Seats, int VotesWithPosition, int FreeVotes, decimal? Share, bool SmallSample, IReadOnlyList<SplitVoteResponse> MostSplit);

/// <summary>A vote where the faction split most: the bill (if any), the title to show, its position and its members' counts.</summary>
public sealed record SplitVoteResponse(
    long VoteId, DateTimeOffset HeldAt, string? BillNumber, string Title, string? Stage, string Position, int For, int Against, int Abstain);

/// <summary>
/// Design 8.26.4: the caller's own comparison. <paramref name="Bills"/>: closed bills the caller took a side on;
/// <paramref name="Consensus"/>: of them, adopted by consensus (no ballots). Rows in the caller's order.
/// </summary>
public sealed record MyAgreementResponse(int Bills, int Consensus, int SmallSample, IReadOnlyList<AgreementRowResponse> Factions, IReadOnlyList<AgreementRowResponse> Mps);

/// <summary>
/// A faction or an MP compared with the caller. For an MP, <paramref name="Faction"/> is today's (or the last one) and
/// <paramref name="Former"/> says the mandate ended; for a faction, <paramref name="Seats"/> are its members today.
/// <paramref name="Share"/> only from 20 common votes.
/// </summary>
public sealed record AgreementRowResponse(
    string Slug, string Name, string? Faction, int? Seats, bool Former, int Matched, int Common, int Free, decimal? Share);

/// <summary>The analytics page's API (design 8.26.6).</summary>
public interface IAnalyticsApi
{
    Task<ApiResult<AnalyticsResponse>> GetAsync(int? year, bool olderAccounts, AnalyticsBillQuery? bills = null, CancellationToken cancellationToken = default);

    Task<ApiResult<MyAgreementResponse>> GetMyAgreementAsync(CancellationToken cancellationToken = default);
}
