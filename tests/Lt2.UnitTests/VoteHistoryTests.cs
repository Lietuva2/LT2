using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client;
using Lt2.Web.Client.Parliament;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.18: the history of the Seimas's votes, its state in the address with fallbacks, its rows by Vilnius day, and
/// where "Rodyti daugiau" and "Senesni balsavimai" lead.
/// </summary>
public sealed class VoteHistoryTests
{
    private static readonly VoteHistoryState Plain = VoteHistory.Read(null, null, null, null);

    /// <summary>A vote held at a Vilnius time (in November Vilnius is UTC+2).</summary>
    private static VoteListItem Vote(long id, int year, int month, int day, int hour) =>
        new(new VoteSummaryResponse(
                new VoteLink(id, new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.FromHours(2)), "Pritarti", "stage-result", "adoption", false, true, new BillLink("XVP-0412", "Pataisa")),
                new VoteTotalsResponse(70, 30, 10, 110, 141),
                null),
            "Klausimas");

    /// <summary><paramref name="count"/> votes, <paramref name="perDay"/> a day, newest first, from 2026-11-13 back.</summary>
    private static List<VoteListItem> Votes(int count, int perDay)
    {
        var start = new DateOnly(2026, 11, 13);
        return Enumerable.Range(0, count)
            .Select(i => { var d = start.AddDays(-(i / perDay)); return Vote(1000 - i, d.Year, d.Month, d.Day, 18 - i % perDay % 10); })
            .ToList();
    }

    [Fact]
    public void The_plain_address_is_the_stage_results_from_the_latest_vote_30_at_a_time()
    {
        Plain.ShouldBe(new VoteHistoryState(null, "etapai", null, 30));
        Plain.ApiKind.ShouldBe(VoteListQuery.StageResults);
        Plain.Filtered.ShouldBeFalse();
        VoteHistory.Address(Plain).ShouldBe("/seimas/balsavimai");
    }

    [Theory]
    [InlineData("priemimai", VoteListQuery.Adoptions)]
    [InlineData("visi", VoteListQuery.All)]
    [InlineData("etapai", VoteListQuery.StageResults)]
    public void Each_choice_of_the_dropdown_asks_the_api_for_its_kind(string kindSlug, string kind) =>
        VoteHistory.Read(null, kindSlug, null, null).ApiKind.ShouldBe(kind);

    [Theory]
    [InlineData(null, "nera", "2026-13-01", "abc")]
    [InlineData("  ", "VISI", "2026-1-5", "100000")]
    [InlineData("", "<b>", "vakar", "-30")]
    [InlineData(null, "", "2026-11-13T00:00", "60.5")]
    [InlineData(null, null, "", "29")]
    [InlineData(null, null, null, "301")]
    public void Unknown_malformed_or_oversized_values_fall_back_to_the_defaults(string? q, string? kindSlug, string? until, string? show) =>
        VoteHistory.Read(q, kindSlug, until, show).ShouldBe(Plain);

    [Fact]
    public void An_oversized_search_is_no_search() =>
        VoteHistory.Read(new string('a', 101), null, null, null).Query.ShouldBeNull();

    [Fact]
    public void The_state_is_read_back_into_the_same_address()
    {
        var state = VoteHistory.Read(" Žuvininkystės ", "visi", " 2026-05-19 ", "90");

        state.ShouldBe(new VoteHistoryState("Žuvininkystės", "visi", new DateOnly(2026, 5, 19), 90));
        state.Filtered.ShouldBeTrue();
        VoteHistory.Address(state, "vote-61").ShouldBe("/seimas/balsavimai?q=%C5%BDuvininkyst%C4%97s&kas=visi&iki=2026-05-19&rodyti=90#vote-61");
        VoteHistory.Read(null, null, null, "300").Shown.ShouldBe(300);
    }

    [Fact]
    public void Rows_fall_into_vilnius_days_newest_first_each_day_once_and_numbered_through()
    {
        // 23:30 UTC on 12 November is already 13 November in Vilnius.
        var late = new VoteListItem(Vote(1, 2026, 11, 13, 1).Summary with
        {
            Vote = Vote(1, 2026, 11, 13, 1).Summary.Vote with { HeldAt = new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.Zero) },
        }, "Klausimas");
        var votes = new List<VoteListItem> { Vote(5, 2026, 11, 13, 15), late, Vote(3, 2026, 11, 12, 18), Vote(2, 2026, 11, 12, 11), Vote(4, 2026, 3, 17, 10) };

        var days = VoteHistory.Days(votes);

        days.Select(d => d.Day).ShouldBe([new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 12), new DateOnly(2026, 3, 17)]);
        days[0].Rows.Select(r => r.Vote.Summary.Vote.Id).ShouldBe([5L, 1L]);
        days.SelectMany(d => d.Rows).Select(r => r.Number).ShouldBe([1, 2, 3, 4, 5]);
        VoteHistory.Days([]).ShouldBeEmpty();
    }

    [Fact]
    public void More_adds_30_landing_on_the_first_row_added_and_keeps_the_rest_of_the_state()
    {
        var state = VoteHistory.Read("pajamu", "visi", "2026-05-19", null);

        VoteHistory.More(state, 30, 95).ShouldBe("/seimas/balsavimai?q=pajamu&kas=visi&iki=2026-05-19&rodyti=80#vote-31");
        VoteHistory.More(state with { Shown = 80 }, 80, 95).ShouldBe("/seimas/balsavimai?q=pajamu&kas=visi&iki=2026-05-19&rodyti=130#vote-81");
        VoteHistory.More(state with { Shown = 130 }, 95, 95).ShouldBeNull("every vote found is shown");
        VoteHistory.More(Plain, 30, 30).ShouldBeNull();
        VoteHistory.More(Plain with { Shown = 285 }, 285, 1000).ShouldBe("/seimas/balsavimai?rodyti=300#vote-286", "never beyond 300");
        VoteHistory.More(Plain with { Shown = 300 }, 300, 1000).ShouldBeNull("a page shows at most 300");
    }

    [Fact]
    public void After_300_older_starts_again_from_the_day_of_the_last_vote_shown()
    {
        var votes = Votes(300, 7);
        var lastDay = Display.DayOf(votes[^1].Summary.Vote.HeldAt);
        var state = VoteHistory.Read("pajamu", "priemimai", null, "300");

        VoteHistory.Older(state, votes, 301).ShouldBe($"/seimas/balsavimai?q=pajamu&kas=priemimai&iki={Display.Day(lastDay)}#votes");
        VoteHistory.Older(state, votes, 300).ShouldBeNull("nothing older");
        VoteHistory.Older(state with { Shown = 270 }, votes.Take(270).ToList(), 400).ShouldBeNull("more is offered first");
    }

    [Fact]
    public void When_all_300_fall_on_one_day_older_starts_from_the_day_before()
    {
        var votes = Votes(300, 400);

        VoteHistory.Older(Plain with { Shown = 300 }, votes, 500).ShouldBe("/seimas/balsavimai?iki=2026-11-12#votes");
    }

    [Theory]
    [InlineData("stage-results", "stage-results")]
    [InlineData("adoptions", "adoptions")]
    [InlineData("all", "all")]
    [InlineData(null, "stage-results")]
    [InlineData("ALL", "stage-results")]
    [InlineData("visi", "stage-results")]
    public void The_api_reads_an_unknown_kind_as_the_stage_results(string? kind, string expected) =>
        VoteListQuery.Kind(kind).ShouldBe(expected);

    [Theory]
    [InlineData(null, 30)]
    [InlineData("abc", 30)]
    [InlineData("0", 30)]
    [InlineData("-5", 30)]
    [InlineData("12", 12)]
    [InlineData("300", 300)]
    [InlineData("100000", 300)]
    [InlineData("99999999999999", 30)]
    public void The_api_limit_is_at_least_1_and_at_most_300(string? limit, int expected) =>
        VoteListQuery.Limit(limit).ShouldBe(expected);

    [Fact]
    public void The_api_day_is_yyyy_mm_dd_or_none()
    {
        VoteListQuery.Until("2026-05-19").ShouldBe(new DateOnly(2026, 5, 19));
        VoteListQuery.Until("19.05.2026").ShouldBeNull();
        VoteListQuery.Until(null).ShouldBeNull();
    }
}
