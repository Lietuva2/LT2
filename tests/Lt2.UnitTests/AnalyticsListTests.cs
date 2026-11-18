using Lt2.Api.Contracts.Analytics;
using Lt2.Web.Client.Analytics;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.26.1 item 2 (owner, 2026-11-18): the list of closed bills on <c>/statistika</c>, its state in the address with
/// fallbacks, what it asks the API for, and where "Rodyti daugiau" leads; and how the API reads the same filters.
/// </summary>
public sealed class AnalyticsListTests
{
    private static AnalyticsListState Read(
        string? metai = null, string? paskyros = null, string? nuo = null, string? iki = null, string? q = null,
        string? rezultatas = null, string? dauguma = null, string? rikiuoti = null, string? rodyti = null) =>
        AnalyticsList.Read(metai, paskyros, nuo, iki, q, rezultatas, dauguma, rikiuoti, rodyti);

    [Fact]
    public void The_plain_address_is_every_closed_bill_of_the_term_newest_first_ten_at_a_time()
    {
        var plain = Read();
        plain.ShouldBe(new AnalyticsListState(null, false, null, null, null, "", "", "", 10));
        plain.Filtered.ShouldBeFalse();
        AnalyticsList.Address(plain).ShouldBe("/statistika");
        AnalyticsList.Query(plain).ShouldBe(new AnalyticsBillQuery(Limit: 11), "one more than shown, to land on the next");
    }

    [Fact]
    public void The_lithuanian_words_of_the_address_become_the_apis_values()
    {
        var state = Read(metai: "2026", paskyros: "senesnes", nuo: "2026-03-01", iki: "2026-10-31", q: " pajamų ", rezultatas: "skyresi", dauguma: "nera", rikiuoti: "skirtumas", rodyti: "30");
        AnalyticsList.Query(state).ShouldBe(new AnalyticsBillQuery(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 10, 31), "pajamų", AnalyticsBillQuery.Differed, "none", AnalyticsBillQuery.Difference, 31));
        AnalyticsList.Address(state).ShouldBe(
            "/statistika?metai=2026&paskyros=senesnes&nuo=2026-03-01&iki=2026-10-31&q=pajam%C5%B3&rezultatas=skyresi&dauguma=nera&rikiuoti=skirtumas&rodyti=30");
        state.Filtered.ShouldBeTrue();
    }

    [Fact]
    public void A_value_the_page_would_not_make_is_the_default()
    {
        var state = Read(rezultatas: "x", dauguma: "taip", rikiuoti: "abc", rodyti: "5", paskyros: "visos");
        state.ShouldBe(new AnalyticsListState(null, false, null, null, null, "", "", "", 10));
        Read(rodyti: "301").Shown.ShouldBe(10);
        Read(q: new string('a', 101)).Query.ShouldBeNull();
    }

    [Fact]
    public void A_typed_day_that_is_not_a_date_is_kept_for_its_field_but_not_used()
    {
        var state = Read(nuo: "2026-13-01", iki: "vakar");
        (state.FromInvalid, state.ToInvalid, state.From).ShouldBe((true, true, "2026-13-01"));
        AnalyticsList.Query(state).From.ShouldBeNull();
        AnalyticsList.Address(state).ShouldBe("/statistika", "the next address forgets it");
    }

    [Fact]
    public void Days_the_wrong_way_round_are_not_used_and_the_page_says_so()
    {
        var state = Read(nuo: "2026-10-31", iki: "2026-03-01");
        state.Reversed.ShouldBeTrue();
        (AnalyticsList.Query(state).From, AnalyticsList.Query(state).To).ShouldBe((null, null));
    }

    [Fact]
    public void Rodyti_daugiau_adds_twenty_and_lands_on_the_first_row_added()
    {
        AnalyticsList.More(Read(q: "XVP"), found: 25, next: "XVP-0711").ShouldBe("/statistika?q=XVP&rodyti=30#projektas-XVP-0711");
        AnalyticsList.More(Read(rodyti: "30"), found: 25, next: null).ShouldBeNull("every bill found is shown");
        AnalyticsList.More(Read(rodyti: "300"), found: 400, next: "XVP-0999").ShouldBeNull("the list holds as many as it may");
        AnalyticsList.More(Read(rodyti: "290"), found: 400, next: "XVP-0999")!.ShouldStartWith("/statistika?rodyti=300#");
        AnalyticsList.More(Read(q: "XVP"), found: 25, next: "XVP-0711", nextInPlot: false)
            .ShouldBe("/statistika?q=XVP&rodyti=30#palyginimas", "a bill without a place in the plot: the part itself, the table stays closed");
    }

    [Fact]
    public void Filtrai_counts_every_field_set_and_the_newest_first_is_none()
    {
        Read().Active.ShouldBe(0);
        Read(metai: "2026", paskyros: "senesnes", rodyti: "30").Active.ShouldBe(0, "the period, the accounts and the length are not the list's filters");
        Read(q: "pajamų", rikiuoti: "skirtumas").Active.ShouldBe(2);
        Read(nuo: "vakar").Active.ShouldBe(1, "a day that is not a date is set too, so its field and error show");
        Read(nuo: "2026-03-01", iki: "2026-10-31", q: "x", rezultatas: "sutapo", dauguma: "uz", rikiuoti: "pozicijos").Active.ShouldBe(6);
    }

    [Fact]
    public void Isvalyti_keeps_the_period_and_the_accounts_and_clears_the_list()
    {
        var state = Read(metai: "2026", paskyros: "senesnes", q: "pajamų", rezultatas: "sutapo", rodyti: "30");
        AnalyticsList.Cleared(state, AnalyticsList.Anchor).ShouldBe("/statistika?metai=2026&paskyros=senesnes#palyginimas");
    }

    [Fact]
    public void The_api_reads_the_same_filters_and_falls_back_to_its_defaults()
    {
        AnalyticsBillQuery.Read("2026-03-01", "2026-10-31", " pajamų ", "agreed", "against", "positions", "40")
            .ShouldBe(new AnalyticsBillQuery(new DateOnly(2026, 3, 1), new DateOnly(2026, 10, 31), "pajamų", "agreed", "against", "positions", 40));
        AnalyticsBillQuery.Read("2026-02-30", null, "", "x", "for ", "x", "0").ShouldBe(new AnalyticsBillQuery());
        AnalyticsBillQuery.Read(null, null, null, null, null, null, "5000").Limit.ShouldBe(AnalyticsBillQuery.MaxLimit);
        AnalyticsBillQuery.Read("2026-10-31", "2026-03-01", null, null, null, null, null).ShouldBe(new AnalyticsBillQuery(), "a range that ends before it starts");
    }

    [Fact]
    public void The_apis_address_leaves_the_defaults_out() =>
        AnalyticsRoutes.Of(2026, true, new AnalyticsBillQuery(Search: "XVP-0412", Sort: AnalyticsBillQuery.Difference, Limit: 31))
            .ShouldBe("/api/v1/analytics?year=2026&accounts=older&q=XVP-0412&sort=difference&limit=31");
}
