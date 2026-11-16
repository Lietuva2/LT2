using System.Net;
using System.Security.Cryptography;
using Lt2.Usage.Counting;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.27: the site's own usage statistics. Only pages answered 200 count; robots apart; a page counts under its route;
/// staff and MP pages stay out of the list; a visitor is the network and the browser, hashed with a salt of the period; a
/// period's salt goes when it ends; the numbers are approximate.
/// </summary>
public sealed class UsageRuleTests
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

    private static readonly Dictionary<string, string?> NoValues = [];

    [Theory]
    [InlineData("GET", "/seimas/XVP-0412", 200, "text/html; charset=utf-8", false, true)]
    [InlineData("GET", "/", 200, "text/html", false, true)]
    [InlineData("POST", "/atsiliepimai", 200, "text/html", false, false)]
    [InlineData("HEAD", "/", 200, "text/html", false, false)]
    [InlineData("GET", "/nerasta", 404, "text/html", false, false)]
    [InlineData("GET", "/nerasta", 200, "text/html", false, false)]
    [InlineData("GET", "/klaida", 200, "text/html", false, false)]
    [InlineData("GET", "/kreipimaisi", 301, null, false, false)]
    [InlineData("GET", "/klaida", 500, "text/html", false, false)]
    [InlineData("GET", "/api/v1/agenda", 200, "application/json", false, false)]
    [InlineData("GET", "/api/v1/whatever", 200, "text/html", false, false)]
    [InlineData("GET", "/_framework/blazor.web.js", 200, "text/javascript", false, false)]
    [InlineData("GET", "/health/ready", 200, "text/html", false, false)]
    [InlineData("GET", "/openapi/v1.json", 200, "application/json", false, false)]
    [InlineData("GET", "/css/site.css", 200, "text/css", false, false)]
    [InlineData("GET", "/darbotvarke", 200, "text/html", true, false)]
    [InlineData("GET", "/apie", 200, "text/html", false, true)]
    public void Only_a_page_answered_200_with_html_to_a_get_counts_never_the_api_files_errors_redirects_or_prefetches(
        string method, string path, int status, string? contentType, bool prefetch, bool counts) =>
        UsageRules.IsPageView(method, path, status, contentType, prefetch).ShouldBe(counts);

    [Fact]
    public void A_path_that_only_begins_like_the_api_is_still_a_page()
    {
        UsageRules.IsPageView("GET", "/apie", 200, "text/html", false).ShouldBeTrue();
        UsageRules.IsPageView("GET", "/healthy-life", 200, "text/html", false).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("curl/8.4.0", true)]
    [InlineData("python-requests/2.31", true)]
    [InlineData("WhatsApp/2.23.20", true)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", true)]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)", true)]
    [InlineData("Mozilla/5.0 (compatible; Discordbot/2.0; +https://discordapp.com)", true)]
    [InlineData("Mozilla/5.0 (compatible) Slackbot-LinkExpanding 1.0", true)]
    [InlineData("Mozilla/5.0 (compatible; TelegramBot (like TwitterBot))", true)]
    [InlineData("Mozilla/5.0 (compatible; SomeTool; +https://irankis.test/apie)", true)]
    [InlineData("Mozilla/5.0 facebookexternalhit/1.1", true)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/120.0 Safari/537.36", true)]
    [InlineData(Chrome, false)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1", false)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 12; Cubot Note 30 Build/SP1A.210812.016) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 11; CUBOT_X30) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36", false)]
    public void A_robot_is_one_that_says_so_or_is_no_browser_at_all_and_a_phone_whose_make_holds_the_letters_is_none(string? userAgent, bool crawler) =>
        UsageRules.IsCrawler(userAgent).ShouldBe(crawler);

    [Fact]
    public void An_ipv4_address_is_counted_whole_and_an_ipv6_address_by_its_network()
    {
        UsageRules.Network(IPAddress.Parse("192.0.2.10")).ShouldBe([192, 0, 2, 10]);
        UsageRules.Network(IPAddress.Parse("::ffff:192.0.2.10")).ShouldBe([192, 0, 2, 10], "a mapped address is the IPv4 one");
        UsageRules.Network(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"))
            .ShouldBe(UsageRules.Network(IPAddress.Parse("2001:db8:1:2::1")), "the same /64: the same network");
        UsageRules.Network(IPAddress.Parse("2001:db8:1:3::1"))
            .ShouldNotBe(UsageRules.Network(IPAddress.Parse("2001:db8:1:2::1")), "another /64: another network");
        UsageRules.Network(null).ShouldBeNull();
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("/metodika", "/metodika")]
    [InlineData("/METODIKA", "/metodika")]
    [InlineData("/darbotvarke/", "/darbotvarke")]
    public void A_page_without_parameters_counts_under_its_route_in_small_letters(string template, string counted) =>
        UsageRules.CountedPath(template, NoValues).ShouldBe(counted);

    [Theory]
    [InlineData("/seimas/{Number}", "Number", "xvp-0412", "/seimas/XVP-0412")]
    [InlineData("/seimas/{Number}/istorija", "Number", "XVP-0412", "/seimas/XVP-0412/istorija")]
    [InlineData("/p/{Slug}", "slug", "Jonas-Petraitis", "/p/jonas-petraitis")]
    [InlineData("/o/{Slug}", "Slug", "bendrija", "/o/bendrija")]
    [InlineData("/seimas/balsavimai/{Id:long}", "Id", "12345", "/seimas/balsavimai/12345")]
    [InlineData("/suvestines/{Date}", "Date", "2026-11-16", "/suvestines/2026-11-16")]
    public void A_public_objects_page_keeps_its_value_written_one_way(string template, string name, string value, string counted) =>
        UsageRules.CountedPath(template, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [name] = value }).ShouldBe(counted);

    [Theory]
    [InlineData("/suvestines/nustatymai/{Token}", "Token", "AbCdEf0123456789xyz")]
    [InlineData("/suvestines/kopijos/{Token}", "Token", "AbCdEf0123456789xyz")]
    [InlineData("/kazkas/{Code?}", "Code", "slaptas")]
    [InlineData("/failai/{*Rest}", "Rest", "a/b/c")]
    public void Any_other_parameter_a_links_token_first_never_reaches_a_counted_path(string template, string name, string value)
    {
        var counted = UsageRules.CountedPath(template, new Dictionary<string, string?> { [name] = value });
        counted.ShouldEndWith("/" + UsageRules.Hidden);
        counted.ShouldNotContain(value);
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("tarpas yra")]
    [InlineData("eilute\nkita")]
    [InlineData("labai-ilgas-labai-ilgas-labai-ilgas-labai-ilgas-labai-ilgas-labai-ilgas")]
    [InlineData("")]
    [InlineData(null)]
    public void A_public_parameter_that_is_no_plain_short_name_is_hidden_too(string? value) =>
        UsageRules.CountedPath("/o/{Slug}", new Dictionary<string, string?> { ["Slug"] = value }).ShouldBe("/o/" + UsageRules.Hidden);

    [Fact]
    public void A_page_without_a_route_is_counted_with_the_others() =>
        UsageRules.CountedPath(null, NoValues).ShouldBe(UsageRules.OtherPaths);

    [Theory]
    [InlineData("/administravimas/lankomumas", false)]
    [InlineData("/moderavimas", false)]
    [InlineData("/moderavimas/paslepti", false)]
    [InlineData("/seimo-narys/projektai", false)]
    [InlineData("/suvestines/nustatymai/***", false)]
    [InlineData("/suvestines/kopijos/***", false)]
    [InlineData("/organizacijos/kvietimas", false)]
    [InlineData("/prisijungti/raktas", false)]
    [InlineData("/suvestines/2026-11-16", true)]
    [InlineData("/organizacijos", true)]
    [InlineData("/seimas/XVP-0412", true)]
    [InlineData("/sveiki", true)]
    public void Staff_mp_and_link_pages_are_left_out_of_the_list_so_nobodys_activity_shows(string path, bool listed) =>
        UsageRules.IsListed(path).ShouldBe(listed);

    [Theory]
    [InlineData("https://www.naujienos.test/some/post?id=1", "naujienos.test")]
    [InlineData("https://news.example.test/a", "news.example.test")]
    [InlineData("http://EXAMPLE.test./", "example.test")]
    [InlineData("https://forumas.test:8443/tema", "forumas.test")]
    [InlineData("https://žinios.test/", "xn--inios-tib.test")]
    [InlineData("https://lt2.test/seimas", null)]
    [InlineData("https://www.lt2.test/seimas", null)]
    [InlineData("android-app://com.google.android.gm/", null)]
    [InlineData("not an address", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_referring_site_is_only_another_sites_host_without_www_or_port_never_its_path(string? referrer, string? host) =>
        UsageRules.ReferrerHost(referrer, "lt2.test").ShouldBe(host);

    [Fact]
    public void A_referring_host_longer_than_a_host_can_be_is_not_counted()
    {
        var label = new string('a', 60);
        UsageRules.ReferrerHost($"https://{label}.{label}.{label}.{label}.{label}.test/", "lt2.test").ShouldBeNull();
    }

    [Fact]
    public void A_day_belongs_to_its_month_and_its_year()
    {
        UsageRules.PeriodsOf(new DateOnly(2026, 11, 16)).ShouldBe(
        [
            (UsageRules.Day, new DateOnly(2026, 11, 16)),
            (UsageRules.Month, new DateOnly(2026, 11, 1)),
            (UsageRules.Year, new DateOnly(2026, 1, 1)),
        ]);
    }

    [Fact]
    public void A_periods_salt_is_kept_until_the_period_is_over_and_not_a_day_longer()
    {
        UsageRules.HasEnded(UsageRules.Day, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16)).ShouldBeFalse();
        UsageRules.HasEnded(UsageRules.Day, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 17)).ShouldBeTrue();
        UsageRules.HasEnded(UsageRules.Month, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)).ShouldBeFalse();
        UsageRules.HasEnded(UsageRules.Month, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)).ShouldBeTrue();
        UsageRules.HasEnded(UsageRules.Year, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).ShouldBeFalse();
        UsageRules.HasEnded(UsageRules.Year, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)).ShouldBeTrue();
    }

    [Fact]
    public void Now_on_the_site_means_a_page_in_the_last_five_minutes()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        UsageRules.IsOnline(now.AddMinutes(-5), now).ShouldBeTrue();
        UsageRules.IsOnline(now.AddMinutes(-5).AddSeconds(-1), now).ShouldBeFalse();
    }

    [Fact]
    public void A_day_keeps_its_pages_until_the_cap_most_viewed_first_and_counts_the_rest_together()
    {
        var batch = new Dictionary<string, int> { ["/a"] = 1, ["/b"] = 5, ["/c"] = 3, ["/stored"] = 2 };
        var placed = UsageCounter.Capped(batch, ["/stored", "/old"], storedCount: 2, cap: 4, other: "(kiti)").ToList();

        placed.ShouldBe(
        [
            ("/b", 5, true),
            ("/stored", 2, false),
            ("(kiti)", 4, true),
        ], "room for one new page beside the place kept for the rest: the most viewed; the others counted together");
    }

    [Fact]
    public void The_rest_add_to_a_stored_rest_and_a_full_day_takes_no_new_page()
    {
        var placed = UsageCounter.Capped(new Dictionary<string, int> { ["/new"] = 1, ["(kiti)"] = 2 }, ["(kiti)"], storedCount: 4, cap: 4, other: "(kiti)").ToList();
        placed.ShouldBe([("(kiti)", 3, false)]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(1_000)]
    [InlineData(25_000)]
    [InlineData(200_000)]
    public void A_sketch_counts_distinct_visitors_within_about_two_percent(int distinct)
    {
        // A fixed salt, so the test gives the same answer every run (a random one is within 2 % all but rarely).
        var salt = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var sketch = new Sketch();
        for (var i = 0; i < distinct; i++)
        {
            var hash = Sketch.Hash(salt, Sketch.VisitorData([203, 0, 113, (byte)i], "Mozilla/5.0 " + i));
            sketch.Add(hash);
            sketch.Add(hash);
        }

        var estimate = sketch.Estimate();
        Math.Abs(estimate - distinct).ShouldBeLessThanOrEqualTo(Math.Max(1, (int)(distinct * 0.02)), $"estimated {estimate} for {distinct}");
    }

    [Fact]
    public void A_visitor_seen_many_times_in_a_period_counts_once()
    {
        var salt = Sketch.NewSalt();
        var sketch = new Sketch();
        for (var i = 0; i < 500; i++)
        {
            sketch.Add(Sketch.Hash(salt, Sketch.VisitorData([192, 0, 2, 10], Chrome)));
        }

        sketch.Estimate().ShouldBe(1);
    }

    [Fact]
    public void Two_browsers_behind_one_address_count_as_two_and_one_browser_on_two_networks_as_two()
    {
        var salt = Enumerable.Range(1, 32).Select(i => (byte)(i * 5)).ToArray();
        var sketch = new Sketch();
        sketch.Add(Sketch.Hash(salt, Sketch.VisitorData([192, 0, 2, 10], Chrome)));
        sketch.Add(Sketch.Hash(salt, Sketch.VisitorData([192, 0, 2, 10], "Mozilla/5.0 (iPhone)")));
        sketch.Estimate().ShouldBe(2);
        sketch.Add(Sketch.Hash(salt, Sketch.VisitorData([198, 51, 100, 7], Chrome)));
        sketch.Estimate().ShouldBe(3);
    }

    [Fact]
    public void The_same_visitor_hashes_differently_under_another_periods_salt_so_nothing_links_the_periods()
    {
        var data = Sketch.VisitorData([192, 0, 2, 10], Chrome);
        Sketch.Hash(Sketch.NewSalt(), data).ShouldNotBe(Sketch.Hash(Sketch.NewSalt(), data));
        Sketch.NewSalt().ShouldNotBe(Sketch.NewSalt(), "a salt is random, never made again from a date or a key");
    }

    [Fact]
    public void A_visitor_and_an_account_are_never_the_same_entry()
    {
        var salt = Sketch.NewSalt();
        Sketch.MemberData(Guid.Empty).ShouldNotBe(Sketch.VisitorData(new byte[16], ""));
        Sketch.Hash(salt, Sketch.MemberData(Guid.CreateVersion7())).ShouldNotBe(Sketch.Hash(salt, Sketch.MemberData(Guid.CreateVersion7())));
    }

    [Fact]
    public void Merging_two_sketches_of_a_period_counts_their_union_once()
    {
        var salt = Enumerable.Range(1, 32).Select(i => (byte)(i * 3)).ToArray();
        var first = new Sketch();
        var second = new Sketch();
        for (var i = 0; i < 300; i++)
        {
            var hash = Sketch.Hash(salt, BitConverter.GetBytes(i));
            (i < 200 ? first : second).Add(hash);
            if (i is >= 100 and < 200)
            {
                second.Add(hash);
            }
        }

        first.Merge(second);
        first.Estimate().ShouldBeInRange(294, 306, "300 distinct, 100 of them in both");
    }

    [Fact]
    public void A_stored_sketch_reads_back_the_same_and_anything_else_reads_as_empty()
    {
        var sketch = new Sketch();
        sketch.Add(Sketch.Hash(Sketch.NewSalt(), RandomNumberGenerator.GetBytes(8)));
        Sketch.From(sketch.ToBytes()).Estimate().ShouldBe(1);
        Sketch.From(null).IsEmpty.ShouldBeTrue();
        Sketch.From([1, 2, 3]).IsEmpty.ShouldBeTrue();
        new Sketch().Estimate().ShouldBe(0);
    }
}
