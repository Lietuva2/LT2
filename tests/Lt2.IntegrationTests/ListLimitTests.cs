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
/// place by the address, the arguments come a page at a time and grow without script, and every moderators' queue says how many wait.
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
        Regex.Count(html, "<li class=\"tl-item\" id=\"bill-").ShouldBe(Math.Min(all.Total, BillListPaging.First));
        html.ShouldContain($"id=\"shown\">Rodoma {Math.Min(all.Total, BillListPaging.First)} iš {all.Total}</p>");
        html.ShouldNotContain("?nuo=", customMessage: "the list grows in place by ?rodyti=, no next page under the cap");
        if (all.Total > BillListPaging.First)
        {
            html.ShouldContain($"href=\"/projektai?rodyti=70\" id=\"more\">Rodyti daugiau</a>", customMessage: "20, then 50 more (owner, 2026-11-19)");
        }
        else
        {
            html.ShouldNotContain("id=\"more\"", customMessage: "every bill is shown");
        }

        var longer = await PageAsync("/projektai?rodyti=70");
        Regex.Count(longer, "<li class=\"tl-item\" id=\"bill-").ShouldBe(Math.Min(all.Total, 70));
        longer.ShouldContain($"id=\"shown\">Rodoma {Math.Min(all.Total, 70)} iš {all.Total}</p>");
        if (all.Total > 70)
        {
            longer.ShouldContain($"href=\"/projektai?rodyti=120\" id=\"more\">", customMessage: "50 more again");
        }

        (await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, limit: 71))).Items.Count
            .ShouldBe(Math.Min(all.Total, 71), "a grown page and the one bill the link lands on fit the API's limit");
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
        html.ShouldContain($"id=\"day-{day:yyyy-MM-dd}\"");
        html.ShouldContain("id=\"vote-1\"");
        // Design 8.16.5 (owner, 2026-11-21): the list's sheet has no heading of its own, so the page's title stays at the top of
        // the screen; the way back above it scrolls away.
        ListHeadSteps.PageHead(html).ShouldMatch("^<div class=\"page-head\" id=\"page-head\"><h1>[^<]+: balsavimai</h1></div>$");
        html.ShouldContain($"href=\"/seimas/balsavimai/{all.Items[0].Vote.Id}\"");
        Regex.Count(html, "id=\"vote-\\d+\"").ShouldBe(Math.Min(all.Items.Count, Lt2.Web.Client.Parliament.MpVotePaging.First));
        if (all.Items.Count > Lt2.Web.Client.Parliament.MpVotePaging.First)
        {
            html.ShouldContain($"href=\"/p/{Slug}/balsavimai?rodyti=100\" id=\"mp-votes-more\"");
        }
        else
        {
            html.ShouldNotContain("id=\"mp-votes-more\"");
        }
    }

    /// <summary>
    /// Owner, 2026-11-19: the arguments a page at a time. The bill's answer carries each side's first two and how many each side
    /// has; <c>GET …/record/reasons?side=&amp;cursor=&amp;limit=</c> gives the rest in the page's order, and <c>GET /reasons/{id}</c>
    /// one published argument (404 for any other). The page shows two a side, "Rodoma 2 iš 3" and "Rodyti daugiau argumentų", a
    /// link to the page with that side grown by 20 (<c>?argumentai-uz=22</c>, owner 2026-11-19), which the server renders with the discussions of all of
    /// them; a linked argument beyond the first two stands first in its side.
    /// </summary>
    [Fact]
    public async Task The_arguments_come_a_page_at_a_time_and_grow_without_script_by_the_address()
    {
        const string Bill = AppealSteps.ThirdBill;
        var moderator = (await ModeratorAsync()).Browser;
        var (first, _) = await Factory.MemberAsync("Pirmoji Rašytoja");
        using var _f = first;
        var (last, _) = await Factory.MemberAsync("Trečioji Rašytoja");
        using var _l = last;
        try
        {
            await Database.PrepareBillAsync(first, moderator, Bill, "for");
            var (second, _) = await Factory.MemberAsync("Antrasis Rašytojas");
            using (second)
            {
                using var written = await second.WriteReasonAsync(Bill, "for", "Antras argumentas už: mažesnė našta dirbantiems.");
                written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
            }

            using (var written = await last.WriteReasonAsync(Bill, "for", "Trečias argumentas už: paprastesnė deklaracija."))
            {
                written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
            }

            using var visitor = NewBrowser();
            var record = await visitor.RecordAsync(Bill);
            record.Reasons.Count(r => r.Side == "for").ShouldBe(ReasonLimits.First, "the bill's answer carries each side's first two only");
            record.ReasonTotals.ShouldBe(new ReasonTotalsResponse(3, 0));

            var page = await visitor.GetJsonAsync<ReasonPageResponse>(AppealRoutes.ReasonPageOf(Bill, "for", null, 2));
            (page.Side, page.Total, page.NextCursor).ShouldBe(("for", 3, "2"));
            page.Items.Select(r => r.Id).ShouldBe(record.Reasons.Where(r => r.Side == "for").Select(r => r.Id), "the same order as the bill's answer");
            var rest = await visitor.GetJsonAsync<ReasonPageResponse>(AppealRoutes.ReasonPageOf(Bill, "for", "2", ReasonLimits.Step));
            rest.NextCursor.ShouldBeNull();
            var third = rest.Items.ShouldHaveSingleItem();
            third.Text.ShouldBe("Trečias argumentas už: paprastesnė deklaracija.");
            (await visitor.GetJsonAsync<ReasonPageResponse>(AppealRoutes.ReasonPageOf(Bill, "against"))).Items.ShouldBeEmpty();
            foreach (var asked in new[] { "0", "abc", "20", "100", "101", "100000" })
            {
                var answer = await visitor.GetJsonAsync<ReasonPageResponse>(AppealRoutes.ReasonsOf(Bill) + "?side=for&limit=" + asked);
                answer.Items.Count.ShouldBe(asked == "0" ? 1 : 3, $"limit={asked} is taken or falls back, never an error");
            }

            using (var sideways = await visitor.Client.GetAsync(AppealRoutes.ReasonPageOf(Bill, "kitaip"), TestContext.Current.CancellationToken))
            {
                await sideways.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "side.invalid");
            }

            using (var unknown = await visitor.Client.GetAsync(AppealRoutes.ReasonPageOf("XVP-NERA", "for"), TestContext.Current.CancellationToken))
            {
                await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.not-found");
            }

            var one = await visitor.GetJsonAsync<ReasonOnBillResponse>(AppealRoutes.ReasonOf(third.Id));
            (one.BillNumber, one.Reason.Id, one.Reason.Text).ShouldBe((Bill, third.Id, third.Text));
            using (var none = await visitor.Client.GetAsync(AppealRoutes.ReasonOf(Guid.NewGuid()), TestContext.Current.CancellationToken))
            {
                await none.ShouldBeProblemAsync(HttpStatusCode.NotFound, "reason.not-found");
            }

            // Without script: two a side, the side's count, and the link to the grown page.
            var plain = await PageAsync("/seimas/" + Bill);
            plain.ShouldNotContain($"id=\"{BillTextLinks.ReasonElement(third.Id)}\"", Case.Sensitive, "the third of its side waits for \"Rodyti daugiau argumentų\"");
            plain.ShouldContain("id=\"reasons-more-for-shown\">Rodoma 2 iš 3</p>", Case.Sensitive);
            plain.ShouldContain($"href=\"/seimas/{Bill}?argumentai-uz=22\" id=\"reasons-more-for\">Rodyti daugiau argumentų</a>", Case.Sensitive);
            plain.ShouldNotContain("id=\"reasons-more-against", Case.Sensitive, "no arguments against: nothing to grow");

            var grown = await PageAsync($"/seimas/{Bill}?argumentai-uz=22");
            grown.ShouldContain($"id=\"{BillTextLinks.ReasonElement(third.Id)}\"", Case.Sensitive);
            grown.ShouldContain("id=\"arguments-for-3\"", Case.Sensitive, "the first extra argument is present");
            grown.ShouldContain($"id=\"discussion-panel-{third.Id:N}\"", Case.Sensitive);
            grown.ShouldContain("id=\"reasons-more-for-shown\">Rodoma 3 iš 3</p>", Case.Sensitive);
            grown.ShouldNotContain("id=\"reasons-more-for\"", Case.Sensitive, "every argument of the side is shown");

            var linked = await PageAsync(BillTextLinks.ToReason(Bill, third.Id).Split('#')[0]);
            var at = linked.IndexOf($"id=\"{BillTextLinks.ReasonElement(third.Id)}\"", StringComparison.Ordinal);
            at.ShouldBeGreaterThan(0);
            at.ShouldBeLessThan(linked.IndexOf("Antras argumentas už", StringComparison.Ordinal), "a linked argument stands first in its side");

            // Withdrawn, it is no longer one to show: the same 404 as an unknown one.
            using (var withdrawn = await last.SendAsync(HttpMethod.Delete, AppealRoutes.ReasonOf(third.Id)))
            {
                withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await withdrawn.Content.ReadAsStringAsync());
            }

            using (var gone = await visitor.Client.GetAsync(AppealRoutes.ReasonOf(third.Id), TestContext.Current.CancellationToken))
            {
                await gone.ShouldBeProblemAsync(HttpStatusCode.NotFound, "reason.not-found");
            }
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
