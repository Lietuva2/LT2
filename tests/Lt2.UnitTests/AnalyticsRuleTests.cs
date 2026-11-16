using Lt2.Parliament.Statistics;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>Design 8.26.2: comparing a closed bill's Seimas vote with the members' frozen counts, rule by rule.</summary>
public sealed class BillComparisonRuleTests
{
    private static SeimasVoteNumbers Vote(int @for, int against, int abstain) => new(false, @for, against, abstain);

    [Fact]
    public void The_seimas_share_for_is_for_out_of_for_against_and_abstain() =>
        AnalyticsRules.SeimasShareFor(Vote(60, 20, 20)).ShouldBe(0.6m);

    [Fact]
    public void A_vote_by_consensus_has_no_share_but_is_a_majority_for()
    {
        var consensus = new SeimasVoteNumbers(true, null, null, null);
        AnalyticsRules.SeimasShareFor(consensus).ShouldBeNull();
        AnalyticsRules.SeimasMajorityFor(consensus).ShouldBe(true);
    }

    [Fact]
    public void The_seimas_majority_is_for_when_votes_for_are_more_than_half_of_those_who_voted() =>
        AnalyticsRules.SeimasMajorityFor(Vote(51, 30, 20)).ShouldBe(true);

    [Fact]
    public void Abstentions_count_against_a_majority_for() =>
        AnalyticsRules.SeimasMajorityFor(Vote(50, 10, 40)).ShouldBe(false);

    [Fact]
    public void Exactly_half_for_is_no_majority_for() =>
        AnalyticsRules.SeimasMajorityFor(Vote(50, 50, 0)).ShouldBe(false);

    [Fact]
    public void A_vote_without_numbers_has_no_majority() =>
        AnalyticsRules.SeimasMajorityFor(new SeimasVoteNumbers(false, null, null, null)).ShouldBeNull();

    [Fact]
    public void The_members_majority_is_the_larger_side_and_a_tie_has_none()
    {
        AnalyticsRules.MembersMajorityFor(12, 8).ShouldBe(true);
        AnalyticsRules.MembersMajorityFor(8, 12).ShouldBe(false);
        AnalyticsRules.MembersMajorityFor(10, 10).ShouldBeNull();
    }

    [Fact]
    public void A_bill_with_fewer_than_twenty_positions_is_not_compared_and_shows_no_members_share()
    {
        var comparison = AnalyticsRules.Compare(Vote(80, 30, 10), membersFor: 12, membersAgainst: 7);

        comparison.Sample.ShouldBe(19);
        comparison.Compared.ShouldBeFalse();
        comparison.MembersShareFor.ShouldBeNull();
        comparison.Agreed.ShouldBeNull();
        comparison.Dot.ShouldBeFalse();
    }

    [Fact]
    public void From_twenty_positions_a_bill_is_compared_and_agrees_when_both_majorities_chose_the_same()
    {
        var comparison = AnalyticsRules.Compare(Vote(80, 30, 10), membersFor: 15, membersAgainst: 5);

        comparison.Compared.ShouldBeTrue();
        comparison.MembersShareFor.ShouldBe(0.75m);
        comparison.Agreed.ShouldBe(true);
        comparison.Dot.ShouldBeTrue();
    }

    [Fact]
    public void Majorities_that_chose_differently_do_not_agree() =>
        AnalyticsRules.Compare(Vote(80, 30, 10), membersFor: 5, membersAgainst: 15).Agreed.ShouldBe(false);

    [Fact]
    public void A_tie_among_members_is_not_compared() =>
        AnalyticsRules.Compare(Vote(80, 30, 10), membersFor: 10, membersAgainst: 10).Compared.ShouldBeFalse();

    [Fact]
    public void A_bill_adopted_by_consensus_is_compared_but_has_no_dot()
    {
        var comparison = AnalyticsRules.Compare(new SeimasVoteNumbers(true, null, null, null), membersFor: 20, membersAgainst: 4);

        comparison.Compared.ShouldBeTrue();
        comparison.Agreed.ShouldBe(true);
        comparison.Dot.ShouldBeFalse();
    }

