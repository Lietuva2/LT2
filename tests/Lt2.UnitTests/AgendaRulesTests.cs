using Lt2.Api.Contracts.Me;
using Lt2.Appeals.Domain;
using Lt2.Web.Client.Agenda;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.11.1, 8.7 (owner, 2026-11-18): the order within a sitting or voting day and the lists' lengths. The agenda has no
/// order of its own within a day, so a day is ordered by activity, then stage, then number; bills the open data does not
/// know yet come last.
/// </summary>
public sealed class AgendaRulesTests
{
    private sealed record Row(string Number, bool Known, int Participants, int Arguments, string? Stage, int Followers = 0);

    private static List<string> Order(params Row[] rows) =>
        AgendaRules.WithinDay(rows, r => r.Known, r => BillListRules.Activity(r.Participants, r.Arguments, r.Followers), r => r.Stage, r => r.Number)
            .Select(r => r.Number).ToList();

    [Fact]
    public void A_day_counts_followers_into_activity_as_participants_and_arguments()
    {
        Order(
                new Row("XVP-0001", true, 2, 0, "adoption"),
                new Row("XVP-0002", true, 0, 0, "presentation", Followers: 3),
                new Row("XVP-0003", true, 1, 1, "adoption", Followers: 0))
            .ShouldBe(["XVP-0002", "XVP-0001", "XVP-0003"]);
    }

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
    public void The_front_pages_islands_ask_about_each_shown_bill_once_in_requests_the_api_takes()
    {
        AgendaRules.Asked(["XVP-0001", "xvp-0001", "XVP-0002"]).ShouldBe(["XVP-0001", "XVP-0002"]);

        // Two planned and two voting days of five rows each: one request, with room to spare.
        (2 * AgendaRules.FrontDays * AgendaRules.FrontPerDay).ShouldBeLessThanOrEqualTo(MyBillsQuery.MaxNumbers);

        // A day grown by "Rodyti daugiau" adds its rows: every one is asked about, a request per thirty.
        var many = AgendaRules.Asked(Enumerable.Range(1, MyBillsQuery.MaxNumbers + 10).Select(i => $"XVP-{i:0000}"));
        many.Count.ShouldBe(MyBillsQuery.MaxNumbers + 10);
        MyAgendaState.Requests(many).Select(r => r.Count).ShouldBe([MyBillsQuery.MaxNumbers, 10]);
    }

    [Theory]
    [InlineData(10, null, 40, 10)]
    [InlineData(10, 20, 40, 20)]
    [InlineData(5, 15, 40, 15)]
    [InlineData(10, 50, 40, 40)]
    [InlineData(10, 7, 40, 10)]
    [InlineData(10, 10, 40, 10)]
    [InlineData(10, 0, 40, 10)]
    [InlineData(10, null, 3, 3)]
    [InlineData(10, 5000, 2000, AgendaRules.MaxAsked)]
    public void A_day_shows_its_first_rows_or_as_many_as_the_address_asks_beyond_them_never_more_than_it_has(int first, int? asked, int known, int shown) =>
        AgendaRules.ShownKnown(first, asked, known).ShouldBe(shown);

    [Theory]
    [InlineData(10, 100, 60)]
    [InlineData(5, 100, 55)]
    [InlineData(60, 100, 110)]
    [InlineData(10, 40, 60)]
    [InlineData(35, 40, 85)]
    [InlineData(40, 40, null)]
    [InlineData(3, 3, null)]
    [InlineData(990, 2000, AgendaRules.MaxAsked)]
    public void Rodyti_daugiau_asks_for_fifty_more_rows_until_every_known_row_shows(int shown, int known, int? next) =>
        AgendaRules.NextAsked(shown, known).ShouldBe(next);

    [Fact]
    public void Rodyti_daugiau_adds_fifty_rows_a_click_on_both_pages() => AgendaRules.MorePerDay.ShouldBe(50);

    [Theory]
    [InlineData(10, 40, true)]
    [InlineData(10, 11, true)]
    [InlineData(10, 10, false)]
    [InlineData(5, 3, false)]
    public void Only_a_day_whose_rows_do_not_all_fit_its_first_page_carries_the_shown_line(int first, int known, bool paged) =>
        AgendaRules.Paged(first, known).ShouldBe(paged);

    [Theory]
    [InlineData(28, 10, 10, false)]
    [InlineData(28, 20, 20, false)]
    [InlineData(28, 28, 40, true)]
    [InlineData(3, 10, 15, true)]
    public void Rodoma_counts_the_bills_the_open_data_does_not_know_yet_once_their_line_shows(int known, int asked, int shownCount, bool unknownShown)
    {
        var rows = Enumerable.Range(1, known).Select(i => new Row($"XVP-{i:0000}", true, 0, 0, null))
            .Concat(Enumerable.Range(1, 12).Select(i => new Row($"XVP-9{i:000}", false, 0, 0, null))).ToList();

        var day = AgendaRules.Day(rows, r => r.Known, AgendaRules.ShownKnown(AgendaRules.PagePerDay, asked, known));

        day.ShownCount.ShouldBe(shownCount);
        day.UnknownShown.ShouldBe(unknownShown);
        day.Count.ShouldBe(known + 12, "the M of \"Rodoma N iš M\" is the day's head count");
    }
}