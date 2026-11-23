using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Client.Appeals;

namespace Lt2.UnitTests;

/// <summary>The arguments a page at a time (design 8.12.1, owner 2026-11-19): each side's column, grown, read again and pinned.</summary>
public sealed class ReasonPagingTests
{
    private static ReasonResponse Reason(int n, int marks = 0, string side = "for") =>
        new(Guid.Parse($"0193a1b2-0000-7000-8000-0000000005{n:D2}"), side, "Argumentas " + n, null, marks, "published", DateTimeOffset.UnixEpoch);

    private static ReasonPageResponse Column(int total, string? next, params ReasonResponse[] items) => new("for", items, total, next, []);

    [Fact]
    public void The_address_asks_for_a_whole_number_beyond_the_first_two_up_to_the_cap_else_the_first_two()
    {
        ReasonPaging.Asked(null).ShouldBe(2);
        ReasonPaging.Asked("12").ShouldBe(12);
        ReasonPaging.Asked("1").ShouldBe(2);
        ReasonPaging.Asked("-5").ShouldBe(2);
        ReasonPaging.Asked("visi").ShouldBe(2);
        ReasonPaging.Asked("999999").ShouldBe(ReasonPaging.Cap);
    }

    [Fact]
    public void A_column_from_the_bills_answer_holds_its_sides_first_ones_and_knows_where_the_next_page_starts()
    {
        var record = new BillRecordResponse(
            Guid.NewGuid(), new BillRefResponse("XVP-0412", "Pataisa"), "open", "", null, new SideCountsResponse(0, 0, 0, 0),
            new ThresholdResponse(100, "default", null, 0, false, false), [Reason(1), Reason(2), Reason(3, side: "against")], ReasonTotals: new ReasonTotalsResponse(15, 1));

        var column = ReasonPaging.FromRecord(record, "for");
        column.Items.Select(r => r.Id).ShouldBe([Reason(1).Id, Reason(2).Id]);
        (column.Total, column.NextCursor, ReasonPaging.Read(column), ReasonPaging.More(column)).ShouldBe((15, "2", 2, true));
        var against = ReasonPaging.FromRecord(record, "against");
        (against.Total, against.NextCursor, ReasonPaging.More(against)).ShouldBe((1, (string?)null, false));
    }

    [Fact]
    public void A_page_is_added_after_what_the_column_shows_and_one_shown_already_is_not_added_twice()
    {
        var column = Column(5, "2", Reason(1), Reason(2));
        var grown = ReasonPaging.Append(column, Column(5, null, Reason(2), Reason(3), Reason(4), Reason(5)));
        grown.Items.Select(r => r.Text).ShouldBe(["Argumentas 1", "Argumentas 2", "Argumentas 3", "Argumentas 4", "Argumentas 5"]);
        ReasonPaging.More(grown).ShouldBeFalse();
        ReasonPaging.NextAsked(Column(15, "2")).ShouldBe(22, "owner, 2026-11-19: two first, then 20 a press");
        ReasonPaging.NextAsked(Column(45, "22")).ShouldBe(42);
    }

    [Fact]
    public void Read_again_the_column_keeps_its_order_with_fresh_numbers_drops_what_went_and_puts_a_new_one_last()
    {
        var pinned = Reason(9);
        var column = Column(4, "3", pinned, Reason(1, 3), Reason(2, 2), Reason(3, 1));
        var fresh = Column(4, "3", Reason(2, 5), Reason(1, 3), Reason(4, 2));

        var read = ReasonPaging.Refresh(column, fresh, new HashSet<Guid> { pinned.Id });

        read.Items.Select(r => (r.Text, r.Endorsements)).ShouldBe([("Argumentas 9", 0), ("Argumentas 1", 3), ("Argumentas 2", 5), ("Argumentas 4", 2)],
            "the pinned one stays; the third went; the new one comes last");
    }

    [Fact]
    public void A_pinned_argument_goes_first_or_is_added_last_or_updated_in_place()
    {
        var column = Column(3, null, Reason(1), Reason(2));
        ReasonPaging.Pin(column, Reason(2)).Items.Select(r => r.Text).ShouldBe(["Argumentas 2", "Argumentas 1"]);
        ReasonPaging.Pin(column, Reason(3)).Items.Select(r => r.Text).ShouldBe(["Argumentas 3", "Argumentas 1", "Argumentas 2"]);
        ReasonPaging.Pin(column, Reason(3), first: false).Items.Select(r => r.Text).ShouldBe(["Argumentas 1", "Argumentas 2", "Argumentas 3"]);
        ReasonPaging.Pin(column, Reason(1, 7), first: false).Items.Select(r => (r.Text, r.Endorsements)).ShouldBe([("Argumentas 1", 7), ("Argumentas 2", 0)]);
        ReasonPaging.Without(column, Reason(1).Id).Items.Select(r => r.Text).ShouldBe(["Argumentas 2"]);
    }

    [Fact]
    public void The_link_is_the_pages_own_path_with_the_sides_count_and_lands_on_the_first_added()
    {
        ReasonPaging.Address("https://lt2.localhost:8443/seimas/XVP-0412?atsakymai=20&argumentai-uz=12#reasons", "https://lt2.localhost:8443/")
            .ShouldBe("/seimas/XVP-0412?atsakymai=20&argumentai-uz=12");
        ReasonPaging.PositionAnchor("against", 13).ShouldBe("arguments-against-13");
    }
}