    [Fact]
    public void The_share_of_agreed_bills_shows_only_from_twenty_compared()
    {
        AnalyticsRules.ShareFromSample(14, 19).ShouldBeNull();
        AnalyticsRules.ShareFromSample(15, 20).ShouldBe(0.75m);
        AnalyticsRules.ShareFromSample(0, 0).ShouldBeNull();
    }

    [Fact]
    public void One_sample_of_twenty_holds_for_every_number_on_the_page() =>
        AnalyticsRules.SmallSample.ShouldBe(20);
}

/// <summary>Design 8.26.4: the caller's own agreement with MPs and factions, rule by rule.</summary>
public sealed class AgreementRuleTests
{
    [Fact]
    public void An_mp_matches_when_the_ballot_is_the_callers_side()
    {
        var agreement = AnalyticsRules.AgreementOf([("for", "for"), ("against", "against"), ("for", "against")]);

        agreement.Matched.ShouldBe(2);
        agreement.Common.ShouldBe(3);
    }

    [Fact]
    public void An_abstention_is_in_the_sample_and_never_matches()
    {
        var agreement = AnalyticsRules.AgreementOf([("for", "abstain"), ("against", "abstain")]);

        agreement.Matched.ShouldBe(0);
        agreement.Common.ShouldBe(2);
    }

    [Fact]
    public void Not_voting_and_a_vote_without_ballots_are_not_in_the_sample() =>
        AnalyticsRules.AgreementOf([("for", "did-not-vote"), ("for", null), ("against", "nobody-voted")]).Common.ShouldBe(0);

    [Fact]
    public void A_free_vote_of_a_faction_is_counted_apart_and_not_in_the_sample()
    {
        var agreement = AnalyticsRules.AgreementOf([("for", "free"), ("for", "for")]);

        agreement.Free.ShouldBe(1);
        agreement.Common.ShouldBe(1);
    }

    [Fact]
    public void A_share_of_agreement_shows_only_from_twenty_common_votes()
    {
        AnalyticsRules.AgreementOf(Enumerable.Repeat(("for", (string?)"for"), 19)).Share.ShouldBeNull();
        AnalyticsRules.AgreementOf(Enumerable.Repeat(("for", (string?)"for"), 20)).Share.ShouldBe(1m);
    }

    [Fact]
    public void Those_with_a_share_come_first_highest_first_then_small_samples_by_name_and_nobody_without_a_common_vote()
    {
        var rows = new (string Name, Agreement Agreement)[]
        {
            ("Žilinskas", new Agreement(1, 2, 0, null)),
            ("Jonaitienė", new Agreement(18, 20, 0, 0.9m)),
            ("Petraitis", new Agreement(12, 20, 0, 0.6m)),
            ("Ąžuolas", new Agreement(3, 4, 0, null)),
            ("Bėrontas", new Agreement(0, 0, 0, null)),
            ("Adomaitis", new Agreement(18, 20, 0, 0.9m)),
        };

        AnalyticsRules.OrderForCaller(rows, r => r.Agreement, r => r.Name).Select(r => r.Name)
            .ShouldBe(["Adomaitis", "Jonaitienė", "Petraitis", "Ąžuolas", "Žilinskas"]);
    }
}

/// <summary>Design 8.26.1: the period is the whole term or one calendar year of it, in Vilnius days.</summary>
public sealed class AnalyticsPeriodRuleTests
{
    private static readonly DateOnly TermFrom = new(2024, 11, 14);
    private static readonly DateOnly Today = new(2026, 11, 16);

    [Fact]
    public void The_years_offered_are_those_of_the_term_up_to_today_newest_first() =>
        AnalyticsRules.Years(TermFrom, null, Today).ShouldBe([2026, 2025, 2024]);

    [Fact]
    public void Without_a_year_the_period_is_the_whole_term_up_to_today() =>
        AnalyticsRules.Period(TermFrom, null, Today, null).ShouldBe((TermFrom, Today));

