using Lt2.Notifications.Digests;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>Design 8.4: when summaries are made and what goes in them.</summary>
public sealed class DigestRuleTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly TimeOnly Seven = new(7, 0);

    private static DigestCandidate Candidate(string bill, bool reached = true, bool held = false, PlannedDate? planned = null, int participants = 100) =>
        new(Guid.NewGuid(), bill, bill, participants, participants, 0, 0, 100, reached, held, null, null, planned);

    private static DateTimeOffset Vilnius(DateOnly day, int hour, int minute = 0) => DigestRules.At(day, new TimeOnly(hour, minute));

    [Fact]
    public void The_weekly_summary_goes_on_the_coming_monday_or_today_on_a_monday()
    {
        DigestRules.WeeklySendingDay(Monday).ShouldBe(Monday);
        DigestRules.WeeklySendingDay(Monday.AddDays(-1)).ShouldBe(Monday, "Sunday: tomorrow");
        DigestRules.WeeklySendingDay(Monday.AddDays(-6)).ShouldBe(Monday, "Tuesday: the next Monday");
        DigestRules.WeeklySendingDay(Monday.AddDays(1)).ShouldBe(Monday.AddDays(7));
    }

    [Fact]
    public void A_summary_is_prepared_the_review_hours_before_it_goes_so_mondays_on_sunday_at_the_same_time()
    {
        DigestRules.PreparedAt(Vilnius(Monday, 7), 24).ShouldBe(Vilnius(Monday.AddDays(-1), 7));
        var wednesday = Monday.AddDays(2);
        DigestRules.PreparedAt(Vilnius(wednesday, 12), 24).ShouldBe(Vilnius(wednesday.AddDays(-1), 12), "before a Thursday sitting: Tuesday noon");
        DigestRules.PreparedAt(Vilnius(Monday, 7), 6).ShouldBe(Vilnius(Monday, 1));
    }

    [Fact]
    public void Preparing_on_the_vilnius_clock_keeps_the_hour_across_the_night_the_clocks_change()
    {
        // 2026-10-25 is the last Sunday of October: the clocks go back that night; Monday 07:00 is still prepared at Sunday 07:00.
        var monday = new DateOnly(2026, 10, 26);
        var prepared = DigestRules.PreparedAt(DigestRules.At(monday, new TimeOnly(7, 0)), 24);
        TimeZoneInfo.ConvertTime(prepared, VilniusTime.Zone).DateTime.ShouldBe(new DateTime(2026, 10, 25, 7, 0, 0));
    }

    [Fact]
    public void A_summary_is_prepared_once_from_its_preparation_time_until_its_sending_day_ends()
    {
        var sendAt = Vilnius(Monday, 7);
        DigestRules.PreparationDue(Vilnius(Monday.AddDays(-1), 6, 59), sendAt, 24, alreadyMade: false).ShouldBeFalse("before Sunday 07:00");
        DigestRules.PreparationDue(Vilnius(Monday.AddDays(-1), 7), sendAt, 24, false).ShouldBeTrue("Sunday 07:00");
        DigestRules.PreparationDue(Vilnius(Monday, 9), sendAt, 24, false).ShouldBeTrue("found late: prepared and gone at once");
        DigestRules.PreparationDue(Vilnius(Monday.AddDays(1), 0, 5), sendAt, 24, false).ShouldBeFalse("its day is over");
        DigestRules.PreparationDue(Vilnius(Monday.AddDays(-1), 8), sendAt, 24, alreadyMade: true).ShouldBeFalse("once");
    }

    [Fact]
    public void A_prepared_summary_goes_at_its_time_and_not_before()
    {
        var sendAt = Vilnius(Monday, 7);
        DigestRules.SendingDue(sendAt.AddSeconds(-1), sendAt).ShouldBeFalse();
        DigestRules.SendingDue(sendAt, sendAt).ShouldBeTrue("nobody approves it");
        DigestRules.SendingDue(sendAt, null).ShouldBeTrue("an urgent one goes at once");
    }

    [Fact]
    public void The_working_day_before_a_monday_sitting_is_the_friday() =>
        DigestRules.WorkingDayBefore(Monday).ShouldBe(Monday.AddDays(-3));

    [Fact]
    public void Only_records_that_reached_their_threshold_and_are_not_held_enter_summaries()
    {
        DigestRules.Eligible(Candidate("A")).ShouldBeTrue();
        DigestRules.Eligible(Candidate("B", reached: false)).ShouldBeFalse();
        DigestRules.Eligible(Candidate("C", held: true)).ShouldBeFalse();
    }

    [Fact]
    public void The_weekly_summary_lists_records_on_the_weeks_agenda_or_changed_notably_since_their_last_summary()
    {
        var onAgenda = Candidate("ON", planned: new PlannedDate("ON", Monday.AddDays(3), "adoption", null, false));
        var unchanged = Candidate("SAME", participants: 105);
        var grown = Candidate("GROWN", participants: 111);
        var fresh = Candidate("NEW");
        var last = new Dictionary<Guid, int> { [onAgenda.RecordId] = 100, [unchanged.RecordId] = 100, [grown.RecordId] = 100 };

        DigestRules.WeeklyItems([onAgenda, unchanged, grown, fresh], Monday, last, 0.10m).Select(c => c.BillNumber).ShouldBe(["ON", "GROWN", "NEW"]);
    }

    [Fact]
    public void A_record_changed_notably_when_its_participants_changed_by_at_least_the_set_share_since_its_last_summary()
    {
        DigestRules.ChangedNotably(110, 100, 0.10m).ShouldBeTrue("exactly the share");
        DigestRules.ChangedNotably(109, 100, 0.10m).ShouldBeFalse();
        DigestRules.ChangedNotably(90, 100, 0.10m).ShouldBeTrue("a fall counts as much as a rise");
        DigestRules.ChangedNotably(120, 100, 0.25m).ShouldBeFalse("the share is the setting's");
        DigestRules.ChangedNotably(125, 100, 0.25m).ShouldBeTrue();
    }

    [Fact]
    public void A_record_never_in_a_summary_counts_as_changed_notably_whatever_the_share() =>
        DigestRules.ChangedNotably(100, null, 1m).ShouldBeTrue();

    [Fact]
    public void A_higher_notable_change_share_leaves_a_grown_record_out_of_the_weekly_summary_unless_it_is_on_the_agenda()
    {
        var onAgenda = Candidate("ON", planned: new PlannedDate("ON", Monday.AddDays(3), "adoption", null, false));
        var grown = Candidate("GROWN", participants: 115);
        var last = new Dictionary<Guid, int> { [onAgenda.RecordId] = 100, [grown.RecordId] = 100 };

        DigestRules.WeeklyItems([onAgenda, grown], Monday, last, 0.10m).Select(c => c.BillNumber).ShouldBe(["ON", "GROWN"]);
        DigestRules.WeeklyItems([onAgenda, grown], Monday, last, 0.20m).Select(c => c.BillNumber).ShouldBe(["ON"]);
    }

    [Fact]
    public void A_before_vote_summary_holds_only_bills_voted_at_that_sitting_not_already_in_mondays_summary()
    {
        var thursday = Monday.AddDays(3);
        var covered = Candidate("COVERED", planned: new PlannedDate("COVERED", thursday, "adoption", null, false));
        var due = Candidate("DUE", planned: new PlannedDate("DUE", thursday, "consideration", null, false));
        var noStage = Candidate("NOSTAGE", planned: new PlannedDate("NOSTAGE", thursday, null, null, false));
        var later = Candidate("LATER", planned: new PlannedDate("LATER", thursday.AddDays(5), "adoption", null, false));

        DigestRules.BeforeVoteItems([covered, due, noStage, later], thursday, new HashSet<Guid> { covered.RecordId })
            .Select(c => c.BillNumber).ShouldBe(["DUE"]);
    }

    [Fact]
    public void An_mp_gets_at_most_the_weekly_ceiling_and_an_urgent_summary_may_exceed_it_by_one()
    {
        DigestRules.WithinCeiling(alreadyThisWeek: 1, maxPerWeek: 2, urgent: false).ShouldBeTrue();
        DigestRules.WithinCeiling(2, 2, urgent: false).ShouldBeFalse();
        DigestRules.WithinCeiling(2, 2, urgent: true).ShouldBeTrue();
        DigestRules.WithinCeiling(3, 2, urgent: true).ShouldBeFalse();
    }

    [Fact]
    public void Bills_on_the_agenda_come_first_by_date_then_the_rest_by_participation()
    {
        var soon = Candidate("SOON", planned: new PlannedDate("SOON", Monday.AddDays(1), "adoption", null, false), participants: 10);
        var later = Candidate("LATER", planned: new PlannedDate("LATER", Monday.AddDays(3), "adoption", null, false), participants: 900);
        var big = Candidate("BIG", participants: 5000);
        var small = Candidate("SMALL", participants: 50);
        DigestRules.Order([small, later, big, soon]).Select(c => c.BillNumber).ShouldBe(["SOON", "LATER", "BIG", "SMALL"]);
    }

    // Design 8.12.2 (owner, 2026-11-18): the day of the next summary that would hold a bill, said on its page once the
    // threshold is reached. Monday is 2026-11-16; sittings are on Tuesdays and Thursdays.

    private static readonly TimeOnly Noon = new(12, 0);

    private static NextSummaryFacts Facts(
        DateTimeOffset now, DateOnly[] sittings, DateOnly[]? weeklyMade = null, DateOnly[]? beforeVoteMade = null,
        bool covered = false, int? last = null, bool weekly = true, bool beforeVote = true) =>
        new(now, weekly ? Seven : null, beforeVote ? Noon : null, sittings.ToHashSet(), (weeklyMade ?? []).ToHashSet(),
            (beforeVoteMade ?? []).ToHashSet(), covered, last, 0.10m);

    private static PlannedDate Planned(DateOnly day, string? stage = "adoption") => new("XVP-0412", day, stage, null, false);

    [Fact]
    public void The_next_summary_of_a_bill_never_in_one_is_the_coming_weekly_summary_of_a_week_with_a_sitting()
    {
        var wednesday = Vilnius(Monday.AddDays(2), 10);
        DigestRules.NextSendingDay(Candidate("XVP-0412"), Facts(wednesday, [Monday.AddDays(8), Monday.AddDays(10)]))
            .ShouldBe(Monday.AddDays(7));
        DigestRules.NextSendingDay(Candidate("XVP-0412"), Facts(wednesday, [Monday.AddDays(15)]))
            .ShouldBe(Monday.AddDays(14), "a week without a sitting has no weekly summary");
        DigestRules.NextSendingDay(Candidate("XVP-0412"), Facts(wednesday, []))
            .ShouldBeNull("no sitting is known yet, so no day either");
    }

    [Fact]
    public void A_weekly_summary_already_prepared_is_not_the_next_one_for_a_bill_it_does_not_hold()
    {
        var sundayAfterPreparing = Vilnius(Monday.AddDays(6), 8);
        DigestRules.NextSendingDay(Candidate("XVP-0412"),
                Facts(sundayAfterPreparing, [Monday.AddDays(8), Monday.AddDays(15)], weeklyMade: [Monday.AddDays(7)]))
            .ShouldBe(Monday.AddDays(14));
    }

    [Fact]
    public void A_bill_that_barely_changed_since_its_last_summary_waits_for_the_week_it_is_planned_in()
    {
        var wednesday = Vilnius(Monday.AddDays(2), 10);
        var bill = Candidate("XVP-0412", planned: Planned(Monday.AddDays(17)), participants: 105);
        DigestRules.NextSendingDay(bill, Facts(wednesday, [Monday.AddDays(8), Monday.AddDays(17)], last: 100))
            .ShouldBe(Monday.AddDays(14), "the Monday of its sitting's week holds it");
        DigestRules.NextSendingDay(Candidate("XVP-0412", participants: 105), Facts(wednesday, [Monday.AddDays(8)], last: 100))
            .ShouldBeNull("not planned and not changed notably: no summary holds it");
        DigestRules.NextSendingDay(Candidate("XVP-0412", participants: 111), Facts(wednesday, [Monday.AddDays(8)], last: 100))
            .ShouldBe(Monday.AddDays(7), "changed by 10 % or more: the coming weekly summary");
    }

    [Fact]
    public void The_before_vote_summary_comes_first_when_this_weeks_monday_summary_went_without_the_bill()
    {
        var tuesday = Vilnius(Monday.AddDays(1), 10);
        var thursday = Monday.AddDays(3);
        var bill = Candidate("XVP-0412", planned: Planned(thursday), participants: 105);
        DigestRules.NextSendingDay(bill, Facts(tuesday, [thursday, Monday.AddDays(8)], weeklyMade: [Monday], last: 100))
            .ShouldBe(Monday.AddDays(2), "12:00 on the working day before the sitting");
        DigestRules.NextSendingDay(bill, Facts(tuesday, [thursday, Monday.AddDays(8)], weeklyMade: [Monday], covered: true, last: 100))
            .ShouldBeNull("Monday's summary already held it for that sitting");
        DigestRules.NextSendingDay(bill, Facts(tuesday, [thursday], weeklyMade: [Monday], beforeVoteMade: [thursday], last: 100))
            .ShouldBeNull("that sitting's before-vote summary is made already");
        DigestRules.NextSendingDay(Candidate("XVP-0412", planned: Planned(thursday, stage: null), participants: 105),
                Facts(tuesday, [thursday], weeklyMade: [Monday], last: 100))
            .ShouldBeNull("a stage that is not voted on has no before-vote summary");
    }

    [Fact]
    public void A_bill_under_its_threshold_or_held_has_no_next_summary_and_a_time_switched_off_none_of_its_kind()
    {
        var wednesday = Vilnius(Monday.AddDays(2), 10);
        DigestRules.NextSendingDay(Candidate("XVP-0412", reached: false), Facts(wednesday, [Monday.AddDays(8)])).ShouldBeNull();
        DigestRules.NextSendingDay(Candidate("XVP-0412", held: true), Facts(wednesday, [Monday.AddDays(8)])).ShouldBeNull();
        DigestRules.NextSendingDay(Candidate("XVP-0412"), Facts(wednesday, [Monday.AddDays(8)], weekly: false)).ShouldBeNull();
        DigestRules.NextSendingDay(Candidate("XVP-0412", planned: Planned(Monday.AddDays(8))), Facts(wednesday, [Monday.AddDays(8)], weekly: false))
            .ShouldBe(Monday.AddDays(7), "with no weekly summary, the before-vote one: the working day before Tuesday's sitting");
    }
}