using Lt2.Parliament.Import.Parsing;
using Lt2.Parliament.Statistics;

namespace Lt2.UnitTests;

/// <summary>Design 7.3: "Balsavo kaip frakcija" and "Dalyvavimas balsavimuose", rule by rule.</summary>
public sealed class FactionPositionRuleTests
{
    [Fact]
    public void A_faction_has_a_position_when_at_least_two_thirds_of_its_members_who_voted_chose_the_same_option() =>
        FactionRule.PositionOf(@for: 7, against: 2, abstain: 1).ShouldBe(FactionPosition.For);

    [Fact]
    public void Exactly_two_thirds_is_enough() =>
        FactionRule.PositionOf(@for: 1, against: 2, abstain: 0).ShouldBe(FactionPosition.Against);

    [Fact]
    public void Less_than_two_thirds_is_a_free_vote() =>
        FactionRule.PositionOf(@for: 3, against: 1, abstain: 1).ShouldBe(FactionPosition.Free);

    [Fact]
    public void A_simple_majority_is_not_a_position_sixteen_to_fourteen_is_free() =>
        FactionRule.PositionOf(@for: 16, against: 14, abstain: 0).ShouldBe(FactionPosition.Free);

    [Fact]
    public void Abstaining_together_is_a_position_too() =>
        FactionRule.PositionOf(@for: 0, against: 1, abstain: 4).ShouldBe(FactionPosition.Abstain);

    [Fact]
    public void Members_who_did_not_vote_do_not_count_towards_the_two_thirds()
    {
        var tally = FactionRule.Tally(
        [
            ("RPF", BallotChoice.For), ("RPF", BallotChoice.For), ("RPF", BallotChoice.Against),
            ("RPF", BallotChoice.DidNotVote), ("RPF", BallotChoice.DidNotVote), ("RPF", BallotChoice.DidNotVote),
        ]).Single();

        tally.ShouldBe(new FactionTally("RPF", 2, 1, 0, 3));
        tally.Position.ShouldBe(FactionPosition.For);
    }

    [Fact]
    public void A_faction_where_nobody_voted_has_no_position_and_is_not_a_free_vote() =>
        FactionRule.PositionOf(0, 0, 0).ShouldBe(FactionPosition.NobodyVoted);

    [Fact]
    public void Each_faction_label_of_a_vote_is_tallied_on_its_own_and_rows_without_a_faction_belong_to_none()
    {
        var tallies = FactionRule.Tally(
        [
            ("RPF", BallotChoice.For), ("PSF", BallotChoice.Against), ("PSF", BallotChoice.Against), ("", BallotChoice.For), (null, BallotChoice.For),
        ]);

        tallies.Select(t => (t.Label, t.Position)).ShouldBe([("PSF", FactionPosition.Against), ("RPF", FactionPosition.For)]);
    }
}

public sealed class MpStatisticsRuleTests
{
    private static MpVoteFact Vote(BallotChoice choice, FactionPosition? faction = FactionPosition.For) => new(choice, faction);

    [Fact]
    public void Participation_is_the_share_of_recorded_votes_held_during_the_mandate_in_which_the_mp_voted_for_against_or_abstained()
    {
        var stats = MpStatisticsRule.Compute(
        [
            Vote(BallotChoice.For), Vote(BallotChoice.Against), Vote(BallotChoice.Abstain), Vote(BallotChoice.DidNotVote),
        ]);

        (stats.VotesHeld, stats.VotesTakenPart).ShouldBe((4, 3));
        stats.Participation.ShouldBe(0.75m);
    }

    [Fact]
    public void Abstaining_counts_as_taking_part() =>
        MpStatisticsRule.Compute([Vote(BallotChoice.Abstain)]).Participation.ShouldBe(1m);

    [Fact]
    public void Voted_with_faction_is_the_share_of_votes_where_the_faction_had_a_position_in_which_the_mp_chose_it()
    {
        var stats = MpStatisticsRule.Compute(
        [
            Vote(BallotChoice.For, FactionPosition.For),
            Vote(BallotChoice.Against, FactionPosition.For),
            Vote(BallotChoice.Abstain, FactionPosition.Abstain),
            Vote(BallotChoice.Against, FactionPosition.Against),
        ]);

        (stats.VotesWithFactionPosition, stats.VotedWithFaction).ShouldBe((4, 3));
        stats.WithFaction.ShouldBe(0.75m);
    }

    [Fact]
    public void Free_votes_are_excluded_from_voted_with_faction_and_counted_separately()
    {
        var stats = MpStatisticsRule.Compute(
        [
            Vote(BallotChoice.For, FactionPosition.For),
            Vote(BallotChoice.Against, FactionPosition.Free),
            Vote(BallotChoice.For, FactionPosition.Free),
        ]);

        (stats.VotesWithFactionPosition, stats.VotedWithFaction, stats.FreeVotes).ShouldBe((1, 1, 2));
        stats.WithFaction.ShouldBe(1m);
    }

    [Fact]
    public void A_vote_the_mp_did_not_take_part_in_counts_against_participation_only_not_against_voting_with_the_faction()
    {
        var stats = MpStatisticsRule.Compute(
        [
            Vote(BallotChoice.For, FactionPosition.For),
            Vote(BallotChoice.DidNotVote, FactionPosition.Against),
            Vote(BallotChoice.DidNotVote, FactionPosition.Free),
        ]);

        stats.ShouldBe(new MpStatistics(VotesHeld: 3, VotesTakenPart: 1, VotesWithFactionPosition: 1, VotedWithFaction: 1, FreeVotes: 0));
    }

    [Fact]
    public void A_faction_where_nobody_voted_gives_no_position_and_no_free_vote()
    {
        var stats = MpStatisticsRule.Compute([Vote(BallotChoice.DidNotVote, FactionPosition.NobodyVoted)]);

        (stats.VotesWithFactionPosition, stats.FreeVotes).ShouldBe((0, 0));
    }

    [Fact]
    public void An_mp_without_a_faction_has_participation_but_no_voted_with_faction_share()
    {
        var stats = MpStatisticsRule.Compute([Vote(BallotChoice.For, faction: null), Vote(BallotChoice.Against, faction: null)]);

        stats.Participation.ShouldBe(1m);
        stats.WithFaction.ShouldBeNull();
        stats.FreeVotes.ShouldBe(0);
    }

    [Fact]
    public void Without_any_recorded_vote_both_shares_are_empty_rather_than_zero()
    {
        var stats = MpStatisticsRule.Compute([]);

        stats.Participation.ShouldBeNull();
        stats.WithFaction.ShouldBeNull();
    }
}
