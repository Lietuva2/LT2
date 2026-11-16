using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;
using Lt2.Usage;
using Lt2.Usage.Counting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.27: the site's own usage statistics over HTTP. A page view counts the visitor (network and browser) or the
/// signed-in account, never both; robots, the API, errors, redirects and prefetches are no visit; a page counts under its
/// route, staff and MP pages stay out of the list; a referring site is its host, once per visitor and day; a restart counts
/// nobody twice, also with the database away at its start; an ended period keeps its count and loses its salt; only an
/// administrator in a staff session reads the numbers.
/// <para>
/// Each test counts on a site of its own whose counter runs on a clock the test sets, in a year of its own far ahead, and
/// writes only when the test says: every number it reads comes from its own requests alone, and it deletes those rows after.
/// </para>
/// </summary>
public sealed class UsageTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

    /// <summary>A browser's user agent unique to the test.</summary>
    private static string Browser() => $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/142.0 UsageTest/{Guid.NewGuid():N}";

    /// <summary>Noon (Vilnius) of a day in the test's own year.</summary>
    private static DateTimeOffset Noon(DateOnly day) => VilniusTime.StartOf(day).AddHours(12);

    private static TestBrowser Open(Lt2Factory site, string? userAgent = null)
    {
        var browser = site.NewBrowser();
        browser.Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent ?? Browser());
        return browser;
    }

    private static async Task GetAsync(TestBrowser browser, string path, HttpStatusCode expected = HttpStatusCode.OK, Action<HttpRequestMessage>? with = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        with?.Invoke(request);
        using var response = await browser.Client.SendAsync(request);
        response.StatusCode.ShouldBe(expected, path);
    }

    private sealed record Total(int Visitors, int Members, int VisitorViews, int MemberViews, int CrawlerViews);

    private async Task<Total> TotalAsync(string period, DateOnly starts)
    {
        var row = await Database.ScalarAsync<string>(
            "SELECT concat_ws(',', visitors, members, visitor_views, member_views, crawler_views) FROM usage.total WHERE period = @p AND starts = @s",
            ("p", period), ("s", starts));
        if (row is null)
        {
            return new Total(0, 0, 0, 0, 0);
        }

        var n = row.Split(',').Select(int.Parse).ToArray();
        return new Total(n[0], n[1], n[2], n[3], n[4]);
    }

    private Task<Dictionary<string, int>> PagesAsync(DateOnly day) => RowsAsync("SELECT path || '=' || views FROM usage.page WHERE day = @d", day);

    private Task<Dictionary<string, int>> ReferrersAsync(DateOnly day) => RowsAsync("SELECT host || '=' || views FROM usage.referrer WHERE day = @d", day);

    private async Task<Dictionary<string, int>> RowsAsync(string sql, DateOnly day)
    {
        var joined = await Database.ScalarAsync<string>($"SELECT string_agg(x, E'\\n') FROM ({sql}) AS t(x)", ("d", day));
        return (joined ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private Task<long> SketchesAsync(string period, DateOnly starts) =>
        Database.ScalarAsync<long>("SELECT count(*) FROM usage.sketch WHERE period = @p AND starts = @s", ("p", period), ("s", starts));

    /// <summary>Deletes every row of the test's own year.</summary>
    private async Task ForgetYearAsync(int year)
    {
        var (from, to) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        await Database.ExecuteAsync("DELETE FROM usage.sketch WHERE starts BETWEEN @f AND @t", ("f", from), ("t", to));
        await Database.ExecuteAsync("DELETE FROM usage.total WHERE starts BETWEEN @f AND @t", ("f", from), ("t", to));
        await Database.ExecuteAsync("DELETE FROM usage.page WHERE day BETWEEN @f AND @t", ("f", from), ("t", to));
        await Database.ExecuteAsync("DELETE FROM usage.referrer WHERE day BETWEEN @f AND @t", ("f", from), ("t", to));
    }

    /// <summary>
    /// A site whose counter runs on a clock the test sets and writes only when the test flushes it (no minute's timer),
    /// started: its first write has read the stored salts, as a real start does (unless the database is "away").
    /// </summary>
    private async Task<UsageSite> SiteAsync(DateTimeOffset now, int failingWrites = 0, string? proxy = null)
    {
        var site = new UsageSite(Database, new ManualClock(now), failingWrites, proxy);
        site.NewBrowser().Dispose();
        await site.Counter.FlushAsync(CancellationToken.None);
        return site;
    }

    private sealed class UsageSite(DatabaseFixture database, ManualClock clock, int failingWrites, string? proxy) : Lt2Factory(database)
    {
        public ManualClock Clock { get; } = clock;

        public UsageCounter Counter => Services.GetRequiredService<UsageCounter>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (proxy is not null)
            {
                builder.UseSetting("ReverseProxy:Address", proxy);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<UsageCounter>();
                services.AddSingleton(sp => new UsageCounter(
                    new FailingScopes(sp.GetRequiredService<IServiceScopeFactory>(), failingWrites), Clock, NullLogger<UsageCounter>.Instance));
                foreach (var flush in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(UsageFlushService)).ToList())
                {
                    services.Remove(flush);
                }
            });
        }
    }

    /// <summary>The database "away" for the first <c>failures</c> writes.</summary>
    private sealed class FailingScopes(IServiceScopeFactory inner, int failures) : IServiceScopeFactory
    {
        private int _left = failures;

        public IServiceScope CreateScope() =>
            Interlocked.Decrement(ref _left) >= 0 ? throw new InvalidOperationException("The database is away (test).") : inner.CreateScope();
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Set(DateTimeOffset now) => _now = now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class ThrowingCounter : IUsageCounter
    {
        public void Count(PageView view) => throw new InvalidOperationException("Counting failed (test).");

        public UsageOnline Online() => new(0, 0);

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingSite(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUsageCounter>();
                services.AddSingleton<IUsageCounter, ThrowingCounter>();
            });
        }
    }

    [Fact]
    public async Task A_visitor_counts_once_a_day_month_and_year_and_every_page_they_open_counts()
    {
        var day = new DateOnly(2091, 5, 12);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            using var visitor = Open(site);
            await GetAsync(visitor, "/metodika");
            await GetAsync(visitor, "/metodika");
            await GetAsync(visitor, "/privatumas");
            site.Counter.Online().ShouldBe(new UsageOnline(1, 0));
            await site.Counter.FlushAsync(CancellationToken.None);

            (await TotalAsync(UsageRules.Day, day)).ShouldBe(new Total(1, 0, 3, 0, 0));
            (await TotalAsync(UsageRules.Month, new DateOnly(2091, 5, 1))).ShouldBe(new Total(1, 0, 3, 0, 0));
            (await TotalAsync(UsageRules.Year, new DateOnly(2091, 1, 1))).ShouldBe(new Total(1, 0, 3, 0, 0));
            (await PagesAsync(day)).ShouldBe(new Dictionary<string, int> { ["/metodika"] = 2, ["/privatumas"] = 1 }, ignoreOrder: true);

            site.Clock.Advance(UsageRules.OnlineWindow + TimeSpan.FromSeconds(1));
            site.Counter.Online().ShouldBe(new UsageOnline(0, 0), "no page in the last five minutes");
        }
        finally
        {
            await ForgetYearAsync(2091);
        }
    }

    [Fact]
    public async Task Two_browsers_behind_one_address_count_as_two_visitors()
    {
        var day = new DateOnly(2092, 2, 3);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            using var laptop = Open(site);
            using var phone = Open(site);
            await GetAsync(laptop, "/");
            await GetAsync(phone, "/");
            await site.Counter.FlushAsync(CancellationToken.None);

            (await TotalAsync(UsageRules.Day, day)).Visitors.ShouldBe(2);
        }
        finally
        {
            await ForgetYearAsync(2092);
        }
    }

    [Fact]
    public async Task A_signed_in_member_counts_as_the_account_whatever_browser_string_it_sends()
    {
        var day = new DateOnly(2093, 7, 20);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            using var member = Open(site, "curl/8.4.0");
            await member.RegisterAndSignInAsync();
            await GetAsync(member, "/metodika");
            await GetAsync(member, "/paskyra");
            site.Counter.Online().ShouldBe(new UsageOnline(0, 1));
            await site.Counter.FlushAsync(CancellationToken.None);

            (await TotalAsync(UsageRules.Day, day)).ShouldBe(new Total(0, 1, 0, 2, 0), "a signed-in session is a person, not a robot");
            (await TotalAsync(UsageRules.Year, new DateOnly(2093, 1, 1))).Members.ShouldBe(1);
        }
        finally
        {
            await ForgetYearAsync(2093);
        }
    }

    [Fact]
    public async Task Robots_the_api_errors_redirects_head_requests_and_prefetches_are_no_visits()
    {
        var day = new DateOnly(2094, 3, 9);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            using var robot = Open(site, "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");
            await GetAsync(robot, "/metodika");
            using var tool = Open(site, "curl/8.4.0");
            await GetAsync(tool, "/metodika");

            using var visitor = Open(site);
            await GetAsync(visitor, Lt2.Api.Contracts.Agenda.AgendaRoutes.Agenda);
            await GetAsync(visitor, "/no-such-page-" + Guid.NewGuid().ToString("N"), HttpStatusCode.NotFound);
            await GetAsync(visitor, "/kreipimaisi", HttpStatusCode.MovedPermanently);
            await GetAsync(visitor, "/nerasta");
            await GetAsync(visitor, "/metodika", with: r => r.Headers.TryAddWithoutValidation("Sec-Purpose", "prefetch;prerender"));
            using (var head = new HttpRequestMessage(HttpMethod.Head, "/metodika"))
            using (await visitor.Client.SendAsync(head))
            {
            }

            await site.Counter.FlushAsync(CancellationToken.None);
            (await TotalAsync(UsageRules.Day, day)).ShouldBe(new Total(0, 0, 0, 0, 2));
            (await PagesAsync(day)).ShouldBeEmpty("robots' pages are not among the people's");
        }
        finally
        {
            await ForgetYearAsync(2094);
        }
    }

    [Fact]
    public async Task A_page_counts_under_its_route_so_case_and_the_query_change_nothing_and_the_old_token_route_is_gone()
    {
        var day = new DateOnly(2095, 10, 1);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            var query = "q" + Guid.NewGuid().ToString("N");
            using var visitor = Open(site);
            await GetAsync(visitor, "/METODIKA");
            await GetAsync(visitor, "/metodika?kas=" + query);
            await GetAsync(visitor, "/seimas/XVP-0618");
            await GetAsync(visitor, "/organizacijos/kvietimas/tok" + Guid.NewGuid().ToString("N"), HttpStatusCode.NotFound);
            await site.Counter.FlushAsync(CancellationToken.None);

            (await PagesAsync(day)).ShouldBe(new Dictionary<string, int> { ["/metodika"] = 2, ["/seimas/XVP-0618"] = 1 }, ignoreOrder: true);
            (await TotalAsync(UsageRules.Day, day)).VisitorViews.ShouldBe(3);
        }
        finally
        {
            await ForgetYearAsync(2095);
        }
    }

    [Fact]
    public async Task Staff_and_mp_pages_count_in_the_totals_but_never_in_the_list_of_pages()
    {
        var day = new DateOnly(2096, 6, 6);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            using var member = Open(site);
            await member.RegisterAndSignInAsync();
            await GetAsync(member, "/seimo-narys/projektai");
            await GetAsync(member, "/administravimas/lankomumas");
            await GetAsync(member, "/moderavimas");
            await site.Counter.FlushAsync(CancellationToken.None);

            (await TotalAsync(UsageRules.Day, day)).MemberViews.ShouldBe(3);
            (await PagesAsync(day)).ShouldBeEmpty();
        }
        finally
        {
            await ForgetYearAsync(2096);
        }
    }

    /// <summary>
    /// Design 8.26 meets 8.27: the analytics page counts as a public page under its own route (not as a bill's
    /// <c>/seimas/{Number}</c>), without its filters; the member's own comparison is an API read and no visit.
    /// </summary>
    [Fact]
    public async Task The_analytics_page_counts_under_its_own_route_and_the_members_own_comparison_is_no_visit()
    {
        var day = new DateOnly(2103, 9, 14);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            using var visitor = Open(site);
            await GetAsync(visitor, "/seimas/analitika");
            using var member = Open(site);
            await member.RegisterAndSignInAsync();
            await GetAsync(member, "/seimas/analitika?metai=2026&paskyros=senesnes");
            await GetAsync(member, Lt2.Api.Contracts.Analytics.AnalyticsRoutes.MyAgreement);
            await site.Counter.FlushAsync(CancellationToken.None);

            (await PagesAsync(day)).ShouldBe(new Dictionary<string, int> { ["/seimas/analitika"] = 2 });
            (await TotalAsync(UsageRules.Day, day)).ShouldBe(new Total(1, 1, 1, 1, 0));
        }
        finally
        {
            await ForgetYearAsync(2103);
        }
    }

    [Fact]
    public async Task A_referring_site_is_its_host_counted_once_per_visitor_and_day_and_never_its_address()
    {
        var day = new DateOnly(2097, 4, 15);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            var secret = "s" + Guid.NewGuid().ToString("N");
            using var visitor = Open(site);
            foreach (var (host, path) in new[] { ("naujienos", "/metodika"), ("naujienos", "/privatumas"), ("forumas", "/"), ("dienrastis", "/"), ("ketvirta", "/") })
            {
                await GetAsync(visitor, path, with: r => r.Headers.Referrer = new Uri($"https://www.{host}.test:8443/straipsnis/{secret}?id={secret}"));
            }

            await GetAsync(visitor, "/metodika", with: r => r.Headers.Referrer = new Uri(Lt2Factory.Origin + "/privatumas"));
            using var other = Open(site);
            await GetAsync(other, "/metodika", with: r => r.Headers.Referrer = new Uri("https://naujienos.test/kitas"));
            await site.Counter.FlushAsync(CancellationToken.None);

            (await ReferrersAsync(day)).ShouldBe(
                new Dictionary<string, int> { ["naujienos.test"] = 2, ["forumas.test"] = 1, ["dienrastis.test"] = 1 }, ignoreOrder: true,
                customMessage: "a visitor's second visit from one site adds nothing, their fourth site is not counted, the site's own pages are no referral");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM usage.referrer WHERE host LIKE @s", ("s", "%" + secret + "%"))).ShouldBe(0);
        }
        finally
        {
            await ForgetYearAsync(2097);
        }
    }

    [Fact]
    public async Task A_restart_counts_a_visitor_once_even_when_the_database_was_away_at_its_start_and_views_wait_for_it()
    {
        var day = new DateOnly(2098, 9, 9);
        try
        {
            var userAgent = Browser();
            await using (var first = await SiteAsync(Noon(day)))
            {
                using var visitor = Open(first, userAgent);
                await GetAsync(visitor, "/metodika");
                await first.Counter.FlushAsync(CancellationToken.None);
            }

            // Restarted with the database away for its start and its next write: views are kept, nobody is counted yet.
            await using var restarted = await SiteAsync(Noon(day).AddMinutes(5), failingWrites: 2);
            restarted.Counter.Ready.ShouldBeFalse();
            using (var again = Open(restarted, userAgent))
            {
                await GetAsync(again, "/metodika");
                await restarted.Counter.FlushAsync(CancellationToken.None);
                restarted.Counter.Ready.ShouldBeFalse();
                await restarted.Counter.FlushAsync(CancellationToken.None);
                restarted.Counter.Ready.ShouldBeTrue("the stored salts are read");
                await GetAsync(again, "/metodika");
                await restarted.Counter.FlushAsync(CancellationToken.None);
            }

            (await TotalAsync(UsageRules.Day, day)).ShouldBe(new Total(1, 0, 3, 0, 0), "one visitor, every view kept through the failed writes");
            (await TotalAsync(UsageRules.Year, new DateOnly(2098, 1, 1))).Visitors.ShouldBe(1);
            (await PagesAsync(day))["/metodika"].ShouldBe(3);
        }
        finally
        {
            await ForgetYearAsync(2098);
        }
    }

    [Fact]
    public async Task A_page_still_answers_when_counting_fails()
    {
        await using var site = new ThrowingSite(Database);
        using var visitor = Open(site);
        await GetAsync(visitor, "/metodika");
    }

    [Fact]
    public async Task An_ended_day_month_and_year_keep_their_counts_and_lose_their_salts_while_the_rest_keep_theirs()
    {
        var march31 = new DateOnly(2099, 3, 31);
        var april1 = new DateOnly(2099, 4, 1);
        var (march, april, year) = (new DateOnly(2099, 3, 1), new DateOnly(2099, 4, 1), new DateOnly(2099, 1, 1));
        try
        {
            await using var site = await SiteAsync(VilniusTime.StartOf(april1).AddSeconds(-30));
            using var visitor = Open(site);
            await GetAsync(visitor, "/metodika");
            site.Clock.Advance(TimeSpan.FromMinutes(1));
            await GetAsync(visitor, "/metodika");
            await site.Counter.FlushAsync(CancellationToken.None);

            (await TotalAsync(UsageRules.Day, march31)).ShouldBe(new Total(1, 0, 1, 0, 0), "its only visitor came in its last minute");
            (await TotalAsync(UsageRules.Day, april1)).Visitors.ShouldBe(1);
            (await TotalAsync(UsageRules.Month, march)).Visitors.ShouldBe(1);
            (await TotalAsync(UsageRules.Month, april)).Visitors.ShouldBe(1);
            (await TotalAsync(UsageRules.Year, year)).ShouldBe(new Total(1, 0, 2, 0, 0), "the same visitor, the same year");
            (await SketchesAsync(UsageRules.Day, march31)).ShouldBe(0, "no salt is stored for a day that is over");
            (await SketchesAsync(UsageRules.Month, march)).ShouldBe(0);
            (await SketchesAsync(UsageRules.Day, april1)).ShouldBe(1);
            (await SketchesAsync(UsageRules.Month, april)).ShouldBe(1);
            (await SketchesAsync(UsageRules.Year, year)).ShouldBe(1);

            site.Clock.Set(VilniusTime.StartOf(new DateOnly(2100, 1, 1)).AddSeconds(30));
            await site.Counter.FlushAsync(CancellationToken.None);
            (await SketchesAsync(UsageRules.Year, year)).ShouldBe(0, "the year is over");
            (await TotalAsync(UsageRules.Year, year)).Visitors.ShouldBe(1);
        }
        finally
        {
            await ForgetYearAsync(2099);
            await ForgetYearAsync(2100);
        }
    }

    [Fact]
    public async Task A_forwarded_ipv6_address_counts_by_its_network_and_a_request_without_an_address_counts_its_view_only()
    {
        var day = new DateOnly(2101, 1, 20);
        try
        {
            await using var site = await SiteAsync(Noon(day), proxy: Lt2Factory.ClientAddress.ToString());
            foreach (var forwarded in new[] { "2001:db8:1:2::1", "2001:db8:1:2:aaaa::99", "2001:db8:1:3::1" })
            {
                using var visitor = Open(site, Chrome);
                await GetAsync(visitor, "/metodika", with: r => r.Headers.TryAddWithoutValidation("X-Forwarded-For", forwarded));
            }

            site.Counter.Count(new PageView("GET", "/metodika", "/metodika", new Dictionary<string, string?>(), 200, "text/html", false, null, Chrome, null, null, "lt2.test"));
            await site.Counter.FlushAsync(CancellationToken.None);

            (await TotalAsync(UsageRules.Day, day)).ShouldBe(new Total(2, 0, 4, 0, 0), "two networks; the request without an address is a view, not a visitor");
        }
        finally
        {
            await ForgetYearAsync(2101);
        }
    }

    [Fact]
    public async Task A_day_keeps_at_most_two_thousand_pages_and_counts_the_rest_together()
    {
        var day = new DateOnly(2102, 8, 8);
        try
        {
            await using var site = await SiteAsync(Noon(day));
            static PageView View(string path) =>
                new("GET", path, path, new Dictionary<string, string?>(), 200, "text/html", false, Lt2Factory.ClientAddress, Chrome, null, null, "lt2.test");
            for (var i = 0; i < UsageRules.PathsPerDay + 5; i++)
            {
                site.Counter.Count(View($"/bandymas-{i:D5}"));
            }

            await site.Counter.FlushAsync(CancellationToken.None);
            site.Counter.Count(View("/dar-vienas"));
            site.Counter.Count(View("/bandymas-00000"));
            await site.Counter.FlushAsync(CancellationToken.None);

            var pages = await PagesAsync(day);
            pages.Count.ShouldBe(UsageRules.PathsPerDay);
            pages[UsageRules.OtherPaths].ShouldBe(6 + 1, "the six over the cap and the new one");
            pages["/bandymas-00000"].ShouldBe(2, "a page already kept still counts");
        }
        finally
        {
            await ForgetYearAsync(2102);
        }
    }

    [Fact]
    public async Task Only_an_administrator_in_a_staff_session_reads_the_numbers_and_they_say_nothing_about_anyone()
    {
        using (var visitor = Open(Factory))
        {
            await GetAsync(visitor, "/metodika");
        }

        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(ApiRoutes.Admin.Usage)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var member = NewBrowser();
        await member.RegisterAndSignInAsync();
        (await member.Client.GetAsync(ApiRoutes.Admin.Usage)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var moderator = await ModeratorAsync();
        (await moderator.Browser.Client.GetAsync(ApiRoutes.Admin.Usage)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var admin = await AdministratorAsync();
        using (var codeSession = await Factory.CodeSessionAsync(admin.Email))
        {
            (await codeSession.Client.GetAsync(ApiRoutes.Admin.Usage)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "a code session is a member's, whatever the account's roles");
        }

        using var answer = await admin.Browser.Client.GetAsync(UsageRoutes.Api(30));
        var json = await answer.Content.ReadAsStringAsync();
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(
            ["days", "months", "onlineMembers", "onlineVisitors", "pages", "rangeDays", "referrers", "years"]);
        root.GetProperty("days")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(
            ["crawlerViews", "memberViews", "members", "starts", "visitorViews", "visitors"]);
        root.GetProperty("rangeDays").GetInt32().ShouldBe(30);
        root.GetProperty("pages").GetArrayLength().ShouldBeGreaterThan(0, "the sweep holds what it is about");
        root.GetProperty("pages")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(["name", "views"]);

        var unknownRange = await admin.Browser.GetJsonAsync<UsageResponse>(UsageRoutes.Api(90));
        unknownRange.RangeDays.ShouldBe(UsageRoutes.DefaultRange, "only 1, 7 or 30 days");
    }
}
