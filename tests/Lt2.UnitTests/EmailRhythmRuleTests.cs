using Lt2.Notifications.Rhythm;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.24 as pure functions: whose email an event goes in, when the daily and weekly emails are cut, and which "planned"
/// date cannot wait. Times are Vilnius times chosen by the test (November: UTC+2); 2026-11-16 is a Monday.
/// </summary>
public sealed class EmailRhythmRuleTests
{
    private static readonly TimeOnly Eight = new(8, 0);

    private static DateTimeOffset Vilnius(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(2));

    private static RhythmChoice Chose(string frequency, DateTimeOffset since, string? previous = null, DateTimeOffset? previousSince = null) =>
        new(frequency, since, previous, previousSince);

    [Fact]
    public void Without_a_choice_every_event_goes_with_the_runs_emails()
    {
        RhythmRules.ChoiceAt(RhythmChoice.Default, Vilnius(1, 0)).ShouldBe(Frequencies.Immediate);
        Frequencies.All.ShouldBe(["immediate", "daily", "weekly"]);
        Frequencies.IsValid("monthly").ShouldBeFalse();
        Frequencies.IsValid(null).ShouldBeFalse();
    }

    [Fact]
    public void An_event_belongs_to_the_choice_the_person_had_when_the_job_first_saw_it()
    {
        var choice = RhythmRules.Change(RhythmChoice.Default, Frequencies.Daily, Vilnius(16, 12));
        RhythmRules.ChoiceAt(choice, Vilnius(16, 11, 59)).ShouldBe(Frequencies.Immediate, "seen before the change: it went at once already");
        RhythmRules.ChoiceAt(choice, Vilnius(16, 12)).ShouldBe(Frequencies.Daily);
    }

    [Fact]
    public void What_waited_for_a_gathered_email_still_comes_in_it_after_a_switch_to_Is_karto()
    {
        var weekly = Chose(Frequencies.Weekly, Vilnius(10, 9));
        var now = RhythmRules.Change(weekly, Frequencies.Immediate, Vilnius(17, 9));
        RhythmRules.ChoiceAt(now, Vilnius(16, 20)).ShouldBe(Frequencies.Weekly, "it waits for Monday's email as it did");
        RhythmRules.ChoiceAt(now, Vilnius(17, 10)).ShouldBe(Frequencies.Immediate);
    }

    [Fact]
    public void A_second_change_before_the_email_drops_what_waited_under_the_first_choice_and_never_sends_it_twice()
    {
        var first = Chose(Frequencies.Weekly, Vilnius(10, 9));
        var second = RhythmRules.Change(first, Frequencies.Daily, Vilnius(17, 9));
        var third = RhythmRules.Change(second, Frequencies.Immediate, Vilnius(17, 10));
        RhythmRules.ChoiceAt(third, Vilnius(16, 20)).ShouldBeNull("it belonged to the weekly email the person left twice");
        RhythmRules.ChoiceAt(third, Vilnius(17, 9, 30)).ShouldBe(Frequencies.Daily, "it waits for the next morning");
        RhythmRules.ChoiceAt(third, Vilnius(17, 10)).ShouldBe(Frequencies.Immediate);
    }

    /// <summary>Design 8.24: a mis-click corrected at once loses nothing (daily → weekly → daily within minutes).</summary>
    [Fact]
    public void Going_back_to_the_choice_just_left_restores_it_so_nothing_waiting_for_it_is_dropped()
    {
        var daily = Chose(Frequencies.Daily, Vilnius(10, 9), Frequencies.Weekly, Vilnius(1, 9));
        var weekly = RhythmRules.Change(daily, Frequencies.Weekly, Vilnius(17, 9));
        var back = RhythmRules.Change(weekly, Frequencies.Daily, Vilnius(17, 9, 2));
        back.ShouldBe(new RhythmChoice(Frequencies.Daily, Vilnius(10, 9), null, null));
        RhythmRules.ChoiceAt(back, Vilnius(16, 20)).ShouldBe(Frequencies.Daily, "what waited for the morning still comes in it");
        RhythmRules.ChoiceAt(back, Vilnius(17, 9, 1)).ShouldBe(Frequencies.Daily, "what was seen during the mis-click too");
    }

    /// <summary>The same for an item still waiting under "Iš karto" (the one-a-day limit of planned dates).</summary>
    [Fact]
    public void Going_back_to_Is_karto_keeps_what_waited_under_it()
    {
        var daily = RhythmRules.Change(RhythmChoice.Default, Frequencies.Daily, Vilnius(17, 9));
        var back = RhythmRules.Change(daily, Frequencies.Immediate, Vilnius(17, 9, 1));
        RhythmRules.ChoiceAt(back, Vilnius(16, 20)).ShouldBe(Frequencies.Immediate);
        RhythmRules.ChoiceAt(back, Vilnius(17, 9, 0)).ShouldBe(Frequencies.Immediate, "seen during the mis-click: told with the runs as before");

        var weekly = Chose(Frequencies.Weekly, Vilnius(10, 9), Frequencies.Immediate, null);
        var twice = RhythmRules.Change(RhythmRules.Change(weekly, Frequencies.Immediate, Vilnius(17, 9)), Frequencies.Weekly, Vilnius(17, 9, 1));
        RhythmRules.ChoiceAt(twice, Vilnius(16, 20)).ShouldBe(Frequencies.Weekly, "back to the weekly choice it waited for");
    }

