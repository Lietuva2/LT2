using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.3, "Projektai be aprašymo" (owner, 2026-11-25): active bills without a description, or with an AI summary nobody
/// has read, that people already pay attention to; the most attention first, then the soonest planned date, then the number.
/// </summary>
public sealed class UndescribedRuleTests
{
    private sealed record Row(string Number, int Participants, int Arguments, int Followers, DateOnly? Planned);

    private static List<string> Order(params Row[] rows) =>
        UndescribedRules.Order(rows.Where(r => UndescribedRules.IsListed(r.Participants, r.Arguments, r.Followers)), r => r.Participants, r => r.Arguments, r => r.Followers, r => r.Planned, r => r.Number).Select(r => r.Number).ToList();

    [Theory]
    [InlineData("", null, true)]
    [InlineData("Santrauka.", DescriptionSources.Ai, true)]
    [InlineData("Aprašymas.", DescriptionSources.AiDraft, false)]
    [InlineData("Aprašymas.", DescriptionSources.Moderator, false)]
    public void A_bill_wants_a_description_without_one_or_with_an_AI_summary_nobody_has_read(string description, string? source, bool wanted) =>
        UndescribedRules.WantsDescription(description, source).ShouldBe(wanted);

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(1, 0, 0, true)]
    [InlineData(0, 1, 0, true)]
    [InlineData(0, 0, 1, true)]
    public void A_bill_is_listed_when_it_has_any_activity_participants_arguments_or_followers(int participants, int arguments, int followers, bool listed) =>
        UndescribedRules.IsListed(participants, arguments, followers).ShouldBe(listed);

    [Fact]
    public void A_bill_nobody_touched_is_not_on_the_list_whatever_its_planned_date() =>
        Order(new Row("XVP-0412", 0, 0, 0, new DateOnly(2026, 11, 26)), new Row("XVP-0533", 0, 0, 1, null)).ShouldBe(["XVP-0533"]);

    [Fact]
    public void A_participant_an_argument_or_a_follower_alone_puts_a_bill_on_the_list() =>
        Order(new Row("XVP-0412", 1, 0, 0, null), new Row("XVP-0533", 0, 1, 0, null), new Row("XVP-0618", 0, 0, 1, null))
            .ShouldBe(["XVP-0412", "XVP-0533", "XVP-0618"]);

    [Fact]
    public void The_most_attention_comes_first_whatever_its_kind() =>
        Order(new Row("XVP-0412", 2, 0, 0, null), new Row("XVP-0533", 0, 0, 5, null), new Row("XVP-0618", 1, 1, 1, null))
            .ShouldBe(["XVP-0533", "XVP-0618", "XVP-0412"]);

    [Fact]
    public void Equal_attention_goes_by_the_soonest_planned_date_bills_without_one_last_then_by_number() =>
        Order(
                new Row("XVP-0618", 2, 0, 0, null),
                new Row("XVP-0533", 1, 1, 0, new DateOnly(2026, 12, 3)),
                new Row("XVP-0412", 0, 0, 2, new DateOnly(2026, 11, 26)),
                new Row("XVP-0100", 0, 2, 0, null))
            .ShouldBe(["XVP-0412", "XVP-0533", "XVP-0100", "XVP-0618"]);
}
