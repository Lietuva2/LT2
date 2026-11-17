using Lt2.Appeals.Domain;
using Lt2.Notifications.Follows;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.20.1, "Seimo narių atsakymai": what is an event, how an answer is recorded (design 8.24: per answer, with the
/// three-day rule), and who is told about which answers.
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
    public void An_answer_is_keyed_by_the_MP_and_the_moment_its_stance_became_public()
    {
        var at = Vilnius(16, 10);
        FollowRules.ResponseOf(FollowRules.ResponseKey(Petraitis, at)).ShouldBe((Petraitis, at));
        FollowRules.ResponseKey(Petraitis, at).Length.ShouldBeLessThanOrEqualTo(40, "bill_event.key holds 40 characters");
        FollowRules.ResponseKey(Petraitis, at.AddMinutes(1)).ShouldNotBe(FollowRules.ResponseKey(Petraitis, at), "a stance changed again is another event");
        FollowRules.ResponseKey(Jonaitiene, at).ShouldNotBe(FollowRules.ResponseKey(Petraitis, at));
    }

    [Fact]
    public void An_answer_first_seen_more_than_three_days_after_it_became_public_is_never_sent()
    {
        FollowRules.ResponseIsSendable(Vilnius(13, 10), Vilnius(16, 10)).ShouldBeTrue();
        FollowRules.ResponseIsSendable(Vilnius(13, 10), Vilnius(16, 10, 1)).ShouldBeFalse();
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