    [Fact]
    public void Picking_the_same_choice_again_changes_nothing()
    {
        var daily = Chose(Frequencies.Daily, Vilnius(10, 9), Frequencies.Weekly, Vilnius(1, 9));
        RhythmRules.Change(daily, Frequencies.Daily, Vilnius(17, 9)).ShouldBeSameAs(daily);
    }

    [Theory]
    [InlineData("Immediate", "immediate", true)]
    [InlineData("Immediate", "daily", false)]
    [InlineData("Daily", "daily", true)]
    [InlineData("Daily", "weekly", false)]
    [InlineData("Weekly", "weekly", true)]
    [InlineData("Early", "daily", true)]
    [InlineData("Early", "weekly", true)]
    [InlineData("Early", "immediate", false)]
    [InlineData("Weekly", null, false)]
    public void Each_email_serves_its_own_choice_and_the_early_one_both_gathered_choices(string pass, string? choice, bool serves) =>
        RhythmRules.Serves(Enum.Parse<Pass>(pass), choice).ShouldBe(serves);

    [Fact]
    public void The_daily_email_is_cut_at_the_morning_hour_of_the_Vilnius_day()
    {
        RhythmRules.DailyCut(Vilnius(17, 8, 7), Eight).ShouldBe(Vilnius(17, 8));
        RhythmRules.DailyCut(Vilnius(17, 7, 59), Eight).ShouldBe(Vilnius(16, 8), "before the hour the last cut is yesterday's");
        RhythmRules.DailyCut(new DateTimeOffset(2026, 11, 17, 6, 0, 0, TimeSpan.Zero), Eight).ShouldBe(Vilnius(17, 8), "a Vilnius hour whatever the server's zone");
    }

    [Fact]
    public void The_weekly_email_is_cut_on_Monday_at_the_morning_hour()
    {
        RhythmRules.WeeklyCut(Vilnius(16, 8, 7), Eight).ShouldBe(Vilnius(16, 8));
        RhythmRules.WeeklyCut(Vilnius(16, 7, 59), Eight).ShouldBe(Vilnius(9, 8), "before Monday's hour it is last Monday's");
        RhythmRules.WeeklyCut(Vilnius(22, 23), Eight).ShouldBe(Vilnius(16, 8), "Sunday still belongs to the week begun on Monday");
    }

    [Fact]
    public void A_gathered_email_holds_only_what_was_first_seen_before_its_cut()
    {
        RhythmRules.InGathered(Vilnius(17, 7, 52), Vilnius(17, 8)).ShouldBeTrue();
        RhythmRules.InGathered(Vilnius(17, 8, 7), Vilnius(17, 8)).ShouldBeFalse("seen in the run of 08:07: the next morning");
    }

    [Fact]
    public void The_next_gathered_email_is_the_next_morning_or_the_next_Monday_and_none_under_Is_karto()
    {
        RhythmRules.NextGathered(Frequencies.Daily, Vilnius(17, 7), Eight).ShouldBe(Vilnius(17, 8));
        RhythmRules.NextGathered(Frequencies.Daily, Vilnius(17, 8), Eight).ShouldBe(Vilnius(18, 8));
        RhythmRules.NextGathered(Frequencies.Weekly, Vilnius(17, 9), Eight).ShouldBe(Vilnius(23, 8));
        RhythmRules.NextGathered(Frequencies.Weekly, Vilnius(16, 7), Eight).ShouldBe(Vilnius(16, 8));
        RhythmRules.NextGathered(Frequencies.Immediate, Vilnius(17, 9), Eight).ShouldBeNull();
    }

    [Fact]
    public void A_planned_sitting_on_or_before_the_day_of_the_next_daily_email_cannot_wait()
    {
        // Seen on Tuesday at 20:00: the next daily email is Wednesday morning.
        RhythmRules.CannotWait(new DateOnly(2026, 11, 18), Frequencies.Daily, Vilnius(17, 20), Eight).ShouldBeTrue("the sitting's own morning is too late");
        RhythmRules.CannotWait(new DateOnly(2026, 11, 17), Frequencies.Daily, Vilnius(17, 20), Eight).ShouldBeTrue();
        RhythmRules.CannotWait(new DateOnly(2026, 11, 19), Frequencies.Daily, Vilnius(17, 20), Eight).ShouldBeFalse("Wednesday's email tells it in time");
    }

    [Fact]
    public void For_the_weekly_choice_any_sitting_up_to_the_next_Monday_cannot_wait()
    {
        RhythmRules.CannotWait(new DateOnly(2026, 11, 20), Frequencies.Weekly, Vilnius(17, 9), Eight).ShouldBeTrue("Friday comes before Monday's email");
        RhythmRules.CannotWait(new DateOnly(2026, 11, 23), Frequencies.Weekly, Vilnius(17, 9), Eight).ShouldBeTrue("the Monday of the email itself");
        RhythmRules.CannotWait(new DateOnly(2026, 11, 24), Frequencies.Weekly, Vilnius(17, 9), Eight).ShouldBeFalse();
    }

    [Fact]
    public void Nothing_is_early_under_Is_karto_which_tells_it_at_once_anyway() =>
        RhythmRules.CannotWait(new DateOnly(2026, 11, 17), Frequencies.Immediate, Vilnius(17, 9), Eight).ShouldBeFalse();
}
