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
    public void A_vote_nobody_voted_in_has_no_share_and_no_majority()
    {
        AnalyticsRules.SeimasShareFor(Vote(0, 0, 0)).ShouldBeNull();
        AnalyticsRules.SeimasMajorityFor(Vote(0, 0, 0)).ShouldBeNull();
    }

    [Fact]
    public void A_vote_without_numbers_has_no_majority()
    {
        var none = new SeimasVoteNumbers(false, null, null, null);
        AnalyticsRules.SeimasMajorityFor(none).ShouldBeNull();
        AnalyticsRules.SeimasMajorityAgainst(none).ShouldBeNull();
        AnalyticsRules.SeimasMajority(none).ShouldBeNull();
    }

    [Fact]
    public void The_seimas_majority_is_against_when_votes_against_are_more_than_half_of_those_who_voted()
    {
        AnalyticsRules.SeimasMajorityAgainst(Vote(30, 51, 20)).ShouldBe(true);
        AnalyticsRules.SeimasMajority(Vote(30, 51, 20)).ShouldBe(SeimasMajorities.Against);
    }

    [Fact]
    public void Abstentions_count_against_a_majority_against() =>
        AnalyticsRules.SeimasMajorityAgainst(Vote(10, 50, 40)).ShouldBe(false);

    [Fact]
    public void Exactly_half_against_is_no_majority_against() =>
        AnalyticsRules.SeimasMajorityAgainst(Vote(50, 50, 0)).ShouldBe(false);

    [Fact]
    public void A_vote_by_consensus_is_never_a_majority_against()
    {
        var consensus = new SeimasVoteNumbers(true, null, null, null);
        AnalyticsRules.SeimasMajorityAgainst(consensus).ShouldBe(false);
        AnalyticsRules.SeimasMajority(consensus).ShouldBe(SeimasMajorities.For);
    }

    [Fact]
    public void The_seimas_majority_is_for_against_or_none_when_neither_side_has_more_than_half()
    {
        AnalyticsRules.SeimasMajority(Vote(51, 30, 20)).ShouldBe(SeimasMajorities.For);
        AnalyticsRules.SeimasMajority(Vote(30, 51, 20)).ShouldBe(SeimasMajorities.Against);
        AnalyticsRules.SeimasMajority(Vote(50, 50, 0)).ShouldBe(SeimasMajorities.None);
        AnalyticsRules.SeimasMajority(Vote(40, 40, 30)).ShouldBe(SeimasMajorities.None);
    }

    [Fact]
    public void No_majority_in_the_seimas_counts_as_not_for_and_agrees_with_members_against()
    {
        var comparison = AnalyticsRules.Compare(Vote(40, 40, 30), membersFor: 5, membersAgainst: 15);
        comparison.SeimasMajority.ShouldBe(SeimasMajorities.None);
        comparison.Agreed.ShouldBe(true);
        AnalyticsRules.Compare(Vote(40, 40, 30), membersFor: 15, membersAgainst: 5).Agreed.ShouldBe(false);
    }

    [Fact]
    public void A_majority_against_in_the_seimas_agrees_with_members_against() =>
        AnalyticsRules.Compare(Vote(30, 70, 10), membersFor: 5, membersAgainst: 15).Agreed.ShouldBe(true);

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
    }

    [Fact]
    public void From_twenty_positions_a_bill_is_compared_and_agrees_when_both_majorities_chose_the_same()
    {
        var comparison = AnalyticsRules.Compare(Vote(80, 30, 10), membersFor: 15, membersAgainst: 5);

        comparison.Compared.ShouldBeTrue();
        comparison.MembersShareFor.ShouldBe(0.75m);
        comparison.Agreed.ShouldBe(true);
        comparison.SeimasShareFor.ShouldBe(80m / 120);
    }

    [Fact]
    public void Majorities_that_chose_differently_do_not_agree() =>
        AnalyticsRules.Compare(Vote(80, 30, 10), membersFor: 5, membersAgainst: 15).Agreed.ShouldBe(false);

    [Fact]
    public void A_tie_among_members_is_not_compared() =>
        AnalyticsRules.Compare(Vote(80, 30, 10), membersFor: 10, membersAgainst: 10).Compared.ShouldBeFalse();

    [Fact]
    public void A_bill_adopted_by_consensus_is_compared_but_has_no_seimas_share()
    {
        var comparison = AnalyticsRules.Compare(new SeimasVoteNumbers(true, null, null, null), membersFor: 20, membersAgainst: 4);

        comparison.Compared.ShouldBeTrue();
        comparison.Agreed.ShouldBe(true);
        comparison.SeimasShareFor.ShouldBeNull();
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
        // Ten votes where 9 of 10 chose the position and ten where 18 of 20 did: 270 of 300.
        var unity = FactionUnityRule.Compute(
            Enumerable.Range(1, 10).Select(i => Tally(i, 9, 1, 0)).Concat(Enumerable.Range(11, 10).Select(i => Tally(i, 2, 18, 0))));

        unity.VotesWithPosition.ShouldBe(20);
        unity.MembersWithPosition.ShouldBe(270);
        unity.MembersVoted.ShouldBe(300);
        unity.Share.ShouldBe(0.9m);
    }

    [Fact]
    public void A_free_vote_is_counted_beside_the_share_and_never_in_it()
    {
        var unity = FactionUnityRule.Compute(Enumerable.Range(1, 20).Select(i => Tally(i, 10, 0, 0)).Append(Tally(21, 16, 14, 0)));

        unity.FreeVotes.ShouldBe(1);
        unity.VotesWithPosition.ShouldBe(20);
        unity.Share.ShouldBe(1m);
    }

    [Fact]
    public void Without_a_vote_with_a_position_there_is_no_share() =>
        FactionUnityRule.Compute([Tally(1, 5, 5, 0)]).Share.ShouldBeNull();

    [Fact]
    public void Under_twenty_votes_with_a_position_there_is_no_share_only_the_counts_and_a_small_sample()
    {
        var small = FactionUnityRule.Compute(Enumerable.Range(1, 19).Select(i => Tally(i, 9, 1, 0)));
        (small.SmallSample, small.Share, small.VotesWithPosition, small.MembersWithPosition, small.MembersVoted).ShouldBe((true, (decimal?)null, 19, 171, 190));

        var enough = FactionUnityRule.Compute(Enumerable.Range(1, 20).Select(i => Tally(i, 9, 1, 0)));
        (enough.SmallSample, enough.Share).ShouldBe((false, (decimal?)0.9m));
    }

    [Fact]
    public void Two_labels_of_one_faction_in_a_vote_are_one_tally_with_the_position_worked_out_from_their_sum()
    {
        // One label alone is "for" (6 of 7), the other "against" (5 of 7); added up (8 for, 6 against) no option reaches two thirds: a free vote.
        var merged = FactionUnityRule.PerVote([Tally(1, 6, 1, 0), Tally(1, 2, 5, 0)]).ShouldHaveSingleItem();

        (merged.For, merged.Against, merged.Position).ShouldBe((8, 6, FactionPosition.Free));
        var unity = FactionUnityRule.Compute([Tally(1, 6, 1, 0), Tally(1, 2, 5, 0)]);
        (unity.FreeVotes, unity.VotesWithPosition).ShouldBe((1, 0), "the vote counts once");
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

/// <summary>Design 8.26.1: a share beside the line at 50 % never reads as 50 % unless it is exactly half.</summary>
public sealed class AnalyticsShareFormatTests
{
    [Fact]
    public void A_share_is_written_as_a_whole_percent() =>
        Lt2.Web.Client.Analytics.AnalyticsFormat.Share(0.712m).ShouldBe(Lt2.Web.Client.Display.Percent(0.712m));

    [Fact]
    public void Exactly_half_is_fifty_percent() =>
        Lt2.Web.Client.Analytics.AnalyticsFormat.Share(0.5m).ShouldBe(Lt2.Web.Client.Display.Percent(0.5m));

    [Fact]
    public void A_majority_and_its_absence_near_half_never_look_the_same()
    {
        Lt2.Web.Client.Analytics.AnalyticsFormat.Share(0.5035m).ShouldBe("50,4 %");
        Lt2.Web.Client.Analytics.AnalyticsFormat.Share(0.496m).ShouldBe("49,6 %");
        Lt2.Web.Client.Analytics.AnalyticsFormat.Share(0.50004m).ShouldBe("50,004 %");
    }

    [Fact]
    public void No_share_is_no_text() =>
        Lt2.Web.Client.Analytics.AnalyticsFormat.Share(null).ShouldBeNull();

    [Fact]
    public void A_difference_is_written_as_a_whole_percent_like_the_shares()
    {
        Lt2.Web.Client.Analytics.AnalyticsFormat.Difference(0.148m).ShouldBe("15\u00a0%");
        Lt2.Web.Client.Analytics.AnalyticsFormat.Difference(0m).ShouldBe("0\u00a0%");
        Lt2.Web.Client.Analytics.AnalyticsFormat.Difference(null).ShouldBeNull();
    }

    [Fact]
    public void A_share_on_the_track_is_a_percentage_of_its_width() =>
        Lt2.Web.Client.Analytics.AnalyticsFormat.Position(0.4321m).ShouldBe("43.2%");
}

/// <summary>Design 8.26.1 item 2 (owner, 2026-11-18): the list of closed bills, its filters and orders, rule by rule.</summary>
public sealed class ComparedBillListRuleTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static ComparedBillKey Key(string number, int daysLater, decimal? difference, int positions) =>
        new(Day.AddDays(daysLater), number, difference, positions);

    private static readonly ComparedBillKey[] Bills =
    [
        Key("XVP-0701", 0, 0.10m, 300),
        Key("XVP-0702", 5, null, 900),
        Key("XVP-0703", 3, 0.40m, 25),
        Key("XVP-0704", 3, 0.40m, 25),
        Key("XVP-0705", 9, 0.05m, 7),
    ];

    private static IEnumerable<string> Ordered(ComparedBillOrder order) => AnalyticsRules.OrderBills(Bills, order, b => b).Select(b => b.Number);

    [Fact]
    public void The_difference_is_the_distance_between_the_seimas_share_for_and_the_members_share_for()
    {
        AnalyticsRules.Difference(0.62m, 0.48m).ShouldBe(0.14m);
        AnalyticsRules.Difference(0.30m, 0.75m).ShouldBe(0.45m);
    }

    [Fact]
    public void Without_either_share_there_is_no_difference()
    {
        AnalyticsRules.Difference(null, 0.48m).ShouldBeNull("a consensus or a vote without numbers");
        AnalyticsRules.Difference(0.62m, null).ShouldBeNull("fewer than twenty positions");
    }

    [Fact]
    public void By_default_the_newest_closing_vote_comes_first_then_the_number() =>
        Ordered(ComparedBillOrder.Latest).ShouldBe(["XVP-0705", "XVP-0702", "XVP-0703", "XVP-0704", "XVP-0701"]);

    [Fact]
    public void By_difference_the_largest_comes_first_and_bills_without_one_come_last() =>
        Ordered(ComparedBillOrder.Difference).ShouldBe(["XVP-0703", "XVP-0704", "XVP-0701", "XVP-0705", "XVP-0702"]);

    [Fact]
    public void By_positions_the_most_come_first_ties_to_the_newest_closing_vote() =>
        Ordered(ComparedBillOrder.Positions).ShouldBe(["XVP-0702", "XVP-0701", "XVP-0703", "XVP-0704", "XVP-0705"]);

    [Fact]
    public void The_result_filter_keeps_only_compared_bills_with_that_result()
    {
        AnalyticsRules.MatchesResult(true, true).ShouldBeTrue();
        AnalyticsRules.MatchesResult(true, false).ShouldBeFalse();
        AnalyticsRules.MatchesResult(false, false).ShouldBeTrue();
        AnalyticsRules.MatchesResult(true, null).ShouldBeFalse("a bill that is not compared has no result");
        AnalyticsRules.MatchesResult(false, null).ShouldBeFalse();
        AnalyticsRules.MatchesResult(null, null).ShouldBeTrue("no filter keeps every bill");
    }

    [Fact]
    public void The_majority_filter_keeps_only_bills_with_that_seimas_majority()
    {
        AnalyticsRules.MatchesMajority(SeimasMajorities.None, SeimasMajorities.None).ShouldBeTrue();
        AnalyticsRules.MatchesMajority(SeimasMajorities.Against, SeimasMajorities.None).ShouldBeFalse("against and none are two choices in the filter");
        AnalyticsRules.MatchesMajority(SeimasMajorities.For, null).ShouldBeFalse("a vote without numbers has no majority");
        AnalyticsRules.MatchesMajority(null, null).ShouldBeTrue();
    }

    [Fact]
    public void The_days_of_the_closing_vote_include_both_ends_and_either_may_be_open()
    {
        var day = new DateOnly(2026, 10, 1);
        AnalyticsRules.InDays(day, day, day).ShouldBeTrue();
        AnalyticsRules.InDays(day, day.AddDays(1), null).ShouldBeFalse();
        AnalyticsRules.InDays(day, null, day.AddDays(-1)).ShouldBeFalse();
        AnalyticsRules.InDays(day, null, null).ShouldBeTrue();
    }
}
