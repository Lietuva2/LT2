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
}