using Lt2.SharedKernel;

namespace Lt2.Parliament.Statistics;

/// <summary>How one faction's members voted in one recorded vote, as faction unity reads it (design 8.26.3).</summary>
internal sealed record FactionVoteTally(long VoteId, DateTimeOffset HeldAt, FactionPosition Position, int For, int Against, int Abstain)
{
    /// <summary>Members who voted for, against or abstained.</summary>
    public int Voted => For + Against + Abstain;
}

/// <summary>A faction's unity over a period (design 8.26.3).</summary>
/// <param name="VotesWithPosition">Votes where the faction had a position (two thirds of those who voted chose one option).</param>
/// <param name="MembersVoted">Of those votes, the members who voted, added up.</param>
/// <param name="MembersWithPosition">Of those, the members who chose the faction's position, added up.</param>
/// <param name="FreeVotes">Votes where no option reached two thirds; never in the share.</param>
/// <param name="Share">Pooled: <paramref name="MembersWithPosition"/> ÷ <paramref name="MembersVoted"/>; null with no vote with a position.</param>
/// <param name="SmallSample">Fewer than <see cref="AnalyticsRules.SmallSample"/> votes with a position: "maža imtis".</param>
/// <param name="MostSplit">The three votes where the faction split most, most split first.</param>
internal sealed record FactionUnity(
    int VotesWithPosition, int MembersVoted, int MembersWithPosition, int FreeVotes, decimal? Share, bool SmallSample, IReadOnlyList<FactionVoteTally> MostSplit);

/// <summary>
/// Design 8.26.3: faction unity, from the faction positions of the two-thirds rule (<see cref="FactionRule"/>). The share
/// is pooled over the votes with a position, so a vote with many members voting weighs more; free votes are counted
/// beside it. A vote's split is the share of the faction's largest option among those who voted; the lowest three are
/// where it split most.
/// </summary>
internal static class FactionUnityRule
{
    /// <summary>How many votes "split most" lists.</summary>
    public const int MostSplitCount = 3;

    /// <summary>The share of the faction's largest option among its members who voted; null when fewer than two voted.</summary>
    public static decimal? Cohesion(FactionVoteTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return tally.Voted < 2 ? null : (decimal)Math.Max(tally.For, Math.Max(tally.Against, tally.Abstain)) / tally.Voted;
    }

    /// <summary>The members who chose the faction's position in one vote; 0 for a free vote or one nobody voted in.</summary>
    public static int WithPosition(FactionVoteTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return tally.Position switch
        {
            FactionPosition.For => tally.For,
            FactionPosition.Against => tally.Against,
            FactionPosition.Abstain => tally.Abstain,
            _ => 0,
        };
    }

    public static FactionUnity Compute(IEnumerable<FactionVoteTally> votes)
    {
        ArgumentNullException.ThrowIfNull(votes);
        var list = votes.ToList();
        var withPosition = list.Where(v => v.Position is FactionPosition.For or FactionPosition.Against or FactionPosition.Abstain).ToList();
        var voted = withPosition.Sum(v => v.Voted);
        var chose = withPosition.Sum(WithPosition);
        var mostSplit = list
            .Where(v => Cohesion(v) is not null)
            .OrderBy(v => Cohesion(v))
            .ThenByDescending(v => v.HeldAt)
            .ThenByDescending(v => v.VoteId)
            .Take(MostSplitCount)
            .ToList();
        return new FactionUnity(
            withPosition.Count,
            voted,
            chose,
            list.Count(v => v.Position == FactionPosition.Free),
            voted == 0 ? null : (decimal)chose / voted,
            !AnalyticsRules.IsEnough(withPosition.Count),
            mostSplit);
    }
}
