using System.Net;
using System.Text.RegularExpressions;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.12.1 (owner, 2026-11-23): the sheet "Ko prašo Lietuva 2.0 naudotojai" of a bill the Seimas has voted a stage of has a
/// switch at its top, "Lietuva 2.0 naudotojai | Seimas", each with its "už : prieš"; both parts are in the served HTML (radio
/// inputs and CSS, no script), and the page carries no ballot: the seat map is read by an island when the switch is first
/// turned to the Seimas, never with the page.
/// </summary>
public sealed class BillSeimasSwitchTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    private const string Empty = "XVP-S0001";

    public async ValueTask InitializeAsync()
    {
        await ParliamentSeed.EnsureAsync(Database);
        await Database.ExecuteAsync("DELETE FROM parliament.bill WHERE number = @n", ("n", Empty));
        await Database.ExecuteAsync("INSERT INTO parliament.bill (id, number, title) VALUES (gen_random_uuid(), @n, 'Balsavimų neturintis projektas')", ("n", Empty));
    }

    public async ValueTask DisposeAsync() => await Database.ExecuteAsync("DELETE FROM parliament.bill WHERE number = @n", ("n", Empty));

    private async Task<string> PageAsync(string number)
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync("/seimas/" + number);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, number);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_bill_with_a_stage_result_has_the_switch_with_both_segments_the_participants_first_and_the_latest_result_as_the_Seimass()
    {
        var html = await PageAsync(AppealSteps.TaxBill);

        html.ShouldContain("id=\"sides-switch\"");
        html.ShouldContain("role=\"radiogroup\" aria-label=\"Naudotojų ar Seimo skaičiai\"");
        Regex.IsMatch(html, "<input type=\"radio\" class=\"visually-hidden\" name=\"sides-view\" id=\"view-public\" checked").ShouldBeTrue("the participants are shown first");
        Regex.IsMatch(html, "<input type=\"radio\" class=\"visually-hidden\" name=\"sides-view\" id=\"view-seimas\"\\s*/?>").ShouldBeTrue("the Seimas's radio is not checked");
        Regex.IsMatch(html, "<label for=\"view-public\">Lietuva 2\\.0 naudotojai · .*?id=\"score-public\">[\\d\\s\\u00a0]+ : [\\d\\s\\u00a0]+</span>").ShouldBeTrue("the participants' for : against");
        html.ShouldContain("<label for=\"view-seimas\">Seimas · 74 : 44</label>", Case.Sensitive);
        Regex.Count(html, "id=\"view-(public|seimas)\"").ShouldBe(2);
        html.ShouldContain("id=\"public-panel\"");
        html.ShouldContain("id=\"seimas-panel\"");
        Regex.IsMatch(html, "id=\"seimas-panel\".*id=\"h-seimas\">Kaip balsavo Seimas</h2>", RegexOptions.Singleline).ShouldBeTrue();
        html.ShouldContain("id=\"not-a-poll\"", Case.Sensitive, "the participants' part is as it was");
        html.ShouldContain("id=\"threshold\"");
        Regex.IsMatch(html, "<script src=\"/?js/lt2-switch[^\"]*\\.js\"></script>").ShouldBeTrue("the script that tells the island when the radio is chosen");
    }

    [Fact]
    public async Task The_Seimas_part_names_the_stage_and_the_day_and_has_the_chip_the_totals_the_bar_and_the_links_but_no_ballot_and_no_seat_map()
    {
        var html = await PageAsync(AppealSteps.TaxBill);
        var panel = html[html.IndexOf("id=\"seimas-panel\"", StringComparison.Ordinal)..];
        panel = panel[..panel.IndexOf("</section>", StringComparison.Ordinal)];

        panel.ShouldContain("Svarstymas · 2026 m. gegužės 19 d.");
        panel.ShouldContain("<a href=\"/seimas/balsavimai/99100006\" id=\"seimas-vote-page\">Balsavimo puslapis</a>");
        panel.ShouldContain("data-majority=\"for\"");
        panel.ShouldContain("id=\"hint-seimas-majority\"");
        AppealPageTests.HintWithIconOf(html, "hint-seimas-majority").Icon.ShouldBeTrue("beside the chip it explains: the circled \"i\"");
        panel.ShouldContain("class=\"figure__num\">74</span>");
        panel.ShouldContain("class=\"figure__num\">44</span>");
        panel.ShouldContain("class=\"figure__num\">10</span>");
        panel.ShouldContain("<svg class=\"splitbar ");
        panel.ShouldContain("<a href=\"/seimas/balsavimai/99100006#balsai\" id=\"each-mp\">Kaip balsavo kiekvienas Seimo narys</a>");
        panel.ShouldContain("<a href=\"/seimas/XVP-0412#h-votes\" id=\"all-votes\">Visi balsavimai dėl projekto</a>");

        // The seat map comes from an island, read when the switch is first turned to the Seimas; the page holds no ballot.
        Regex.Count(html, "<rect [^>]*class=\"seat ").ShouldBe(0);
        html.ShouldNotContain("class=\"seatmap\"");
        html.ShouldNotContain("id=\"seimas-seats\"");
        html.ShouldNotContain("href=\"/p/", customMessage: "no MP's row on the bill's page");
        html.ShouldNotContain("Jonas Petraitis");
        html.ShouldNotContain("id=\"seimas-seats-loading\"");
    }

    [Fact]
    public async Task A_stage_approved_by_consensus_says_so_in_the_segment_without_numbers_and_has_no_ballots()
    {
        var html = await PageAsync(AppealSteps.SecondBill);

        html.ShouldContain("<label for=\"view-seimas\">Seimas · bendru sutarimu</label>", Case.Sensitive);
        var panel = html[html.IndexOf("id=\"seimas-panel\"", StringComparison.Ordinal)..];
        panel = panel[..panel.IndexOf("</section>", StringComparison.Ordinal)];
        panel.ShouldContain("data-majority=\"consensus\"");
        panel.ShouldContain("Atskirų Seimo narių balsų nėra");
        panel.ShouldNotContain("figure__num");
        panel.ShouldNotContain("id=\"each-mp\"");
        panel.ShouldContain("id=\"all-votes\"");
        panel.ShouldContain("id=\"seimas-vote-page\"");
    }

    [Fact]
    public async Task A_bill_the_Seimas_has_not_voted_has_no_switch_and_the_sheet_is_as_it_was()
    {
        var html = await PageAsync(Empty);

        html.ShouldContain("id=\"sides\"");
        html.ShouldContain("id=\"h-sides\">Ko prašo Lietuva 2.0 naudotojai</h2>");
        html.ShouldContain("id=\"not-a-poll\"");
        html.ShouldNotContain("id=\"sides-switch\"");
        html.ShouldNotContain("id=\"view-seimas\"");
        html.ShouldNotContain("id=\"seimas-panel\"");
        html.ShouldNotContain("name=\"sides-view\"");
    }

    [Fact]
    public async Task The_vote_page_still_draws_its_seat_map_on_the_server()
    {
        using var browser = NewBrowser();
        var html = WebUtility.HtmlDecode(await browser.Client.GetStringAsync("/seimas/balsavimai/99100006"));

        html.ShouldContain("<svg class=\"seatmap\"");
        Regex.Count(html, "<rect [^>]*class=\"seat ").ShouldBeGreaterThan(0);
    }
}
