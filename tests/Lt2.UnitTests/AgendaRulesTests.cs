using Lt2.Api.Contracts.Me;
using Lt2.Web.Client.Agenda;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.11.1, 8.7 (owner, 2026-11-18): the order within a sitting or voting day and the lists' lengths. The agenda has no
/// order of its own within a day, so a day is ordered by activity, then stage, then number; bills the open data does not
/// know yet come last.
/// </summary>
public sealed class AgendaRulesTests
{
    private sealed record Row(string Number, bool Known, int Participants, int Arguments, string? Stage);

    private static List<string> Order(params Row[] rows) =>
        AgendaRules.WithinDay(rows, r => r.Known, r => AgendaRules.Activity(r.Participants, r.Arguments), r => r.Stage, r => r.Number)
            .Select(r => r.Number).ToList();

    [Fact]
    public void Activity_is_participants_plus_published_arguments() => AgendaRules.Activity(6800 + 4280, 12).ShouldBe(11092);

    [Fact]
    public void A_day_puts_the_most_activity_first_counting_arguments_as_participants()
    {
        Order(
                new Row("XVP-0001", true, 3, 0, "adoption"),
                new Row("XVP-0002", true, 2, 2, "presentation"),
                new Row("XVP-0003", true, 0, 0, "adoption"))
            .ShouldBe(["XVP-0002", "XVP-0001", "XVP-0003"]);
    }

    [Fact]
    public void Equal_activity_goes_by_stage_adoption_then_consideration_then_presentation_then_none()
    {
        Order(
                new Row("XVP-0001", true, 0, 0, null),
                new Row("XVP-0002", true, 0, 0, "presentation"),
                new Row("XVP-0003", true, 0, 0, "consideration"),
                new Row("XVP-0004", true, 0, 0, "adoption"))
            .ShouldBe(["XVP-0004", "XVP-0003", "XVP-0002", "XVP-0001"]);
    }

    [Fact]
    public void Equal_activity_and_stage_go_by_number()
    {
        Order(new Row("XVP-0412", true, 1, 0, "adoption"), new Row("XVP-0118", true, 1, 0, "adoption")).ShouldBe(["XVP-0118", "XVP-0412"]);
    }

    [Fact]
    public void Bills_the_open_data_does_not_know_yet_come_after_every_known_one()
    {
        Order(new Row("XVP-0001", false, 0, 0, "adoption"), new Row("XVP-0900", true, 0, 0, null)).ShouldBe(["XVP-0900", "XVP-0001"]);
    }

    [Theory]
    [InlineData(3, 3, 0)]
    [InlineData(10, 10, 0)]
    [InlineData(28, 10, 18)]
    public void A_day_shows_its_first_rows_and_keeps_the_rest_and_the_unknown_apart(int known, int shown, int rest)
    {
        var rows = Enumerable.Range(1, known).Select(i => new Row($"XVP-{i:0000}", true, 0, 0, null))
            .Concat(Enumerable.Range(1, 12).Select(i => new Row($"XVP-9{i:000}", false, 0, 0, null))).ToList();

        var day = AgendaRules.Day(rows, r => r.Known, AgendaRules.PagePerDay);

        day.Shown.Count.ShouldBe(shown);
        day.Rest.Count.ShouldBe(rest);
        day.Unknown.Count.ShouldBe(12);
        day.Known.ShouldBe(known);
        day.Count.ShouldBe(known + 12);
        day.Shown.Concat(day.Rest).ShouldAllBe(r => r.Known);
    }

    [Fact]
    public void The_front_page_shows_two_days_of_five_and_the_agenda_page_ten_a_day()
    {
        AgendaRules.FrontDays.ShouldBe(2);
        AgendaRules.FrontPerDay.ShouldBe(5);
        AgendaRules.PagePerDay.ShouldBe(10);
    }

    [Fact]
    public void The_front_pages_islands_ask_about_each_shown_bill_once_and_never_more_than_one_request_takes()
    {
        AgendaRules.Asked(["XVP-0001", "xvp-0001", "XVP-0002"]).ShouldBe(["XVP-0001", "XVP-0002"]);

        // Two planned and two voting days of five rows each: within the request's limit with room to spare.
        (2 * AgendaRules.FrontDays * AgendaRules.FrontPerDay).ShouldBeLessThanOrEqualTo(MyBillsQuery.MaxNumbers);
        var many = Enumerable.Range(1, MyBillsQuery.MaxNumbers + 10).Select(i => $"XVP-{i:0000}");
        AgendaRules.Asked(many).Count.ShouldBe(MyBillsQuery.MaxNumbers);
    }
}
