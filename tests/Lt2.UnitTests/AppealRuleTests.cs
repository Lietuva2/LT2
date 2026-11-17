using Lt2.SharedKernel;
using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 8.2 and <c>AGENTS.md</c> "Appeals": one record per bill, one position per person, two sides, arguments, "Man svarbu".</summary>
public sealed class AppealRuleTests
{
    [Theory]
    [InlineData("for")]
    [InlineData("against")]
    public void For_and_against_are_the_two_sides(string side) => AppealRules.CheckSide(side).ShouldBeNull();

    [Theory]
    [InlineData("abstain")]
    [InlineData("For")]
    [InlineData("")]
    [InlineData(null)]
    public void Abstaining_or_anything_else_is_not_a_side(string? side) => AppealRules.CheckSide(side).ShouldBe("side.invalid");

    [Fact]
    public void An_argument_may_be_as_long_as_the_setting_says()
    {
        AppealRules.CheckReasonText(new string('a', 2000), maxLength: 2000).ShouldBeNull();
        AppealRules.CheckReasonText(new string('a', 2001), maxLength: 2000).ShouldBe("reason.too-long");
        AppealRules.CheckReasonText(new string('a', 501), maxLength: 500).ShouldBe("reason.too-long");
    }

    [Fact]
    public void The_length_of_members_texts_is_one_setting_of_2000_characters_by_default()
    {
        SettingKeys.TextsMaxLength.Name.ShouldBe("Texts:MaxLength");
        SettingKeys.TextsMaxLength.Default.ShouldBe(2000, "the owner's decision of 2026-11-16");
        SettingKeys.All.ShouldContain(SettingKeys.TextsMaxLength);
    }

    [Fact]
    public void With_the_setting_at_0_only_the_technical_ceiling_limits_an_argument()
    {
        AppealRules.CheckReasonText(new string('a', SettingKeys.MemberTextCeiling), maxLength: 0).ShouldBeNull();
        AppealRules.CheckReasonText(new string('a', SettingKeys.MemberTextCeiling + 1), maxLength: 0).ShouldBe("reason.too-long");
        MemberTextLength.Allowed(0).ShouldBe(SettingKeys.MemberTextCeiling);
    }

    [Fact]
    public void The_technical_ceiling_holds_whatever_the_setting_says()
    {
        SettingKeys.MemberTextCeiling.ShouldBe(20_000);
        MemberTextLength.Allowed(50_000).ShouldBe(SettingKeys.MemberTextCeiling);
        AppealRules.CheckReasonText(new string('a', SettingKeys.MemberTextCeiling + 1), maxLength: 50_000).ShouldBe("reason.too-long");
    }

