using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Lt2.Usage;
using Lt2.Usage.Counting;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.27: the site's own usage statistics over HTTP. A page view counts the visitor (network and browser) or the
/// signed-in account, never both; robots, the API, files, errors and prefetches count as no visit; a link's token and the
/// query never reach a counted path; a referring site is its host only; a period's salt and sketch go when it ends and its
/// count stays; only an administrator in a staff session reads the numbers, and they say nothing about anyone.
/// </summary>
public sealed class UsageTests(DatabaseFixture database) : ApiTest(database)
{
    /// <summary>A browser's user agent unique to the test, so its visitor is new to the day.</summary>
    private static string Browser() => $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/142.0 UsageTest/{Guid.NewGuid():N}";

    private TestBrowser Visitor(string? userAgent = null)
    {
        var browser = NewBrowser();
        browser.Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent ?? Browser());
        return browser;
    }

    private async Task<UsageResponse> ReportAsync(int days = 1)
    {
        var admin = await AdministratorAsync();
        return await admin.Browser.GetJsonAsync<UsageResponse>(UsageRoutes.Api(days));
    }

    private static UsagePeriodResponse Today(UsageResponse report) => report.Days[^1];

    private static UsagePeriodResponse ThisMonth(UsageResponse report) => report.Months[^1];

    private static UsagePeriodResponse ThisYear(UsageResponse report) => report.Years[^1];

    /// <summary>Today's views of a path, as written by the last report's flush.</summary>
    private async Task<int> ViewsAsync(string path) =>
        await Database.ScalarAsync<int?>("SELECT views FROM usage.page WHERE day = @d AND path = @p", ("d", VilniusTime.Today(TimeProvider.System)), ("p", path)) ?? 0;

    private static async Task OpenAsync(TestBrowser browser, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await browser.Client.GetAsync(path);
        response.StatusCode.ShouldBe(expected, path);
    }

    [Fact]
    public async Task A_visitor_counts_once_a_day_month_and_year_and_every_page_they_open_counts()
    {
        var before = await ReportAsync();
        var (methodology, privacy) = (await ViewsAsync("/metodika"), await ViewsAsync("/privatumas"));
        using var visitor = Visitor();
        await OpenAsync(visitor, "/metodika");
        await OpenAsync(visitor, "/metodika");
        await OpenAsync(visitor, "/privatumas");
        var after = await ReportAsync();

        (Today(after).Visitors - Today(before).Visitors).ShouldBe(1);
        (ThisMonth(after).Visitors - ThisMonth(before).Visitors).ShouldBe(1);
        (ThisYear(after).Visitors - ThisYear(before).Visitors).ShouldBe(1);
        (Today(after).VisitorViews - Today(before).VisitorViews).ShouldBe(3);
        (ThisMonth(after).VisitorViews - ThisMonth(before).VisitorViews).ShouldBe(3);
        (Today(after).Members - Today(before).Members).ShouldBe(0);
        (await ViewsAsync("/metodika") - methodology).ShouldBe(2);
        (await ViewsAsync("/privatumas") - privacy).ShouldBe(1);
        (after.OnlineVisitors - before.OnlineVisitors).ShouldBe(1, "now on the site");
    }

    [Fact]
    public async Task Two_browsers_behind_one_address_count_as_two_visitors()
    {
        var before = await ReportAsync();
        using var laptop = Visitor();
        using var phone = Visitor();
        await OpenAsync(laptop, "/");
        await OpenAsync(phone, "/");
        var after = await ReportAsync();

        (Today(after).Visitors - Today(before).Visitors).ShouldBe(2);
    }

    [Fact]
    public async Task A_signed_in_member_counts_as_the_account_and_not_as_a_visitor()
    {
        using var member = Visitor();
        await member.RegisterAndSignInAsync();
        var before = await ReportAsync();
        await OpenAsync(member, "/metodika");
        await OpenAsync(member, "/sveiki");
        var after = await ReportAsync();

        (Today(after).Members - Today(before).Members).ShouldBe(1);
        (ThisYear(after).Members - ThisYear(before).Members).ShouldBe(1);
        (Today(after).MemberViews - Today(before).MemberViews).ShouldBe(2);
        (Today(after).Visitors - Today(before).Visitors).ShouldBe(0);
        (Today(after).VisitorViews - Today(before).VisitorViews).ShouldBe(0);
        (after.OnlineMembers - before.OnlineMembers).ShouldBe(1);
    }

    [Fact]
    public async Task Robots_the_api_errors_redirects_head_requests_and_prefetches_are_no_visits()
    {
        var before = await ReportAsync();
        var methodology = await ViewsAsync("/metodika");

        using var robot = Visitor("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");
        await OpenAsync(robot, "/metodika");
        using var tool = Visitor("curl/8.4.0");
        await OpenAsync(tool, "/metodika");

        using var visitor = Visitor();
        await OpenAsync(visitor, Lt2.Api.Contracts.Agenda.AgendaRoutes.Agenda);
        await OpenAsync(visitor, "/no-such-page-" + Guid.NewGuid().ToString("N"), HttpStatusCode.NotFound);
        await OpenAsync(visitor, "/kreipimaisi", HttpStatusCode.MovedPermanently);
        using (var head = new HttpRequestMessage(HttpMethod.Head, "/metodika"))
        {
            using var answer = await visitor.Client.SendAsync(head);
        }

        using (var prefetch = new HttpRequestMessage(HttpMethod.Get, "/metodika"))
        {
            prefetch.Headers.TryAddWithoutValidation("Sec-Purpose", "prefetch");
            using var answer = await visitor.Client.SendAsync(prefetch);
            answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var after = await ReportAsync();
        (Today(after).CrawlerViews - Today(before).CrawlerViews).ShouldBe(2);
        (Today(after).Visitors - Today(before).Visitors).ShouldBe(0);
        (Today(after).VisitorViews - Today(before).VisitorViews).ShouldBe(0);
        (await ViewsAsync("/metodika") - methodology).ShouldBe(0, "robots' pages are not among the people's");
        (after.OnlineVisitors - before.OnlineVisitors).ShouldBe(0);
    }

    [Fact]
    public async Task A_links_token_and_the_query_never_reach_a_counted_path()
    {
        // An MP's preferences page answers 200 only to a real link (design 8.19.4): the letter gives every sitting MP one.
        await Database.ResetMpMailAsync();
        try
        {
            var admin = await AdministratorAsync();
            (await admin.IntroduceAsync()).ShouldBeGreaterThan(0);
            var token = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);
            var query = "q" + Guid.NewGuid().ToString("N");
            await ReportAsync();
            var (redacted, methodology) = (await ViewsAsync("/suvestines/nustatymai/***"), await ViewsAsync("/metodika"));

            using var visitor = Visitor();
            await OpenAsync(visitor, Lt2.Api.Contracts.Digests.MpMailRoutes.PreferencesPageOf(token));
            await OpenAsync(visitor, "/metodika?kas=" + query);
            await OpenAsync(visitor, "/suvestines/nustatymai/" + new string('b', 43), HttpStatusCode.NotFound);
            await ReportAsync();

            (await ViewsAsync("/suvestines/nustatymai/***") - redacted).ShouldBe(1, "counted, with the token replaced; an unknown one is no page");
            (await ViewsAsync("/metodika") - methodology).ShouldBe(1, "counted under the path alone");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM usage.page WHERE path LIKE @t OR path LIKE @q", ("t", "%" + token + "%"), ("q", "%" + query + "%")))
                .ShouldBe(0);
        }
        finally
        {
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task A_referring_site_is_counted_by_its_host_and_never_its_address()
    {
        var host = $"naujienos-{Guid.NewGuid():N}.test";
        var secret = "s" + Guid.NewGuid().ToString("N");
        using var visitor = Visitor();
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/metodika"))
        {
            request.Headers.Referrer = new Uri($"https://www.{host}/straipsnis/{secret}?id={secret}");
            using var answer = await visitor.Client.SendAsync(request);
            answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var own = new HttpRequestMessage(HttpMethod.Get, "/privatumas"))
        {
            own.Headers.Referrer = new Uri(Lt2Factory.Origin + "/metodika");
            using var answer = await visitor.Client.SendAsync(own);
            answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var after = await ReportAsync();
        after.Referrers.ShouldContain(new UsageItemResponse(host, 1));
        after.Referrers.ShouldNotContain(r => r.Name.Contains("lt2.test", StringComparison.Ordinal), "moving between the site's own pages is no referral");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM usage.referrer WHERE host LIKE @s", ("s", "%" + secret + "%"))).ShouldBe(0);
    }

    [Fact]
    public async Task A_restarted_site_counts_with_the_stored_salt_so_a_visitor_before_and_after_counts_once()
    {
        var userAgent = Browser();
        using var visitor = Visitor(userAgent);
        await OpenAsync(visitor, "/metodika");
        var before = await ReportAsync();

        // Another instance of the application starts on the same database: at start it takes the salts of the periods under way.
        await using (var restarted = new Lt2Factory(Database))
        {
            using var again = restarted.NewBrowser();
            again.Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
            await OpenAsync(again, "/metodika");
            await restarted.Services.GetRequiredService<IUsageCounter>().FlushAsync(CancellationToken.None);
        }

        var after = await ReportAsync();
        (Today(after).Visitors - Today(before).Visitors).ShouldBe(0, "the same network and browser, the same day");
        (ThisMonth(after).Visitors - ThisMonth(before).Visitors).ShouldBe(0);
        (Today(after).VisitorViews - Today(before).VisitorViews).ShouldBe(1);
    }

    [Fact]
    public async Task A_period_under_way_keeps_its_salt_and_one_that_ended_keeps_only_its_count()
    {
        // Periods long over, which no other test touches: a sketch of three visitors left from before a restart.
        var day = new DateOnly(2020, 1, 15);
        var month = new DateOnly(2020, 1, 1);
        var sketch = new Sketch();
        var salt = Sketch.NewSalt();
        foreach (var agent in new[] { "a", "b", "c" })
        {
            sketch.Add(Sketch.Hash(salt, Sketch.VisitorData([192, 0, 2, 1], agent)));
        }

        const string Insert = "INSERT INTO usage.sketch (period, starts, audience, salt, registers) VALUES (@p, @s, 'visitor', @salt, @r)";
        try
        {
            await Database.ExecuteAsync(Insert, ("p", "day"), ("s", day), ("salt", salt), ("r", sketch.ToBytes()));
            await Database.ExecuteAsync(Insert, ("p", "month"), ("s", month), ("salt", salt), ("r", sketch.ToBytes()));

            using var visitor = Visitor();
            await OpenAsync(visitor, "/metodika");
            await ReportAsync();

            (await Database.ScalarAsync<long>("SELECT count(*) FROM usage.sketch WHERE starts IN (@d, @m)", ("d", day), ("m", month)))
                .ShouldBe(0, "the salt and the sketch of an ended period are deleted");
            (await Database.ScalarAsync<int>("SELECT visitors FROM usage.total WHERE period = 'day' AND starts = @d", ("d", day))).ShouldBe(3);
            (await Database.ScalarAsync<int>("SELECT visitors FROM usage.total WHERE period = 'month' AND starts = @m", ("m", month))).ShouldBe(3);

            var today = VilniusTime.Today(TimeProvider.System);
            (await Database.ScalarAsync<long>(
                    "SELECT count(*) FROM usage.sketch WHERE audience = 'visitor' AND ((period = 'day' AND starts = @d) OR (period = 'month' AND starts = @m) OR (period = 'year' AND starts = @y))",
                    ("d", today), ("m", new DateOnly(today.Year, today.Month, 1)), ("y", new DateOnly(today.Year, 1, 1))))
                .ShouldBe(3, "today's, this month's and this year's salts stay while they last");
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM usage.sketch WHERE starts IN (@d, @m)", ("d", day), ("m", month));
            await Database.ExecuteAsync("DELETE FROM usage.total WHERE starts IN (@d, @m)", ("d", day), ("m", month));
        }
    }

    [Fact]
    public async Task Only_an_administrator_in_a_staff_session_reads_the_numbers_and_they_say_nothing_about_anyone()
    {
        using var stranger = Visitor();
        (await stranger.Client.GetAsync(ApiRoutes.Admin.Usage)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var member = Visitor();
        await member.RegisterAndSignInAsync();
        (await member.Client.GetAsync(ApiRoutes.Admin.Usage)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var moderator = await ModeratorAsync();
        (await moderator.Browser.Client.GetAsync(ApiRoutes.Admin.Usage)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await AdministratorAsync();
        using var answer = await admin.Browser.Client.GetAsync(UsageRoutes.Api(30));
        var json = await answer.Content.ReadAsStringAsync();
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(
            ["days", "months", "onlineMembers", "onlineVisitors", "pages", "rangeDays", "referrers", "years"]);
        root.GetProperty("days")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(
            ["crawlerViews", "memberViews", "members", "starts", "visitorViews", "visitors"]);
        root.GetProperty("days").GetArrayLength().ShouldBe(30);
        root.GetProperty("months").GetArrayLength().ShouldBe(12);
        root.GetProperty("rangeDays").GetInt32().ShouldBe(30);
        root.GetProperty("pages").GetArrayLength().ShouldBeGreaterThan(0, "the sweep holds what it is about");
        root.GetProperty("pages")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(["name", "views"]);
    }
}
