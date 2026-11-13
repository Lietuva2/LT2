using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 8.3 (decided 2026-11-13): "may this person write now" under a moderator's restriction.</summary>
public sealed class WritingRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_person_without_a_restriction_may_write() =>
        WritingRules.MayWrite([], Now).ShouldBeTrue();

    [Fact]
    public void A_person_restricted_until_a_date_may_not_write_until_that_day_is_over_in_vilnius()
    {
        var endsAt = WritingRules.EndsAt(new DateOnly(2026, 11, 20));
        endsAt.ShouldBe(new DateTimeOffset(2026, 11, 20, 22, 0, 0, TimeSpan.Zero), "midnight after 20 November in Vilnius (UTC+2)");
        WritingRules.MayWrite([(endsAt, null)], Now).ShouldBeFalse();
        WritingRules.MayWrite([(endsAt, null)], endsAt.AddSeconds(-1)).ShouldBeFalse("the last day counts in full");
    }

    [Fact]
    public void A_person_restricted_until_lifted_may_not_write_however_long_ago_it_was_set() =>
        WritingRules.MayWrite([(null, null)], Now.AddYears(3)).ShouldBeFalse();

    [Fact]
    public void A_dated_restriction_ends_by_itself_when_its_last_day_is_over()
    {
        var endsAt = WritingRules.EndsAt(new DateOnly(2026, 11, 20));
        WritingRules.MayWrite([(endsAt, null)], endsAt).ShouldBeTrue();
    }

    [Fact]
    public void A_lifted_restriction_no_longer_stops_writing_whatever_its_length()
    {
        WritingRules.MayWrite([(null, Now.AddMinutes(-1))], Now).ShouldBeTrue();
        WritingRules.MayWrite([(WritingRules.EndsAt(new DateOnly(2027, 1, 1)), Now.AddMinutes(-1))], Now).ShouldBeTrue();
    }

    [Fact]
    public void One_restriction_that_holds_is_enough_whatever_earlier_ones_did()
    {
        var ended = WritingRules.EndsAt(new DateOnly(2026, 11, 1));
        WritingRules.MayWrite([(ended, null), (null, Now.AddDays(-2)), (null, null)], Now).ShouldBeFalse();
    }

    [Fact]
    public void The_last_day_shown_is_the_one_the_moderator_chose()
    {
        var day = new DateOnly(2026, 3, 29); // the night Lithuania moves to summer time
        WritingRules.Until(WritingRules.EndsAt(day)).ShouldBe(day);
        WritingRules.Until(null).ShouldBeNull("until lifted");
    }

    [Fact]
    public void A_last_day_in_the_past_is_refused_but_today_is_allowed()
    {
        WritingRules.CheckUntil(new DateOnly(2026, 11, 12), Now).ShouldBe("restriction.until-past");
        WritingRules.CheckUntil(new DateOnly(2026, 11, 13), Now).ShouldBeNull();
        WritingRules.CheckUntil(null, Now).ShouldBeNull("until lifted");
    }
}
