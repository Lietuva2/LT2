using Lt2.Appeals.Domain;
using Lt2.Notifications.Follows;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.20.1, "Seimo narių atsakymai": what is an event, when the day's email goes, one email per bill per day, and who is
/// told about which answers.
/// </summary>
public sealed class ResponseNoticeRuleTests
{
    private const long Petraitis = 1001;
    private const long Jonaitiene = 1002;

    // 2026-11-17 in Vilnius (UTC+2 in November).
    private static DateTimeOffset Vilnius(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(2));

    private static PublicResponseFacts Answer(long mp, DateTimeOffset at, string stance = "for") => new("XVP-0412", mp, stance, at);

    [Fact]
    public void A_first_publication_is_an_event() => ResponseRules.Announces(null, "for").ShouldBeTrue();

    [Fact]
    public void A_changed_stance_is_an_event_also_when_written_again_after_a_withdrawal() =>
        ResponseRules.Announces("for", "against").ShouldBeTrue();

    [Fact]
    public void A_changed_explanation_with_the_same_stance_is_no_event() => ResponseRules.Announces("undecided", "undecided").ShouldBeFalse();

    [Fact]
    public void The_day_before_is_due_from_the_morning_time_on_and_nothing_before_it()
    {
        var eight = new TimeOnly(8, 0);
        FollowRules.ResponsesDayDue(Vilnius(17, 7, 59), eight).ShouldBeNull();
        FollowRules.ResponsesDayDue(Vilnius(17, 8, 0), eight).ShouldBe(new DateOnly(2026, 11, 16));
        FollowRules.ResponsesDayDue(Vilnius(17, 23, 59), eight).ShouldBe(new DateOnly(2026, 11, 16));
    }

    [Fact]
    public void The_morning_time_is_a_Vilnius_time_whatever_the_server_zone()
    {
        // 06:30 UTC is 08:30 in Vilnius in November.
        FollowRules.ResponsesDayDue(new DateTimeOffset(2026, 11, 17, 6, 30, 0, TimeSpan.Zero), new TimeOnly(8, 0)).ShouldBe(new DateOnly(2026, 11, 16));
        FollowRules.ResponsesDayDue(new DateTimeOffset(2026, 11, 17, 5, 30, 0, TimeSpan.Zero), new TimeOnly(8, 0)).ShouldBeNull();
    }

    [Fact]
    public void The_setting_defaults_to_eight_in_the_morning()
    {
        SettingKeys.FollowsResponsesAt.Default.ShouldBe("08:00");
        FollowRules.ResponsesAt("08:00").ShouldBe(new TimeOnly(8, 0));
        FollowRules.ResponsesAt("09:30").ShouldBe(new TimeOnly(9, 30));
        FollowRules.ResponsesAt("off").ShouldBe(new TimeOnly(8, 0));
        SettingKeys.FollowsResponsesAt.IsValid(System.Text.Json.JsonSerializer.SerializeToElement("off")).ShouldBeFalse("there is no off: the switch is the person's");
    }

    [Fact]
    public void One_email_per_bill_per_day_the_event_is_keyed_by_the_day()
    {
        FollowRules.ResponsesKey(new DateOnly(2026, 11, 16)).ShouldBe("2026-11-16");
        FollowRules.ResponsesKey(new DateOnly(2026, 11, 17)).ShouldNotBe(FollowRules.ResponsesKey(new DateOnly(2026, 11, 16)));
    }

    [Fact]
    public void A_follower_is_told_only_about_answers_from_after_they_followed()
    {
        var early = Answer(Petraitis, Vilnius(16, 10));
        var late = Answer(Jonaitiene, Vilnius(16, 16));
        FollowRules.ResponsesFor(Vilnius(16, 9), null, [early, late]).ShouldBe(new[] { early, late });
        FollowRules.ResponsesFor(Vilnius(16, 12), null, [early, late]).ShouldBe(new[] { late });
        FollowRules.ResponsesFor(Vilnius(16, 17), null, [early, late]).ShouldBeEmpty("no email for someone who followed later");
    }

    [Fact]
    public void The_MPs_own_account_and_office_are_not_told_about_their_own_MPs_answers()
    {
        var own = Answer(Petraitis, Vilnius(16, 10));
        var other = Answer(Jonaitiene, Vilnius(16, 11), "against");
        FollowRules.ResponsesFor(Vilnius(15, 9), Petraitis, [own, other]).ShouldBe(new[] { other });
        FollowRules.ResponsesFor(Vilnius(15, 9), Petraitis, [own]).ShouldBeEmpty();
    }
}
