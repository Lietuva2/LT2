using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Lt2.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.18: the history of the Seimas's votes, the API (<c>GET /api/v1/votes</c>) and the page
/// (<c>/seimas/balsavimai</c>) as the server renders it, without JavaScript. Each test invents its own votes in 2001, long
/// before the invented parliament's, on an invented bill with diacritics in its title: 110 days (2001-09-13 to 2001-12-31)
/// with a presentation, a consideration and an adoption each; on 2001-12-31 also a consideration held at 00:30 Vilnius
/// time (22:30 UTC the day before), an amendment and a procedural vote without a bill. "Iki 2001-12-31" keeps a test to
/// them: 331 stage results, 110 adoptions, 333 votes in all.
/// </summary>
public sealed partial class VoteHistoryTests(DatabaseFixture database) : ApiTest(database), IAsyncLifetime
{
    private const string Bill = "XVP-9871";
    private const string BillTitle = "Žuvininkystės įstatymo bandomoji pataisa";
    private const string Until = "2001-12-31";
    private const long Midnight = 99301001;
    private const long Amendment = 99301002;
    private const long Procedural = 99301003;
    private const int StageResults = 331;
    private const int Adoptions = 110;
    private const int All = 333;

    public async ValueTask InitializeAsync()
    {
        await ParliamentSeed.EnsureAsync(Database);
        await RemoveAsync();
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.bill (id, number, title) VALUES (gen_random_uuid(), @number, @title);
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT 99300000 + n * 3 + s, (SELECT agenda_item_id FROM parliament.vote WHERE id = 99100101), (SELECT id FROM parliament.bill WHERE number = @number),
                   timestamptz '2001-12-31 00:00:00+00' - make_interval(days => 109 - n) + make_interval(hours => (ARRAY[6, 10, 11])[s + 1]),
                   'Pritarti', NULL, 'StageResult', (ARRAY['Presentation', 'Consideration', 'Adoption'])[s + 1], false, true, 70, 30, 10, 110, 141
            FROM generate_series(0, 109) AS n, generate_series(0, 2) AS s;
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT v.id, (SELECT agenda_item_id FROM parliament.vote WHERE id = 99100101), CASE WHEN v.kind = 'Procedural' THEN NULL ELSE (SELECT id FROM parliament.bill WHERE number = @number) END,
                   v.held_at, v.wording, NULL, v.kind, v.stage, false, true, 60, 40, 5, 105, 141
            FROM (VALUES (@midnight, timestamptz '2001-12-30 22:30:00+00', 'Pritarti po svarstymo', 'StageResult', 'Consideration'),
                         (@amendment, timestamptz '2001-12-31 09:00:00+00', 'Dėl pasiūlymo Nr. 3', 'Amendment', NULL),
                         (@procedural, timestamptz '2001-12-31 09:00:00+00', 'Dėl darbotvarkės papildymo', 'Procedural', NULL)) AS v(id, held_at, wording, kind, stage);
            """,
            ("number", Bill), ("title", BillTitle), ("midnight", Midnight), ("amendment", Amendment), ("procedural", Procedural));
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.vote WHERE held_at < '2002-01-01'")).ShouldBe(All);
    }

    public async ValueTask DisposeAsync() => await RemoveAsync();

    private async Task RemoveAsync()
    {
        await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id BETWEEN 99300000 AND 99301999");
        await Database.ExecuteAsync("DELETE FROM parliament.bill WHERE number = @number", ("number", Bill));
    }

    private async Task<VoteListResponse> VotesAsync(string query)
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(ParliamentRoutes.Votes + query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, query);
        return (await response.Content.ReadFromJsonAsync<VoteListResponse>())!;
    }

    private async Task<string> OkAsync(string path)
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The list's sheet.</summary>
    private static string List(string html)
    {
        var start = html.IndexOf("<div class=\"sheet stack\" id=\"balsavimai\">", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "the list is on a sheet");
        return html[start..html.IndexOf("<p class=\"fine\">", start, StringComparison.Ordinal)];
    }

    // ---- The API -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_api_lists_the_stage_results_newest_first_and_counts_them()
    {
        var page = await VotesAsync("?until=" + Until + "&limit=10");

        page.Total.ShouldBe(StageResults);
        page.Items.Count.ShouldBe(10);
        page.NextCursor.ShouldBe("10");
        page.Items.Select(i => i.Summary.Vote.HeldAt).ShouldBeInOrder(SortDirection.Descending);
        page.Items.ShouldAllBe(i => i.Summary.Vote.Kind == "stage-result" && i.Summary.Vote.Bill!.Number == Bill);
        page.Items[0].Summary.Vote.Id.ShouldBe(99300000 + 109 * 3 + 2, "the adoption at 11:00 on the last day");
        page.Items[3].Summary.Vote.Id.ShouldBe(Midnight, "00:30 in Vilnius is the last day's earliest");
        page.Items[0].Summary.Totals.ShouldBe(new VoteTotalsResponse(70, 30, 10, 110, 141));
    }

    [Fact]
    public async Task The_api_pages_with_limit_and_cursor_and_keeps_to_300()
    {
        var all = await VotesAsync("?until=" + Until + "&limit=300");
        all.Items.Count.ShouldBe(300);
        all.NextCursor.ShouldBe("300");

        var second = await VotesAsync("?until=" + Until + "&limit=100&cursor=100");
        second.Items.Select(i => i.Summary.Vote.Id).ShouldBe(all.Items.Skip(100).Take(100).Select(i => i.Summary.Vote.Id));
        second.NextCursor.ShouldBe("200");

        var last = await VotesAsync("?until=" + Until + "&limit=300&cursor=300");
        last.Items.Count.ShouldBe(StageResults - 300);
        last.NextCursor.ShouldBeNull();

        (await VotesAsync("?until=" + Until + "&limit=100000")).Items.Count.ShouldBe(300, "at most 300 in one answer");
        (await VotesAsync("?until=" + Until + "&limit=abc")).Items.Count.ShouldBe(VoteListQuery.DefaultLimit);
    }

    [Fact]
    public async Task The_api_kinds_are_the_stage_results_the_adoptions_and_every_vote()
    {
        var adoptions = await VotesAsync("?until=" + Until + "&kind=adoptions&limit=300");
        adoptions.Total.ShouldBe(Adoptions);
        adoptions.Items.ShouldAllBe(i => i.Summary.Vote.Kind == "stage-result" && i.Summary.Vote.Stage == "adoption");

        var all = await VotesAsync("?until=" + Until + "&kind=all&limit=10");
        all.Total.ShouldBe(All);
        var procedural = all.Items.Single(i => i.Summary.Vote.Id == Procedural);
        procedural.Summary.Vote.Kind.ShouldBe("procedural");
        procedural.Summary.Vote.Bill.ShouldBeNull();
        procedural.AgendaItemTitle.ShouldNotBeNullOrWhiteSpace();
        all.Items.Single(i => i.Summary.Vote.Id == Amendment).Summary.Vote.Kind.ShouldBe("amendment");

        (await VotesAsync("?until=" + Until + "&kind=nera")).Total.ShouldBe(StageResults, "an unknown kind is the default");
    }

    [Theory]
    [InlineData("zuvininkystes", StageResults)]
    [InlineData("ŽUVININKYSTĖS", StageResults)]
    [InlineData("xvp-9871", StageResults)]
    [InlineData("bandomoji pataisa", StageResults)]
    [InlineData("nera-tokio-projekto", 0)]
    public async Task The_api_search_finds_a_bills_votes_by_number_or_title_without_case_or_diacritics(string q, int expected) =>
        (await VotesAsync("?until=" + Until + "&q=" + Uri.EscapeDataString(q))).Total.ShouldBe(expected);

    [Fact]
    public async Task With_every_vote_the_search_finds_the_bills_amendment_but_never_a_vote_without_a_bill()
    {
        var found = await VotesAsync("?until=" + Until + "&kind=all&limit=300&q=zuvininkystes");

        found.Total.ShouldBe(All - 1);
        found.Items.ShouldContain(i => i.Summary.Vote.Id == Amendment);
        found.Items.ShouldNotContain(i => i.Summary.Vote.Id == Procedural);
    }

    [Fact]
    public async Task Until_keeps_the_votes_held_on_that_vilnius_day_or_earlier()
    {
        var before = await VotesAsync("?until=2001-12-30&limit=300");
        before.Total.ShouldBe(StageResults - 4, "the last day's three and the one at 00:30 Vilnius time");
        before.Items.ShouldAllBe(i => VilniusTime.DateOf(i.Summary.Vote.HeldAt) <= new DateOnly(2001, 12, 30));
        before.Items.ShouldNotContain(i => i.Summary.Vote.Id == Midnight);

        (await VotesAsync("?until=2001-09-13")).Total.ShouldBe(3);
        (await VotesAsync("?until=2001-09-12")).Total.ShouldBe(0);
        (await VotesAsync("?until=0001-01-01")).Total.ShouldBe(0);
        (await VotesAsync("?until=9999-12-31")).Total.ShouldBe((await VotesAsync(string.Empty)).Total);
        (await VotesAsync("?until=2001-31-12")).Total.ShouldBe((await VotesAsync(string.Empty)).Total, "a malformed day is none");
    }

    [Fact]
    public async Task The_api_is_public_cached_with_an_etag_and_the_same_for_everyone()
    {
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.TaxBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var visitor = NewBrowser();
        using var first = await visitor.Client.GetAsync(ParliamentRoutes.Votes);
        var etag = first.Headers.ETag.ShouldNotBeNull();
        first.Headers.CacheControl!.Public.ShouldBeTrue();
        var body = await first.Content.ReadAsStringAsync();
        body.ShouldContain("\"" + AppealSteps.TaxBill + "\"", customMessage: "the default answer holds the invented parliament's votes");
        (await member.Client.GetStringAsync(ParliamentRoutes.Votes)).ShouldBe(body);

        using var again = new HttpRequestMessage(HttpMethod.Get, ParliamentRoutes.Votes);
        again.Headers.IfNoneMatch.Add(etag);
        using var second = await visitor.Client.SendAsync(again);
        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    // ---- The page ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_page_shows_30_by_day_and_grows_by_50_landing_on_the_first_row_added()
    {
        var html = await OkAsync("/seimas/balsavimai?iki=" + Until);
        html.ShouldContain("<title>Seimo balsavimai · Lietuva 2.0</title>");
        html.ShouldContain("<h1 class=\"seimas-title\">Seimas</h1>");
        html.ShouldNotContain("<h2>Seimo balsavimai</h2>", customMessage: "no heading of its own: the marked tab says where the reader is (design 8.21)");
        html.ShouldContain("<a href=\"/seimas/balsavimai\" id=\"tab-seimas-balsavimai\" aria-current=\"page\">Balsavimai</a>", customMessage: "the page is the tab \"Balsavimai\", with the search in the address too (design 8.21)");
        html.ShouldContain("<a href=\"/darbotvarke\" id=\"tab-projektai\">Projektai</a>", customMessage: "Projektai is not marked");
        html.ShouldNotContain("id=\"view-agenda\"", customMessage: "no views row: the page is reached by a link");
        html.ShouldNotContain("id=\"to-agenda\"", customMessage: "the tab row leads back to the agenda; no breadcrumb");
        html.ShouldContain("<form class=\"filters\" method=\"get\" action=\"/seimas/balsavimai\" role=\"search\" id=\"votes-search\">");
        html.IndexOf("id=\"votes-search\"", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"balsavimai\"", StringComparison.Ordinal), "the controls lie above the list's sheet");
        var list = List(html);
        // Design 8.16.5 (owner, 2026-11-21): the list's sheet has no heading of its own, so the tab row stays at the top of
        // the screen; the heading "Seimas", the lead and the form scroll away.
        var head = ListHeadSteps.SeimasHead(html);
        head.ShouldContain("id=\"project-tabs\"");
        head.ShouldNotContain("id=\"votes-search\"");
        html.ShouldMatch("<div class=\"stack page-head__rest\">\\s*<p class=\"lead\">");

        Row().Count(list).ShouldBe(30);
        list.ShouldContain("Rodoma 30 iš 331");
        list.ShouldContain("href=\"/seimas/balsavimai?iki=2001-12-31&rodyti=80#balsavimas-31\" id=\"votes-more\"");
        list.ShouldContain("<section class=\"tl-day tl-past\" aria-label=\"2001 m. gruodžio 31 d.\" id=\"diena-2001-12-31\">");
        list.ShouldContain("<span class=\"label\">2001 m.</span><b>gruodžio 31 d.</b><span class=\"small\">pirmadienis</span>");
        RowsOf(list, "2001-12-31").ShouldBe(4);
        RowsOf(list, "2001-12-22").ShouldBe(2, "the part shown cuts the tenth day");

        var more = List(await OkAsync("/seimas/balsavimai?iki=" + Until + "&rodyti=80"));
        Row().Count(more).ShouldBe(80);
        more.ShouldContain("id=\"balsavimas-31\"");
        more.ShouldContain("Rodoma 80 iš 331");
        more.ShouldContain("?iki=2001-12-31&rodyti=130#balsavimas-81");
        RowsOf(more, "2001-12-22").ShouldBe(3, "a cut day continues when more is shown");
        foreach (var page in new[] { list, more })
        {
            var days = Day().Matches(page).Select(m => m.Groups[1].Value).ToList();
            days.ShouldBe(days.Distinct().ToList(), "a day's heading is never repeated");
            days.ShouldBeInOrder(SortDirection.Descending);
        }
    }

    [Fact]
    public async Task One_page_shows_at_most_300_then_older_starts_again_from_the_day_of_the_last_vote_shown()
    {
        var shown = await VotesAsync("?until=" + Until + "&limit=300");
        var lastDay = VilniusTime.DateOf(shown.Items[^1].Summary.Vote.HeldAt);
        var list = List(await OkAsync("/seimas/balsavimai?iki=" + Until + "&rodyti=300"));

        Row().Count(list).ShouldBe(300);
        list.ShouldContain("Rodoma 300 iš 331");
        list.ShouldNotContain("id=\"votes-more\"");
        list.ShouldContain($"href=\"/seimas/balsavimai?iki={lastDay:yyyy-MM-dd}#balsavimai\" id=\"votes-older\">Senesni balsavimai →</a>");
        (await OkAsync("/seimas/balsavimai?iki=" + Until + "&rodyti=330")).ShouldContain("Rodoma 30 iš 331", customMessage: "beyond 300 is the default");

        var older = List(await OkAsync($"/seimas/balsavimai?iki={lastDay:yyyy-MM-dd}"));
        Row().Count(older).ShouldBe(30);
        Day().Match(older).Groups[1].Value.ShouldBe($"{lastDay:yyyy-MM-dd}", "it starts from that day, the whole of it");
        older.ShouldNotContain("id=\"votes-older\"");
    }

    [Theory]
    [InlineData("?kas=nera&iki=2001-13-45&rodyti=abc&q=")]
    [InlineData("?rodyti=100000&kas=VISI")]
    [InlineData("?rodyti=-30&iki=vakar")]
    [InlineData("?q=" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("?iki=&kas=&rodyti=60.5")]
    public async Task A_malformed_address_shows_the_default_list(string query)
    {
        var plain = List(await OkAsync("/seimas/balsavimai"));
        Row().Count(plain).ShouldBeGreaterThan(0, "the default list has the invented parliament's stage results");

        List(await OkAsync("/seimas/balsavimai" + query)).ShouldBe(plain);
    }

    [Fact]
    public async Task A_search_without_diacritics_finds_the_bills_votes_and_nothing_found_offers_to_clear()
    {
        var found = List(await OkAsync("/seimas/balsavimai?q=zuvininkystes&iki=" + Until));
        found.ShouldContain("Rodoma 30 iš 331");
        found.ShouldContain("href=\"/seimas/balsavimai?q=zuvininkystes&iki=2001-12-31&rodyti=80#balsavimas-31\"");

        var none = await OkAsync("/seimas/balsavimai?q=nera-tokio-projekto");
        none.ShouldContain("value=\"nera-tokio-projekto\"");
        var empty = List(none);
        Row().Count(empty).ShouldBe(0);
        empty.ShouldContain("<p id=\"votes-none\">Nerasta nė vieno balsavimo. <a href=\"/seimas/balsavimai\">Išvalyti</a></p>");
        empty.ShouldNotContain("Rodoma");
    }

    [Fact]
    public async Task Every_vote_adds_the_amendments_and_procedural_votes_with_their_kind_and_wording_and_adoptions_leave_only_adoptions()
    {
        var all = await OkAsync("/seimas/balsavimai?kas=visi&iki=" + Until);
        all.ShouldMatch("<option value=\"visi\" selected[^>]*>Visi balsavimai</option>");
        var list = List(all);
        list.ShouldContain("Rodoma 30 iš 333");

        var amendment = RowOf(list, Amendment);
        amendment.ShouldContain("<span class=\"tag\">Dėl pasiūlymo</span>");
        amendment.ShouldContain("<span class=\"small\">Dėl pasiūlymo Nr. 3</span>");
        amendment.ShouldContain($"<a class=\"tl-item__title\" href=\"/seimas/balsavimai/{Amendment}\">{BillTitle}</a>");

        var procedural = RowOf(list, Procedural);
        procedural.ShouldContain("<span class=\"tag\">Procedūrinis balsavimas</span>");
        procedural.ShouldContain("<span class=\"small\">Dėl darbotvarkės papildymo</span>");
        procedural.ShouldContain($"<a class=\"tl-item__title\" href=\"/seimas/balsavimai/{Procedural}\">");
        procedural.ShouldNotContain("href=\"/seimas/XVP-", customMessage: "no bill");

        var stage = RowOf(list, Midnight);
        stage.ShouldContain("<span class=\"tag\">Svarstymas</span>");
        stage.ShouldNotContain("Pritarti po svarstymo", customMessage: "a stage result's wording is not repeated in the row");

        var adoptions = List(await OkAsync("/seimas/balsavimai?kas=priemimai&iki=" + Until));
        adoptions.ShouldContain("Rodoma 30 iš 110");
        Tag().Matches(adoptions).Select(m => m.Groups[1].Value).Distinct().ShouldBe(["Priėmimas"]);
    }

    [Fact]
    public async Task Each_row_leads_to_its_vote_by_the_title_and_to_its_bill_by_the_number_and_has_no_participants_numbers()
    {
        var list = List(await OkAsync("/seimas/balsavimai?iki=" + Until));
        var vote = 99300000 + 109 * 3 + 2;
        var row = RowOf(list, vote);

        // Each row is a vote (owner, 2026-11-23): the title leads to the vote's page, the number to the bill, and there is no button.
        row.ShouldContain($"<a class=\"tag tag--ink num\" href=\"/seimas/{Bill}\">{Bill}</a>");
        row.ShouldContain($"<a class=\"tl-item__title\" href=\"/seimas/balsavimai/{vote}\">{BillTitle}</a>");
        row.ShouldNotContain($"href=\"/seimas/{Bill}\">{BillTitle}", customMessage: "the title does not lead to the bill");
        row.ShouldNotContain("Kaip balsavo Seimo nariai");
        list.ShouldNotContain("Kaip balsavo Seimo nariai");
        // Owner, 2026-11-23: the numbers stand in the right column, under the word "Seimas"; what and where stay on the left.
        var side = row[row.IndexOf("<div class=\"tl-item__side\">", StringComparison.Ordinal)..];
        side.ShouldContain("<span class=\"label\">Seimas</span>");
        side.ShouldContain("už 70 · prieš 30 · susilaikė 10");
        side.ShouldContain("<svg class=\"splitbar");
        row[..row.IndexOf("tl-item__side", StringComparison.Ordinal)].ShouldNotContain("už 70", customMessage: "the result is not in the main column");
        row.ShouldNotContain("dalyvi", customMessage: "no participants' numbers in the rows (design 8.18)");
        row.ShouldNotContain("riba");
    }

    /// <summary>
    /// Design 8.18, "The majority" (owner, 2026-11-18): the open data carries no official outcome, so each row says the
    /// majority of those who voted (8.26.2): "Dauguma už" (more than half of for, against and abstain for it), "Dauguma prieš",
    /// or "Nėra daugumos"; never "priimta". What it is is one hint beside the list. The vote's page and the bill's list of
    /// votes say the same.
    /// </summary>
    [Fact]
    public async Task Each_row_says_the_majority_of_those_who_voted_and_one_hint_says_what_it_is()
    {
        const long Adoption = 99300000 + 109 * 3 + 2;
        const long Consideration = 99300000 + 109 * 3 + 1;
        const long Presentation = 99300000 + 109 * 3;
        await Database.ExecuteAsync("UPDATE parliament.vote SET \"for\" = 40, against = 60, abstain = 5, voted = 105 WHERE id = @v", ("v", Consideration));
        await Database.ExecuteAsync("UPDATE parliament.vote SET \"for\" = 50, against = 40, abstain = 10, voted = 100 WHERE id = @v", ("v", Presentation));
        var page = await OkAsync("/seimas/balsavimai?kas=visi&iki=" + Until);
        var list = List(page);

        RowOf(list, Adoption).ShouldContain("<span class=\"tag tag--outcome\" data-majority=\"for\">Dauguma už</span>");
        RowOf(list, Consideration).ShouldContain("<span class=\"tag tag--outcome tag--outline\" data-majority=\"against\">Dauguma prieš</span>");
        RowOf(list, Presentation).ShouldContain("<span class=\"tag tag--outcome tag--outline tag--dashed\" data-majority=\"none\">Nėra daugumos</span>", customMessage: "exactly half for is no majority");
        list.ShouldNotContain("Priimta");
        Regex.Count(page, "Kas yra balsavusiųjų dauguma\\?").ShouldBe(1, "one hint beside the list, none per row");
        AppealPageTests.HintWithIconOf(page, "hint-vote-majority").Icon.ShouldBeFalse("under the list it keeps its words: the question says what the rows' chip is");

        var votePage = await OkAsync($"/seimas/balsavimai/{Consideration}");
        votePage.ShouldContain("data-majority=\"against\">Dauguma prieš</span>", customMessage: "the vote's page");
        var onVotePage = AppealPageTests.HintWithIconOf(votePage, "hint-vote-majority");
        onVotePage.Icon.ShouldBeTrue("beside the chip it explains: the circled \"i\"");
        onVotePage.Trigger.ShouldBe("Kas yra balsavusiųjų dauguma?", "the question stays as hidden text");
        (await OkAsync($"/seimas/{Bill}")).ShouldContain("data-majority=\"none\">Nėra daugumos</span>", customMessage: "the bill's list of votes");
    }

    [Fact]
    public async Task The_page_is_the_same_for_a_visitor_and_a_member_who_took_a_side()
    {
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.TaxBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var visitor = NewBrowser();

        var mine = WebUtility.HtmlDecode(await member.Client.GetStringAsync("/seimas/balsavimai"));
        var theirs = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/balsavimai"));
        var list = List(theirs);
        list.ShouldContain($"href=\"/seimas/{AppealSteps.TaxBill}\"", customMessage: "the list holds the bill the member took a side on");
        mine.ShouldContain("href=\"/paskyra\"", customMessage: "the member's page is the signed-in one");
        List(mine).ShouldBe(list);
    }

    // ---- The ways in -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_start_page_and_the_agenda_link_to_the_history_beside_the_heading_also_without_a_recent_vote()
    {
        using var visitor = NewBrowser();
        (await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda)).RecentVotes.ShouldBeEmpty("no vote in the last seven days");
        ForgetFrontPage();

        // Owner, 2026-11-21: the voted days are a sheet of their own with an h2, and "Ankstesni balsavimai" stands beside the
        // heading, as "Visa darbotvarkė" beside the planned days' heading; the section shows without a vote too.
        var home = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/"));
        home.ShouldNotContain("id=\"home-voted-");
        home.ShouldContain("<section class=\"sheet stack\" aria-labelledby=\"h-home-recent\" id=\"home-recent\">");
        var row = ReasonsToComeSteps.HeadRow(home, "h-home-recent");
        row.ShouldContain("<h2 id=\"h-home-recent\">Neseniai balsuota</h2>");
        row.ShouldContain("<a href=\"/seimas/balsavimai\" id=\"home-votes-older\">Ankstesni balsavimai →</a>");
        home.ShouldContain("<p id=\"home-no-recent\">Per pastarąsias septynias dienas Seimas dėl projektų etapų nebalsavo.</p>");
        home.IndexOf("id=\"home-no-recent\"", StringComparison.Ordinal).ShouldBeGreaterThan(home.IndexOf("id=\"h-home-recent\"", StringComparison.Ordinal));
        home.ShouldNotContain("tl-older", customMessage: "no link at the end of the list");
        Regex.Count(home, "Ankstesni balsavimai").ShouldBe(1);

        var agenda = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
        agenda.ShouldNotContain("id=\"agenda-no-recent\"");
        agenda.ShouldNotContain("agenda-votes-older", customMessage: "the agenda has no link to the history: it is the tab \"Balsavimai\"");
        agenda.ShouldNotContain("tl-older");
        agenda.ShouldNotContain("Ankstesni balsavimai");
    }

    [Fact]
    public async Task With_a_recent_vote_the_voted_days_are_a_section_of_their_own_with_the_link_beside_its_heading_and_a_row_leads_to_its_vote()
    {
        var old = await Database.MoveVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, DateTimeOffset.UtcNow.AddDays(-2));
        try
        {
            ForgetFrontPage();
            using var visitor = NewBrowser();
            var home = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/"));
            home.LastIndexOf("id=\"home-voted-", StringComparison.Ordinal).ShouldBeGreaterThan(0);
            // Owner, 2026-11-23: the start page's rows are the history's: the title leads to the vote, the number to the bill,
            // and no button "Kaip balsavo Seimo nariai"; the bill's numbers stay beside them.
            var voted = home[home.IndexOf($"id=\"recent-{ReasonsToComeSteps.TaxConsiderationVote}\"", StringComparison.Ordinal)..];
            voted = voted[..voted.IndexOf("</li>", StringComparison.Ordinal)];
            voted.ShouldContain($"<a class=\"tag tag--ink num\" href=\"/seimas/{AppealSteps.TaxBill}\">{AppealSteps.TaxBill}</a>");
            voted.ShouldContain($"<a class=\"tl-item__title\" href=\"/seimas/balsavimai/{ReasonsToComeSteps.TaxConsiderationVote}\">");
            voted.ShouldNotContain($"<a class=\"tl-item__title\" href=\"/seimas/{AppealSteps.TaxBill}\">");
            voted.ShouldNotContain("Kaip balsavo Seimo nariai");
            voted.ShouldNotContain("btn--plain");
            // Owner, 2026-11-23: the numbers stand in the right column, each block under the word that says whose it is: the
            // Seimas's result, then the bill's participants; the main column holds what and where only.
            var side = voted[voted.IndexOf("<div class=\"tl-item__side\">", StringComparison.Ordinal)..];
            side.ShouldContain("<span class=\"label\">Seimas</span>");
            side.ShouldContain("<span class=\"label\">Dalyviai</span>");
            side.IndexOf("Seimas</span>", StringComparison.Ordinal).ShouldBeLessThan(side.IndexOf("Dalyviai</span>", StringComparison.Ordinal));
            side.ShouldContain("<svg class=\"splitbar");
            voted[..voted.IndexOf("tl-item__side", StringComparison.Ordinal)].ShouldNotContain("splitbar", customMessage: "no numbers in the main column");
            home.ShouldNotContain("Kaip balsavo Seimo nariai");
            // The planned days' sheet ends before the voted days' sheet begins; its heading row holds the link, before the
            // first voted day.
            var coming = home.IndexOf("id=\"home-coming\"", StringComparison.Ordinal);
            var recent = home.IndexOf("<section class=\"sheet stack\" aria-labelledby=\"h-home-recent\" id=\"home-recent\">", StringComparison.Ordinal);
            coming.ShouldBeGreaterThan(0);
            recent.ShouldBeGreaterThan(home.LastIndexOf("id=\"agenda-", StringComparison.Ordinal));
            var between = home[coming..recent];
            Regex.Count(between, "<section").ShouldBe(Regex.Count(between, "</section>") - 1, "the planned days' sheet is closed before the next opens");
            var heading = home.IndexOf("id=\"h-home-recent\"", StringComparison.Ordinal);
            heading.ShouldBeLessThan(home.IndexOf("id=\"home-voted-", StringComparison.Ordinal));
            home.IndexOf("id=\"home-votes-older\"", StringComparison.Ordinal).ShouldBeGreaterThan(heading);
            home.IndexOf("id=\"home-votes-older\"", StringComparison.Ordinal).ShouldBeLessThan(home.IndexOf("id=\"home-voted-", StringComparison.Ordinal));
            home.ShouldNotContain("id=\"home-no-recent\"");

            var agenda = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
            // The agenda page lists no vote held, a recent one too, and has no link to the history (the tab "Balsavimai" is).
            agenda.ShouldNotContain("id=\"agenda-votes-older\"");
            agenda.ShouldNotContain("id=\"recent-" + ReasonsToComeSteps.TaxConsiderationVote + "\"");
        }
        finally
        {
            await Database.RestoreVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, old);
            ForgetFrontPage();
        }
    }
    [Fact]
    public async Task A_vote_page_links_up_to_the_history_and_balsavimai_is_never_a_bill()
    {
        foreach (var path in new[] { "/seimas/balsavimai/99100002", "/seimas/balsavimai/99100101" })
        {
            var html = await OkAsync(path);
            html.ShouldContain("<li><a href=\"/seimas/balsavimai\" id=\"to-vote-history\">Seimo balsavimai</a></li>", customMessage: path);

            // Design 8.21: a vote is under the tab "Balsavimai", as a summary's date page is under "Suvestinės": the menu item
            // "Seimas" is current, the dropdown marks no tab (not "Balsavimai" either), and there is no tab row.
            var header = html[..html.IndexOf("</header>", StringComparison.Ordinal)];
            header.ShouldContain("<a class=\"drop__toggle drop__link\" href=\"/darbotvarke\" id=\"nav-projects\" aria-current=\"true\">Seimas</a>", customMessage: path);
            header.ShouldNotContain("aria-current=\"page\"", customMessage: path);
            html.ShouldNotContain("aria-current=\"page\"", customMessage: path + ": no tab is marked");
            html.ShouldNotContain("id=\"project-tabs\"", customMessage: path);
        }

        using var browser = NewBrowser();
        using var bill = await browser.Client.GetAsync(ParliamentRoutes.BillByNumber("balsavimai"));
        bill.StatusCode.ShouldBe(HttpStatusCode.NotFound, "no bill is called that");
        (await OkAsync("/seimas/balsavimai")).ShouldNotContain("Įstatymo projektas");
    }

    private void ForgetFrontPage() => Factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);

    /// <summary>How many rows the day's section of the list holds.</summary>
    private static int RowsOf(string list, string day)
    {
        var start = list.IndexOf($"id=\"diena-{day}\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, day);
        return Row().Count(list[start..list.IndexOf("</section>", start, StringComparison.Ordinal)]);
    }

    /// <summary>The row of a vote: from its "li" to its end.</summary>
    private static string RowOf(string list, long vote)
    {
        var at = list.IndexOf($"href=\"/seimas/balsavimai/{vote}\"", StringComparison.Ordinal);
        at.ShouldBeGreaterThan(0, vote.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var start = list.LastIndexOf("<li class=\"tl-item\"", at, StringComparison.Ordinal);
        return list[start..list.IndexOf("</li>", at, StringComparison.Ordinal)];
    }

    [GeneratedRegex("<li class=\"tl-item\" id=\"balsavimas-\\d+\">")]
    private static partial Regex Row();

    [GeneratedRegex("id=\"diena-(\\d{4}-\\d{2}-\\d{2})\"")]
    private static partial Regex Day();

    [GeneratedRegex("<span class=\"tag\">([^<]+)</span>")]
    private static partial Regex Tag();
}