    [Fact]
    public void An_organisations_statement_and_an_MPs_response_follow_the_same_length_setting()
    {
        OrganisationRules.CheckStatement("for", new string('a', 2000), null, null, maxLength: 2000).ShouldBeNull();
        OrganisationRules.CheckStatement("for", new string('a', 2001), null, null, maxLength: 2000).ShouldBe("statement.too-long");
        OrganisationRules.CheckStatement("for", new string('a', 5000), null, null, maxLength: 0).ShouldBeNull("0 = no limit but the ceiling");
        ResponseRules.CheckResponse("for", new string('a', 2000), maxLength: 2000).ShouldBeNull();
        ResponseRules.CheckResponse("for", new string('a', 2001), maxLength: 2000).ShouldBe("response.too-long");
        ResponseRules.CheckResponse("for", new string('a', SettingKeys.MemberTextCeiling + 1), maxLength: 0).ShouldBe("response.too-long");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2000, true)]
    [InlineData(20_000, true)]
    [InlineData(20_001, false)]
    [InlineData(-1, false)]
    public void The_setting_accepts_0_or_a_length_up_to_the_ceiling(int value, bool valid) =>
        SettingKeys.TextsMaxLength.IsValid(System.Text.Json.JsonSerializer.SerializeToElement(value)).ShouldBe(valid);

    [Fact]
    public void Surrounding_white_space_does_not_count_towards_the_length() =>
        AppealRules.CheckReasonText("  \n" + new string('ą', 500) + "\n  ", maxLength: 500).ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    [InlineData(null)]
    public void A_blank_argument_is_not_an_argument(string? text) => AppealRules.CheckReasonText(text, maxLength: 2000).ShouldBe("reason.empty");

    [Fact]
    public void An_argument_is_kept_as_plain_text_with_line_breaks_but_without_control_characters() =>
        AppealRules.NormaliseText(" Pirma eilutė\r\nantra\u0007 <b>ne HTML</b> ").ShouldBe("Pirma eilutė\nantra <b>ne HTML</b>");

    [Fact]
    public void There_is_no_limit_on_arguments_per_person_when_the_setting_is_0()
    {
        AppealRules.CheckNewReason(Sides.For, 0, limit: 0).ShouldBeNull();
        AppealRules.CheckNewReason(Sides.For, 500, limit: 0).ShouldBeNull();
        SettingKeys.AppealsReasonsPerPerson.Default.ShouldBe(0, "the owner's decision: no limit at the start");
    }

    [Fact]
    public void With_a_limit_set_a_person_may_write_only_while_under_it()
    {
        AppealRules.CheckNewReason(Sides.For, 1, limit: 2).ShouldBeNull();
        AppealRules.CheckNewReason(Sides.For, 2, limit: 2).ShouldBe("reason.limit");
    }

    [Fact]
    public void A_limit_switched_on_later_leaves_a_person_over_it_unable_to_add_but_removes_nothing()
    {
        AppealRules.CheckNewReason(Sides.For, 5, limit: 2).ShouldBe("reason.limit");
        AppealRules.Left(5, 2).ShouldBe(0, "nothing negative, nothing taken away");
        AppealRules.CheckEndorsement(ownReason: false, Sides.For, Sides.For, 7, limit: 3).ShouldBe("endorsement.limit");
    }

    [Fact]
    public void Only_shown_or_waiting_arguments_count_towards_the_limit()
    {
        AppealRules.CountedReasonStatuses.ShouldBe([ReasonStatuses.Published, ReasonStatuses.Pending], ignoreOrder: true);
        AppealRules.CountedReasonStatuses.ShouldNotContain(ReasonStatuses.Hidden);
        AppealRules.CountedReasonStatuses.ShouldNotContain(ReasonStatuses.Merged);
    }

    [Fact]
    public void Without_a_limit_nothing_is_left_to_count_so_the_pages_show_no_counter()
    {
        AppealRules.Left(3, limit: 0).ShouldBeNull();
        AppealRules.Left(1, limit: 3).ShouldBe(2);
    }

    [Fact]
    public void A_person_with_or_without_a_side_may_write_an_argument_for_either_side()
    {
        // The rule has no input for the author's side: it cannot refuse an argument because of it.
        AppealRules.CheckNewReason(Sides.For, 0, limit: 0).ShouldBeNull();
        AppealRules.CheckNewReason(Sides.Against, 0, limit: 0).ShouldBeNull();
        AppealRules.CheckNewReason("abstain", 0, limit: 0).ShouldBe("side.invalid", "an argument still belongs to one of the two sides");
    }

    [Fact]
    public void A_limit_on_arguments_counts_across_both_sides()
    {
        AppealRules.CheckNewReason(Sides.Against, 1, limit: 2).ShouldBeNull("one for, then one against");
        AppealRules.CheckNewReason(Sides.Against, 2, limit: 2).ShouldBe("reason.limit");
    }

    [Fact]
    public void Man_svarbu_still_needs_the_persons_own_side_even_though_arguments_do_not()
    {
        AppealRules.CheckEndorsement(ownReason: false, null, Sides.For, 0, limit: 0).ShouldBe("position.required");
        AppealRules.CheckEndorsement(ownReason: false, Sides.Against, Sides.For, 0, limit: 0).ShouldBe("endorsement.other-side");
    }

    [Fact]
    public void Man_svarbu_is_only_for_arguments_of_ones_own_side()
    {
        AppealRules.CheckEndorsement(ownReason: false, Sides.For, Sides.For, 0, limit: 0).ShouldBeNull();
        AppealRules.CheckEndorsement(ownReason: false, Sides.For, Sides.Against, 0, limit: 0).ShouldBe("endorsement.other-side");
        AppealRules.CheckEndorsement(ownReason: false, null, Sides.Against, 0, limit: 0).ShouldBe("position.required");
    }

    /// <summary>Owner, 2026-11-17: the marks choose the arguments sent to MPs, so an author never marks their own, whatever else holds.</summary>
    [Fact]
    public void Man_svarbu_is_never_on_ones_own_argument()
    {
        AppealRules.CheckEndorsement(ownReason: true, Sides.For, Sides.For, 0, limit: 0).ShouldBe("endorsement.own-reason");
        AppealRules.CheckEndorsement(ownReason: true, null, Sides.For, 0, limit: 0).ShouldBe("endorsement.own-reason");
        AppealRules.CheckEndorsement(ownReason: false, Sides.For, Sides.For, 0, limit: 0).ShouldBeNull("someone else's argument of one's own side");
    }

    [Fact]
    public void There_is_no_limit_on_man_svarbu_marks_when_the_setting_is_0_and_the_set_limit_otherwise()
    {
        AppealRules.CheckEndorsement(ownReason: false, Sides.Against, Sides.Against, 40, limit: 0).ShouldBeNull();
        SettingKeys.AppealsEndorsementsPerPerson.Default.ShouldBe(0);
        AppealRules.CheckEndorsement(ownReason: false, Sides.Against, Sides.Against, 2, limit: 3).ShouldBeNull();
        AppealRules.CheckEndorsement(ownReason: false, Sides.Against, Sides.Against, 3, limit: 3).ShouldBe("endorsement.limit");
    }
    [Fact]
    public void Changing_sides_drops_the_man_svarbu_marks_of_the_old_side()
    {
        AppealRules.ChangingSideDropsEndorsementsOfTheOldSide(Sides.For, Sides.Against).ShouldBeTrue();
        AppealRules.ChangingSideDropsEndorsementsOfTheOldSide(Sides.For, Sides.For).ShouldBeFalse();
        AppealRules.ChangingSideDropsEndorsementsOfTheOldSide(null, Sides.For).ShouldBeFalse();
    }

    [Fact]
    public void The_neutral_description_is_required_and_short()
    {
        AppealRules.CheckDescription("Įvedamas trečias pajamų mokesčio tarifas.").ShouldBeNull();
        AppealRules.CheckDescription(" ").ShouldBe("description.empty");
        AppealRules.CheckDescription(new string('x', AppealRules.DescriptionMaxLength + 1)).ShouldBe("description.too-long");
    }
}