    [Fact]
    public void A_year_is_cut_to_the_term_and_to_today()
    {
        AnalyticsRules.Period(TermFrom, null, Today, 2024).ShouldBe((TermFrom, new DateOnly(2024, 12, 31)));
        AnalyticsRules.Period(TermFrom, null, Today, 2025).ShouldBe((new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));
        AnalyticsRules.Period(TermFrom, null, Today, 2026).ShouldBe((new DateOnly(2026, 1, 1), Today));
    }

    [Fact]
    public void A_year_outside_the_term_is_the_whole_term() =>
        AnalyticsRules.Period(TermFrom, null, Today, 2019).ShouldBe((TermFrom, Today));

    [Fact]
    public void A_finished_term_ends_on_its_last_day() =>
        AnalyticsRules.Period(new DateOnly(2020, 11, 13), new DateOnly(2024, 11, 13), Today, null).ShouldBe((new DateOnly(2020, 11, 13), new DateOnly(2024, 11, 13)));
}

/// <summary>Design 8.26.3: faction unity from the two-thirds rule's positions, rule by rule.</summary>
public sealed class FactionUnityRuleTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static FactionVoteTally Tally(long id, int @for, int against, int abstain, int daysLater = 0) =>
        new(id, Day.AddDays(daysLater), FactionRule.PositionOf(@for, against, abstain), @for, against, abstain);

    [Fact]
    public void Unity_is_the_share_of_members_who_voted_that_chose_the_factions_position_pooled_over_votes()
    {
        // 9 of 10 and 18 of 20 chose the position: 27 of 30.
        var unity = FactionUnityRule.Compute([Tally(1, 9, 1, 0), Tally(2, 2, 18, 0)]);

        unity.VotesWithPosition.ShouldBe(2);
        unity.MembersWithPosition.ShouldBe(27);
        unity.MembersVoted.ShouldBe(30);
        unity.Share.ShouldBe(0.9m);
    }

    [Fact]
    public void A_free_vote_is_counted_beside_the_share_and_never_in_it()
    {
        var unity = FactionUnityRule.Compute([Tally(1, 10, 0, 0), Tally(2, 16, 14, 0)]);

        unity.FreeVotes.ShouldBe(1);
        unity.VotesWithPosition.ShouldBe(1);
        unity.Share.ShouldBe(1m);
    }

    [Fact]
    public void Without_a_vote_with_a_position_there_is_no_share() =>
        FactionUnityRule.Compute([Tally(1, 5, 5, 0)]).Share.ShouldBeNull();

    [Fact]
    public void Fewer_than_twenty_votes_with_a_position_is_a_small_sample()
    {
        FactionUnityRule.Compute(Enumerable.Range(1, 19).Select(i => Tally(i, 10, 0, 0))).SmallSample.ShouldBeTrue();
        FactionUnityRule.Compute(Enumerable.Range(1, 20).Select(i => Tally(i, 10, 0, 0))).SmallSample.ShouldBeFalse();
    }

    [Fact]
    public void The_votes_where_it_split_most_are_the_three_with_the_smallest_largest_option_free_votes_included()
    {
        var unity = FactionUnityRule.Compute(
        [
            Tally(1, 10, 0, 0),
            Tally(2, 6, 4, 0),
            Tally(3, 5, 3, 2),
            Tally(4, 8, 2, 0),
            Tally(5, 7, 3, 0),
        ]);

        unity.MostSplit.Select(t => t.VoteId).ShouldBe([3L, 2L, 5L]);
    }

    [Fact]
    public void A_vote_where_fewer_than_two_members_voted_is_no_split() =>
        FactionUnityRule.Cohesion(Tally(1, 1, 0, 0)).ShouldBeNull();

    [Fact]
    public void On_an_equal_split_the_newest_vote_comes_first() =>
        FactionUnityRule.Compute([Tally(1, 5, 5, 0, daysLater: 0), Tally(2, 5, 5, 0, daysLater: 3)]).MostSplit.Select(t => t.VoteId).ShouldBe([2L, 1L]);
}
