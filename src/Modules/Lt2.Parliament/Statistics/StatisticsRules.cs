using Lt2.Parliament.Import.Parsing;

namespace Lt2.Parliament.Statistics;

/// <summary>A faction's position in one vote (design 5.1 <c>faction_position</c>).</summary>
internal enum FactionPosition
{
    /// <summary>No member of the faction voted for, against or abstained: no position, and not a free vote either.</summary>
    NobodyVoted = 0,

    /// <summary>No option reached two thirds of the members who voted.</summary>
    Free = 1,

    For = 2,
    Against = 3,
    Abstain = 4,
}

/// <summary>How one faction's members voted in one vote, as the per-MP results label them.</summary>
internal sealed record FactionTally(string Label, int For, int Against, int Abstain, int DidNotVote)
{
    public FactionPosition Position => FactionRule.PositionOf(For, Against, Abstain);
}

/// <summary>
/// Design 7.3 "Balsavo kaip frakcija": a faction has a position in a vote when at least two thirds of its members who
/// voted chose the same option (for, against or abstain). Members who did not vote are not counted. Otherwise the
/// vote is free for that faction.
/// </summary>
internal static class FactionRule
{
    public static FactionPosition PositionOf(int @for, int against, int abstain)
    {
        var voted = @for + against + abstain;
        if (voted == 0)
        {
            return FactionPosition.NobodyVoted;
        }

        // Two thirds or more: at most one option can reach it.
        return 3 * @for >= 2 * voted ? FactionPosition.For
            : 3 * against >= 2 * voted ? FactionPosition.Against
            : 3 * abstain >= 2 * voted ? FactionPosition.Abstain
            : FactionPosition.Free;
    }

    /// <summary>One tally per faction label in a vote; rows without a label belong to no faction.</summary>
    public static IReadOnlyList<FactionTally> Tally(IEnumerable<(string? Label, BallotChoice Choice)> ballots) =>
        ballots.Where(b => !string.IsNullOrWhiteSpace(b.Label))
            .GroupBy(b => b.Label!.Trim(), StringComparer.Ordinal)
            .Select(g => new FactionTally(
                g.Key,
                g.Count(b => b.Choice == BallotChoice.For),
                g.Count(b => b.Choice == BallotChoice.Against),
                g.Count(b => b.Choice == BallotChoice.Abstain),
                g.Count(b => b.Choice == BallotChoice.DidNotVote)))
            .OrderBy(t => t.Label, StringComparer.Ordinal)
            .ToList();

    /// <summary>Whether an MP's choice is the faction's position.</summary>
    public static bool Matches(BallotChoice choice, FactionPosition position) => (choice, position) switch
    {
        (BallotChoice.For, FactionPosition.For) => true,
        (BallotChoice.Against, FactionPosition.Against) => true,
        (BallotChoice.Abstain, FactionPosition.Abstain) => true,
        _ => false,
    };
}

/// <summary>
/// One recorded vote held during the MP's mandate, as the statistics see it.
/// </summary>
/// <param name="Choice">The MP's choice; <see cref="BallotChoice.DidNotVote"/> also when the MP has no row in the result.</param>
/// <param name="FactionPosition">The position of the MP's faction in that vote; null when the MP had no faction.</param>
internal sealed record MpVoteFact(BallotChoice Choice, FactionPosition? FactionPosition);

/// <summary>Design 5.1 <c>mp_stat</c>: the counts behind the two statistics of a profile.</summary>
/// <param name="VotesHeld">Recorded votes held during the mandate (votes with per-MP results; consensus votes have none).</param>
/// <param name="VotesTakenPart">Of those, votes where the MP voted for, against or abstained.</param>
/// <param name="VotesWithFactionPosition">Votes the MP took part in where the MP's faction had a position.</param>
/// <param name="VotedWithFaction">Of those, votes where the MP chose the faction's position.</param>
/// <param name="FreeVotes">Votes the MP took part in where the MP's faction voted freely; shown beside the share.</param>
internal sealed record MpStatistics(int VotesHeld, int VotesTakenPart, int VotesWithFactionPosition, int VotedWithFaction, int FreeVotes)
{
    /// <summary>"Dalyvavimas balsavimuose"; null when no vote was held, never a made-up zero.</summary>
    public decimal? Participation => VotesHeld == 0 ? null : (decimal)VotesTakenPart / VotesHeld;

    /// <summary>"Balsavo kaip frakcija"; null when the faction never had a position in a vote the MP took part in.</summary>
    public decimal? WithFaction => VotesWithFactionPosition == 0 ? null : (decimal)VotedWithFaction / VotesWithFactionPosition;
}

/// <summary>Design 7.3: the two statistics of an MP profile. No ranking, no score: counts and two shares.</summary>
internal static class MpStatisticsRule
{
    /// <param name="votesHeldDuringMandate">One fact per recorded vote held during the MP's mandate.</param>
    public static MpStatistics Compute(IEnumerable<MpVoteFact> votesHeldDuringMandate)
    {
        ArgumentNullException.ThrowIfNull(votesHeldDuringMandate);
        int held = 0, tookPart = 0, withPosition = 0, withFaction = 0, free = 0;
        foreach (var vote in votesHeldDuringMandate)
        {
            held++;
            if (vote.Choice == BallotChoice.DidNotVote)
            {
                continue;
            }

            tookPart++;
            switch (vote.FactionPosition)
            {
                case FactionPosition.Free:
                    free++;
                    break;
                case FactionPosition.For or FactionPosition.Against or FactionPosition.Abstain:
                    withPosition++;
                    if (FactionRule.Matches(vote.Choice, vote.FactionPosition.Value))
                    {
                        withFaction++;
                    }

                    break;
            }
        }

        return new MpStatistics(held, tookPart, withPosition, withFaction, free);
    }
}