/// <summary>Design 8.12.2: one threshold for all bills, a moderator's override, never below the minimum.</summary>
public sealed class ThresholdRuleTests
{
    private static readonly DateTimeOffset Summarised = new(2026, 11, 16, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nobody_sets_a_threshold_below_the_administrators_minimum()
    {
        ThresholdRules.CheckRequested(49, 50).ShouldBe("threshold.below-minimum");
        ThresholdRules.CheckRequested(50, 50).ShouldBeNull();
        ThresholdRules.CheckRequested(1000, 50).ShouldBeNull();
    }

    [Fact]
    public void The_form_offers_the_default_but_never_less_than_the_minimum()
    {
        ThresholdRules.Offered(100, 50).ShouldBe(100);
        ThresholdRules.Offered(100, 150).ShouldBe(150);
    }

    [Fact]
    public void A_bill_without_its_own_number_has_the_default_read_now()
    {
        ThresholdRules.Effective(stored: null, defaultValue: 100, minimum: 50).ShouldBe(100);
        ThresholdRules.Effective(stored: null, defaultValue: 250, minimum: 50).ShouldBe(250);
    }

    [Fact]
    public void A_moderators_number_replaces_the_default()
    {
        ThresholdRules.Effective(stored: 1000, defaultValue: 100, minimum: 50).ShouldBe(1000);
        ThresholdRules.Effective(stored: 60, defaultValue: 100, minimum: 50).ShouldBe(60);
    }

    [Fact]
    public void A_raised_minimum_lifts_every_bill_below_it()
    {
        ThresholdRules.Effective(stored: 60, defaultValue: 100, minimum: 150).ShouldBe(150);
        ThresholdRules.Effective(stored: null, defaultValue: 100, minimum: 150).ShouldBe(150);
    }

    [Fact]
    public void The_threshold_counts_both_sides_together()
    {
        ThresholdRules.Reached(participantsBothSides: 99, effectiveThreshold: 100).ShouldBeFalse();
        ThresholdRules.Reached(participantsBothSides: 100, effectiveThreshold: 100).ShouldBeTrue();
    }

    [Fact]
    public void A_bill_in_a_summary_really_sent_stays_eligible() =>
        ThresholdRules.Reached(participantsBothSides: 10, effectiveThreshold: 100, firstSummarisedAt: Summarised).ShouldBeTrue();
}

/// <summary>Design 6.6 and 8.2: both sides together, each with the count for accounts older than 30 days.</summary>
public sealed class CountRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_account_counts_as_older_than_30_days_once_thirty_full_days_have_passed()
    {
        // The counts take a member as older than 30 days when they became one at or before this moment.
        var cutoff = CountRules.EstablishedBefore(Now);
        (Now.AddDays(-30) <= cutoff).ShouldBeTrue();
        (Now.AddDays(-30).AddSeconds(1) <= cutoff).ShouldBeFalse();
        (Now.AddDays(-400) <= cutoff).ShouldBeTrue();
        (Now <= cutoff).ShouldBeFalse();
    }

