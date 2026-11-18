using Lt2.Web.Client;
using Lt2.Web.Client.Agenda;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.11.1, "Days and their length", and 8.7 (owner, 2026-11-18): the agenda page and the front page show the first
/// rows of a day, and "Rodyti likusius" fetches the rest of that day by the address, landing on the first row added; the
/// days already opened stay open.
/// </summary>
public sealed class AgendaPagingTests
{
    private static readonly DateOnly Tuesday = new(2026, 11, 24);
    private static readonly DateOnly Thursday = new(2026, 11, 26);
    private static readonly DateOnly Voted = new(2026, 11, 17);
    private static readonly HashSet<DateOnly> None = [];

    [Fact]
    public void Rodyti_likusius_opens_that_planned_day_whole_landing_on_its_first_row_added() =>
        AgendaPaging.RestOfPlanned(AgendaPaging.Agenda, None, None, Tuesday)
            .ShouldBe("/darbotvarke?diena=2026-11-24#diena-2026-11-24-likusieji");

    [Fact]
    public void Rodyti_likusius_under_a_voted_day_opens_that_voting_day() =>
        AgendaPaging.RestOfVoted(AgendaPaging.Agenda, None, None, Voted)
            .ShouldBe("/darbotvarke?balsuota=2026-11-17#balsuota-2026-11-17-likusieji");

    [Fact]
    public void The_front_page_opens_its_days_in_place_by_the_same_address()
    {
        AgendaPaging.RestOfPlanned(AgendaPaging.Front, None, None, Tuesday).ShouldBe("/?diena=2026-11-24#home-day-2026-11-24-likusieji");
        AgendaPaging.RestOfVoted(AgendaPaging.Front, None, None, Voted).ShouldBe("/?balsuota=2026-11-17#home-voted-2026-11-17-likusieji");
    }

    [Fact]
    public void The_days_already_opened_stay_open() =>
        AgendaPaging.RestOfPlanned(AgendaPaging.Agenda, new HashSet<DateOnly> { Thursday }, new HashSet<DateOnly> { Voted }, Tuesday)
            .ShouldBe("/darbotvarke?diena=2026-11-24&diena=2026-11-26&balsuota=2026-11-17#diena-2026-11-24-likusieji");

    [Fact]
    public void Without_days_the_address_is_the_page() => AgendaPaging.Address(AgendaPaging.Front, [], []).ShouldBe("/");

    [Fact]
    public void The_address_reads_well_formed_days_only_each_once()
    {
        AgendaPaging.Read(["2026-11-26", " 2026-11-24 ", "2026-11-24", "rytoj", "2026-13-01", "", null])
            .ShouldBe([Tuesday, Thursday]);
        AgendaPaging.Read(null).ShouldBeEmpty();
    }

    [Fact]
    public void The_address_reads_at_most_a_month_of_days() =>
        AgendaPaging.Read(Enumerable.Range(0, 100).Select(i => Display.Day(Tuesday.AddDays(i)))).Count.ShouldBe(AgendaPaging.MaxDays);
}
