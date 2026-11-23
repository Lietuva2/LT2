using Lt2.Web.Client;
using Lt2.Web.Client.Agenda;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.11.1, "Days and their length", and 8.7 (owner, 2026-11-19: one way for every list): the agenda page and the front
/// page show the first rows of a day, and the shared "Rodyti daugiau" grows that day by the address
/// (<c>?diena=2026-11-24:60</c>: the first 10 and 50 more, owner 2026-11-19), the days already grown stay so; a value the page would not
/// make is left out.
/// </summary>
public sealed class AgendaPagingTests
{
    private static readonly DateOnly Tuesday = new(2026, 11, 24);
    private static readonly DateOnly Thursday = new(2026, 11, 26);
    private static readonly DateOnly Voted = new(2026, 11, 17);
    private static readonly Dictionary<DateOnly, int> None = [];

    [Fact]
    public void Rodyti_daugiau_grows_that_planned_day_by_the_address_without_a_fragment() =>
        AgendaPaging.MorePlanned(AgendaPaging.Agenda, None, None, Tuesday, 60)
            .ShouldBe("/darbotvarke?diena=2026-11-24:60");

    [Fact]
    public void Rodyti_daugiau_under_a_voted_day_grows_that_voting_day() =>
        AgendaPaging.MoreVoted(AgendaPaging.Agenda, None, None, Voted, 60)
            .ShouldBe("/darbotvarke?balsuota=2026-11-17:60");

    [Fact]
    public void The_front_page_grows_its_days_in_place_by_the_same_address()
    {
        AgendaPaging.MorePlanned(AgendaPaging.Front, None, None, Tuesday, 55)
            .ShouldBe("/?diena=2026-11-24:55");
        AgendaPaging.MoreVoted(AgendaPaging.Front, None, None, Voted, 55).ShouldBe("/?balsuota=2026-11-17:55");
    }

    [Fact]
    public void The_days_already_grown_stay_so_and_the_grown_day_takes_its_new_number() =>
        AgendaPaging.MorePlanned(
                AgendaPaging.Agenda, new Dictionary<DateOnly, int> { [Thursday] = 60, [Tuesday] = 60 }, new Dictionary<DateOnly, int> { [Voted] = 110 },
                Tuesday, 110)
            .ShouldBe("/darbotvarke?diena=2026-11-24:110&diena=2026-11-26:60&balsuota=2026-11-17:110");

    [Fact]
    public void Without_days_the_address_is_the_page() => AgendaPaging.Address(AgendaPaging.Front, None, None).ShouldBe("/");

    [Fact]
    public void The_address_reads_a_day_and_a_whole_number_only_and_a_day_named_twice_keeps_the_larger()
    {
        var read = AgendaPaging.Read(["2026-11-26:20", " 2026-11-24:15 ", "2026-11-24:30", "2026-11-24", "rytoj:20", "2026-13-01:20",
            "2026-11-25:0", "2026-11-25:-5", "2026-11-25:daug", "2026-11-25:20:30", "", null]);
        read.ShouldBe(new Dictionary<DateOnly, int> { [Tuesday] = 30, [Thursday] = 20 });
        AgendaPaging.Read(null).ShouldBeEmpty();
    }

    [Fact]
    public void The_address_reads_at_most_a_month_of_days() =>
        AgendaPaging.Read(Enumerable.Range(0, 100).Select(i => Display.Day(Tuesday.AddDays(i)) + ":20")).Count.ShouldBe(AgendaPaging.MaxDays);

    [Fact]
    public void The_links_carry_on_only_the_days_the_page_shows_grown_beyond_their_first_rows() =>
        AgendaPaging.Grown([(Tuesday, 10), (Thursday, 20), (Voted, 5)], AgendaRules.PagePerDay)
            .ShouldBe(new Dictionary<DateOnly, int> { [Thursday] = 20 });

    [Fact]
    public void A_row_id_is_the_rows_own_on_both_pages()
    {
        AgendaPaging.PlannedRowId(Tuesday, "XVP-0412").ShouldBe("agenda-2026-11-24-XVP-0412");
        AgendaPaging.VotedRowId(5123).ShouldBe("recent-5123");
    }
}