    [Fact]
    public void Every_published_count_carries_both_sides_and_their_older_account_counts()
    {
        var counts = CountRules.Publish(6800, 6150, 4280, 3900);

        counts.For.ShouldBe(6800);
        counts.ForOlderThan30Days.ShouldBe(6150);
        counts.Against.ShouldBe(4280);
        counts.AgainstOlderThan30Days.ShouldBe(3900);
        counts.Total.ShouldBe(11080);
    }

    [Theory]
    [InlineData(10, 11, 0, 0)]
    [InlineData(0, 0, 5, 6)]
    [InlineData(-1, 0, 0, 0)]
    public void An_older_account_count_never_exceeds_its_side(int forTotal, int forOlder, int againstTotal, int againstOlder) =>
        Should.Throw<ArgumentOutOfRangeException>(() => CountRules.Publish(forTotal, forOlder, againstTotal, againstOlder));
}

/// <summary>Design 6.3 and 8.2: group numbers only for groups of at least 20 people.</summary>
public sealed class GroupRuleTests
{
    [Fact]
    public void A_group_of_19_is_not_shown()
    {
        GroupRules.MayPublish(19).ShouldBeFalse();
        GroupRules.Publishable(19).ShouldBeNull();
        GroupRules.Publishable(0).ShouldBeNull();
    }

    [Fact]
    public void A_group_beside_a_total_is_hidden_when_the_rest_could_be_worked_out_by_subtraction()
    {
        GroupRules.PublishableBeside(groupSize: 410, total: 6800).ShouldBe(410);
        GroupRules.PublishableBeside(groupSize: 25, total: 25).ShouldBe(25, "nothing else on that side");
        GroupRules.PublishableBeside(groupSize: 25, total: 30).ShouldBeNull("the other 5 would show by subtraction");
        GroupRules.PublishableBeside(groupSize: 19, total: 500).ShouldBeNull();
    }

