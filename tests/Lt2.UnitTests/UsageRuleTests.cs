using System.Net;
using System.Security.Cryptography;
using Lt2.Usage.Counting;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.27: the site's own usage statistics. Only pages answered 200 count; robots apart; a visitor is the network and
/// the browser, hashed with a salt of the period; a period's salt goes when it ends; the numbers are approximate.
/// </summary>
public sealed class UsageRuleTests
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

    [Theory]
    [InlineData("GET", "/seimas/XVP-0412", 200, "text/html; charset=utf-8", false, true)]
    [InlineData("GET", "/", 200, "text/html", false, true)]
    [InlineData("POST", "/atsiliepimai", 200, "text/html", false, false)]
    [InlineData("HEAD", "/", 200, "text/html", false, false)]
    [InlineData("GET", "/nerasta", 404, "text/html", false, false)]
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
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", true)]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0)", true)]
    [InlineData("facebookexternalhit/1.1", true)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/120.0 Safari/537.36", true)]
    [InlineData(Chrome, false)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1", false)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0", false)]
    public void A_robot_is_one_that_says_so_or_is_no_browser_at_all(string? userAgent, bool crawler) =>
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
    [InlineData(null, "/")]
    [InlineData("/seimas/XVP-0412/", "/seimas/XVP-0412")]
    [InlineData("/suvestines/nustatymai/***", "/suvestines/nustatymai/***")]
    public void A_path_is_counted_without_its_trailing_slash(string? path, string counted) =>
        UsageRules.PathOf(path).ShouldBe(counted);

    [Fact]
    public void Too_long_a_path_is_counted_with_the_others()
    {
        UsageRules.PathOf("/" + new string('a', UsageRules.MaxPathLength)).ShouldBe(UsageRules.OtherPaths);
        UsageRules.PathOf("/" + new string('a', UsageRules.MaxPathLength - 1)).Length.ShouldBe(UsageRules.MaxPathLength);
    }

    [Theory]
    [InlineData("https://www.facebook.com/some/post?id=1", "facebook.com")]
    [InlineData("https://news.example.test/a", "news.example.test")]
    [InlineData("http://EXAMPLE.test./", "example.test")]
    [InlineData("https://lt2.test/seimas", null)]
    [InlineData("https://www.lt2.test/seimas", null)]
    [InlineData("android-app://com.google.android.gm/", null)]
    [InlineData("not an address", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_referring_site_is_only_another_sites_host_without_www_never_its_path(string? referrer, string? host) =>
        UsageRules.ReferrerHost(referrer, "lt2.test").ShouldBe(host);

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
            var hash = Sketch.Hash(salt, Sketch.VisitorData([10, 0, (byte)(i >> 8), (byte)i], "Mozilla/5.0 " + i));
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
        var salt = Sketch.NewSalt();
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
        var salt = Sketch.NewSalt();
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
