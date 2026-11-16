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

    /// <summary><c>GET /analytics</c> for a year of the term (null: the whole term) and the accounts counted.</summary>
    public static string Of(int? year, bool olderAccounts)
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

        return query.Count == 0 ? Analytics : Analytics + "?" + string.Join("&", query);
    }
}

/// <summary>
/// Design 8.26.1: the public analytics, the same for everyone. <paramref name="Year"/> is the year shown, null for the whole
/// term; <paramref name="Years"/> are the years the period may be (newest first). <paramref name="SmallSample"/> is the
/// sample under which no share is shown (20). <paramref name="CountedAt"/>: when these numbers were counted (they are kept
/// for up to an hour, design 8.26.6).
/// </summary>
public sealed record AnalyticsResponse(
    string TermName,
    IReadOnlyList<int> Years,
    int? Year,
    bool OlderAccounts,
    int SmallSample,
    DateTimeOffset CountedAt,
    AnalyticsFiguresResponse Figures,
    IReadOnlyList<BillComparisonResponse> Bills,
    IReadOnlyList<FactionUnityResponse> Factions);

/// <summary>The four figures (design 8.26.1). <paramref name="AgreedShare"/> only from 20 compared bills.</summary>
public sealed record AnalyticsFiguresResponse(
    int BillsTakenPart, int BillsDiscussed, int Positions, int Agreed, int Compared, decimal? AgreedShare, int MpsTakenOver, int MpsSitting);

/// <summary>
/// A closed bill of the period with at least one position (design 8.26.1–2), the latest closing vote first.
/// <paramref name="MembersShareFor"/> only from 20 positions; <paramref name="Dot"/>: it stands in the plot;
/// <paramref name="OfficialNote"/>: the Seimas's own note on the result (for example that per-MP votes do not match the totals).
/// </summary>
public sealed record BillComparisonResponse(
    string Number,
    string Title,
    long VoteId,
    DateTimeOffset VotedAt,
    bool IsConsensus,
    decimal? SeimasShareFor,
    bool? SeimasMajorityFor,
    int MembersFor,
    int MembersAgainst,
    decimal? MembersShareFor,
    bool Compared,
    bool? Agreed,
    bool Dot,
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
    Task<ApiResult<AnalyticsResponse>> GetAsync(int? year, bool olderAccounts, CancellationToken cancellationToken = default);

    Task<ApiResult<MyAgreementResponse>> GetMyAgreementAsync(CancellationToken cancellationToken = default);
}