    [Fact]
    public void A_group_of_20_or_more_is_shown()
    {
        GroupRules.MayPublish(20).ShouldBeTrue();
        GroupRules.Publishable(20).ShouldBe(20);
        GroupRules.Publishable(410).ShouldBe(410);
    }
}

/// <summary>Design 6.6: the surge note and the daily share of new accounts.</summary>
public sealed class SurgeRuleTests
{
    [Fact]
    public void A_surge_needs_at_least_the_share_and_at_least_the_number_of_new_accounts()
    {
        SurgeRules.IsSurge(recentPositions: 100, fromNewAccounts: 50, minimumShare: 0.30m, minimumAccounts: 50).ShouldBeTrue();
        SurgeRules.IsSurge(recentPositions: 100, fromNewAccounts: 30, minimumShare: 0.30m, minimumAccounts: 50).ShouldBeFalse("too few accounts");
        SurgeRules.IsSurge(recentPositions: 1000, fromNewAccounts: 299, minimumShare: 0.30m, minimumAccounts: 50).ShouldBeFalse("too small a share");
        SurgeRules.IsSurge(recentPositions: 1000, fromNewAccounts: 300, minimumShare: 0.30m, minimumAccounts: 50).ShouldBeTrue();
        SurgeRules.IsSurge(recentPositions: 0, fromNewAccounts: 0, minimumShare: 0.30m, minimumAccounts: 0).ShouldBeFalse();
    }

    [Fact]
    public void The_surge_note_never_overstates_the_share() => SurgeRules.SharePercent(300, 119).ShouldBe(39);

    [Fact]
    public void The_daily_share_of_new_accounts_is_zero_on_a_day_without_new_positions()
    {
        SurgeRules.NewAccountShare(0, 0).ShouldBe(0);
        SurgeRules.NewAccountShare(3, 1).ShouldBe(0.3333m);
    }
}

/// <summary>Design 5.3: a finished day's aggregate is written once, after the day, and not changed by later days.</summary>
public sealed class DailyStatRuleTests
{
    private static readonly DateTimeOffset DayEnd = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_day_still_running_is_rewritten_on_every_run() =>
        DailyStatRules.ShouldWrite(DayEnd, DayEnd.AddHours(-1), DayEnd.AddHours(-2)).ShouldBeTrue();

    [Fact]
    public void A_finished_day_is_written_by_the_first_run_after_it_ended()
    {
        DailyStatRules.ShouldWrite(DayEnd, DayEnd.AddMinutes(5), computedAt: null).ShouldBeTrue("no row yet");
        DailyStatRules.ShouldWrite(DayEnd, DayEnd.AddMinutes(5), DayEnd.AddHours(-1)).ShouldBeTrue("the row is the day so far");
    }

    [Fact]
    public void A_finished_days_final_row_is_left_alone() =>
        DailyStatRules.ShouldWrite(DayEnd, DayEnd.AddHours(20), DayEnd.AddMinutes(5)).ShouldBeFalse("later changes of side are not that day's");
}

/// <summary>Design 8.1: a bill's record closes at the final Seimas vote and its counts are frozen.</summary>
public sealed class ClosingRuleTests
{
    [Fact]
    public void An_open_record_closes_when_its_bill_has_a_final_vote()
    {
        ClosingRules.ShouldClose(BillRecordStatuses.Open, 99100009).ShouldBeTrue();
        ClosingRules.ShouldClose(BillRecordStatuses.Open, null).ShouldBeFalse();
        ClosingRules.ShouldClose(BillRecordStatuses.Rejected, 99100009).ShouldBeFalse("a hidden record stays hidden");
    }

    [Fact]
    public void A_closed_record_shows_its_frozen_counts_whatever_happens_to_positions_later()
    {
        var live = CountRules.Publish(5, 1, 3, 0);
        ClosingRules.Shown(live, 7, 2, 4, 1).ShouldBe(CountRules.Publish(7, 2, 4, 1));
        ClosingRules.Shown(live, null, null, null, null).ShouldBe(live);
    }
}
