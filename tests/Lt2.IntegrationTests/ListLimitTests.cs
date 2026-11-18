using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Mps;
using Lt2.Api.Contracts.Organisations;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Client.Appeals;
using BillListResponse = Lt2.Api.Contracts.Appeals.BillListResponse;

namespace Lt2.IntegrationTests;

/// <summary>
/// The lists' lengths with a real-sized parliament (owner, 2026-11-18) over HTTP: <c>/projektai</c> and an MP's votes grow in
/// place by the address, the arguments' "show all" works without script, and every moderators' queue says how many wait.
/// </summary>
public sealed partial class ListLimitTests(DatabaseFixture database) : ApiTest(database)
{
    private async Task<string> PageAsync(string address)
    {
        using var visitor = NewBrowser();
        using var page = await visitor.Client.GetAsync(address, TestContext.Current.CancellationToken);
        page.StatusCode.ShouldBe(HttpStatusCode.OK, address);
        return WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_bill_list_answers_as_many_as_asked_within_its_limits()
    {
        using var visitor = NewBrowser();
        var all = await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null));
        all.Total.ShouldBeGreaterThan(2, "the run's catalogue has active bills");
        all.Items.Count.ShouldBe(Math.Min(all.Total, BillListLimits.Default));

        var two = await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, limit: 2));
        two.Items.Select(i => i.Bill.Number).ShouldBe(all.Items.Take(2).Select(i => i.Bill.Number), "the same order, cut");
        two.NextCursor.ShouldBe("2");
        var next = await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, "2", limit: 1));
        next.Items.Single().Bill.Number.ShouldBe(all.Items[2].Bill.Number);

        foreach (var (asked, items) in new[] { ("0", Math.Min(all.Total, BillListLimits.Default)), ("abc", Math.Min(all.Total, BillListLimits.Default)), ("100000", Math.Min(all.Total, BillListLimits.Max)) })
        {
            var answer = await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.Appeals + "?limit=" + asked);
            answer.Items.Count.ShouldBe(items, $"limit={asked} falls back, never an error");
        }
    }

    [Fact]
    public async Task The_bill_list_page_says_how_many_it_shows_and_rodyti_shows_more_in_place()
    {
        using var visitor = NewBrowser();
        var all = await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, limit: BillListLimits.Max));

        var html = await PageAsync("/projektai");
        Regex.Count(html, "<li id=\"bill-").ShouldBe(Math.Min(all.Total, BillListPaging.First));
        html.ShouldContain($"id=\"shown\">Rodoma {Math.Min(all.Total, BillListPaging.First)} iš {all.Total}</p>");
        html.ShouldNotContain("?nuo=", customMessage: "the list grows in place by ?rodyti=, no next page under the cap");
        if (all.Total > BillListPaging.First)
        {
            html.ShouldContain($"href=\"/projektai?rodyti=40#bill-{all.Items[BillListPaging.First].Bill.Number}\" id=\"more\">Rodyti daugiau</a>", customMessage: "lands on the first bill added");
        }
        else
        {
            html.ShouldNotContain("id=\"more\"", customMessage: "every bill is shown");
        }

        var longer = await PageAsync("/projektai?rodyti=40");
        Regex.Count(longer, "<li id=\"bill-").ShouldBe(Math.Min(all.Total, 40));
        (await PageAsync("/projektai?rodyti=nesąmonė")).ShouldContain("id=\"shown\">Rodoma " + Math.Min(all.Total, BillListPaging.First), customMessage: "a value the page would not make is the default");
    }

    [Fact]
    public async Task An_mps_votes_come_as_many_as_asked_and_the_page_lists_them_by_day_growing_in_place()
    {
        using var visitor = NewBrowser();
        const string Slug = "aurelija-kadagyte";
        var all = await visitor.GetJsonAsync<MpVotesResponse>(ParliamentRoutes.MpVotesBySlug(Slug) + "?limit=300");
        all.Items.Count.ShouldBeGreaterThan(1, "the MP has votes in the run's data");
        var one = await visitor.GetJsonAsync<MpVotesResponse>(ParliamentRoutes.MpVotesBySlug(Slug) + "?limit=1");
        one.Items.Single().Vote.Id.ShouldBe(all.Items[0].Vote.Id);
        one.NextCursor.ShouldNotBeNull();
        (await visitor.GetJsonAsync<MpVotesResponse>(ParliamentRoutes.MpVotesBySlug(Slug) + "?limit=x")).Items.Count.ShouldBe(Math.Min(all.Items.Count, MpVoteLimits.Default));

        var html = await PageAsync($"/p/{Slug}/balsavimai");
        var day = Lt2.Web.Client.Display.DayOf(all.Items[0].Vote.HeldAt);
        html.ShouldContain($"<section class=\"tl-day tl-past\"");
        html.ShouldContain($"id=\"diena-{day:yyyy-MM-dd}\"");
        html.ShouldContain("id=\"balsavimas-1\"");
        html.ShouldContain($"href=\"/seimas/balsavimai/{all.Items[0].Vote.Id}\"");
        Regex.Count(html, "id=\"balsavimas-\\d+\"").ShouldBe(Math.Min(all.Items.Count, Lt2.Web.Client.Parliament.MpVotePaging.First));
        if (all.Items.Count > Lt2.Web.Client.Parliament.MpVotePaging.First)
        {
            html.ShouldContain($"href=\"/p/{Slug}/balsavimai?rodyti=100#balsavimas-51\" id=\"mp-votes-more\"");
        }
        else
        {
            html.ShouldNotContain("id=\"mp-votes-more\"");
        }
    }

    /// <summary>
    /// Owner, 2026-11-18: the bill's page shows two arguments a side; "Visi argumentai" works without script as a link to the
    /// page with every argument (<c>?argumentai=visi</c>), rendered by the server, with the discussions of all of them.
    /// </summary>
    [Fact]
    public async Task Every_argument_shows_without_script_by_the_address()
    {
        const string Bill = AppealSteps.ThirdBill;
        var moderator = (await ModeratorAsync()).Browser;
        var (first, _) = await Factory.MemberAsync("Pirmoji Rašytoja");
        using var _f = first;
        try
        {
            await Database.PrepareBillAsync(first, moderator, Bill, "for");
            foreach (var (name, text) in new[] { ("Antrasis Rašytojas", "Antras argumentas už: mažesnė našta dirbantiems."), ("Trečioji Rašytoja", "Trečias argumentas už: paprastesnė deklaracija.") })
            {
                var (writer, _) = await Factory.MemberAsync(name);
                using var _w = writer;
                using var written = await writer.WriteReasonAsync(Bill, "for", text);
                written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
            }

            using var visitor = NewBrowser();
            var third = (await visitor.RecordAsync(Bill)).Reasons.Where(r => r.Side == "for").ToList()[2].Id;
            var plain = await PageAsync("/seimas/" + Bill);
            plain.ShouldNotContain($"id=\"{BillTextLinks.ReasonElement(third)}\"", Case.Sensitive, "the third of its side waits behind \"show all\"");
            plain.ShouldMatch($"<a class=\"linkish\" id=\"reasons-all\" href=\"/seimas/{Bill}\\?argumentai=visi#reasons\">\\s*Visi argumentai: 3\\s*</a>");

            var all = await PageAsync($"/seimas/{Bill}?argumentai=visi");
            all.ShouldContain($"id=\"{BillTextLinks.ReasonElement(third)}\"", Case.Sensitive);
            all.ShouldContain($"id=\"aptarimas-panel-{third:N}\"", Case.Sensitive);
            all.ShouldMatch($"<a class=\"linkish\" id=\"reasons-all\" href=\"/seimas/{Bill}#reasons\">\\s*Rodyti mažiau\\s*</a>");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    /// <summary>Owner, 2026-11-18: every moderators' queue says how many wait in all, beside the hundred it shows.</summary>
    [Fact]
    public async Task Every_moderators_queue_says_how_many_wait_in_all()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var appeals = await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue);
        appeals.RecordsTotal.ShouldBeGreaterThanOrEqualTo(appeals.Records.Count);
        appeals.ReasonsTotal.ShouldBeGreaterThanOrEqualTo(appeals.Reasons.Count);
        (appeals.Records.Count < 100 ? appeals.RecordsTotal : 100).ShouldBe(appeals.Records.Count);
        (appeals.Reasons.Count < 100 ? appeals.ReasonsTotal : 100).ShouldBe(appeals.Reasons.Count);

        var comments = await moderator.GetJsonAsync<CommentModerationResponse>(CommentRoutes.Moderation);
        comments.ReportedTotal.ShouldBeGreaterThanOrEqualTo(comments.Reported.Count);
        comments.HiddenTotal.ShouldBeGreaterThanOrEqualTo(comments.Hidden.Count);
        var responses = await moderator.GetJsonAsync<MpResponseModerationResponse>(MpResponseRoutes.Moderation);
        responses.QueueTotal.ShouldBeGreaterThanOrEqualTo(responses.Queue.Count);
        responses.HiddenTotal.ShouldBeGreaterThanOrEqualTo(responses.Hidden.Count);
        var organisations = await moderator.GetJsonAsync<OrganisationModerationResponse>(OrganisationRoutes.ModerationQueue);
        organisations.OrganisationsTotal.ShouldBeGreaterThanOrEqualTo(organisations.Organisations.Count);
        organisations.StatementsTotal.ShouldBeGreaterThanOrEqualTo(organisations.Statements.Count);

        // Fewer than a hundred: the totals are the lists' own lengths.
        if (appeals.Reasons.Count < 100)
        {
            appeals.ReasonsTotal.ShouldBe(appeals.Reasons.Count);
        }
    }
}
