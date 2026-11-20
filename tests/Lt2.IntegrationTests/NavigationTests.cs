using System.Net;
using System.Text.RegularExpressions;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.21: the menu, the account item and the footer as the server renders them. Nothing about the person is in the page
/// as served: the header is the same for every member (the sign-out form's antiforgery token aside), staff included. The
/// footer, with the terms and the privacy notice (8.22), is on every page: public, a member's, and a page opened from an
/// MP's email link.
/// </summary>
public sealed partial class NavigationTests(DatabaseFixture database) : ApiTest(database)
{
    [GeneratedRegex("<header class=\"page top\">.*?</header>", RegexOptions.Singleline)]
    private static partial Regex Header();

    [GeneratedRegex("<footer class=\"page foot stack\">.*?</footer>", RegexOptions.Singleline)]
    private static partial Regex Footer();

    [GeneratedRegex("(name=\"__RequestVerificationToken\"[^>]*value=\")[^\"]*(\")")]
    private static partial Regex AntiforgeryValue();

    private static readonly string[] FooterLinks =
    [
        "/darbotvarke", "/projektai", "/seimas/balsavimai", "/suvestines", "/seimo-nariai", "/statistika",
        "/metodika", "/moderavimo-taisykles", "/taisykles", "/privatumas", "/organizacijos", "/atsiliepimai",
    ];

    private static string FooterOf(string html)
    {
        var footer = Footer().Match(html);
        footer.Success.ShouldBeTrue("the page has the footer");
        return WebUtility.HtmlDecode(footer.Value);
    }

    private static string HeaderOf(string html)
    {
        var header = Header().Match(html);
        header.Success.ShouldBeTrue("the page has the header");
        return header.Value;
    }

    [Fact]
    public async Task Two_members_and_a_moderator_get_the_same_header_apart_from_the_antiforgery_token()
    {
        var (ruta, rutaEmail) = await Factory.MemberAsync("Rūta Mockutė");
        using var _r = ruta;
        var (ona, onaEmail) = await Factory.MemberAsync("Ona Jonaitienė");
        using var _o = ona;
        var moderator = await ModeratorAsync();

        var headers = new List<string>();
        foreach (var (browser, name, email) in new[] { (ruta, "Rūta Mockutė", rutaEmail), (ona, "Ona Jonaitienė", onaEmail), (moderator.Browser, "", moderator.Email) })
        {
            var header = HeaderOf(await browser.Client.GetStringAsync("/metodika"));
            AntiforgeryValue().IsMatch(header).ShouldBeTrue("the sign-out form carries its token");
            if (name.Length > 0)
            {
                WebUtility.HtmlDecode(header).ShouldNotContain(name);
            }

            WebUtility.HtmlDecode(header).ShouldNotContain(email, Case.Insensitive);
            headers.Add(AntiforgeryValue().Replace(header, "$1…$2"));
        }

        headers[0].ShouldContain("id=\"account-menu\"", customMessage: "the account item, not the sign-in link");
        headers[0].ShouldContain(">Paskyra</summary>");
        headers[0].ShouldContain("data-account-name hidden");

        // The administrators' screens, the Hangfire dashboard among them (design 3, 6.3), hidden until the browser has read
        // what the account may open; the dashboard is no Blazor page, so its link is a full page load.
        headers[0].ShouldContain("<li data-account-link=\"usage\" hidden>");
        headers[0].ShouldContain("<li data-account-link=\"feedback\" hidden><a href=\"/moderavimas/atsiliepimai\">Atsiliepimai</a></li>");
        headers[0].ShouldContain("<li data-account-link=\"jobs\" hidden><a href=\"/administravimas/darbai\" data-enhance-nav=\"false\">Foniniai darbai</a></li>");
        headers[1].ShouldBe(headers[0]);
        headers[2].ShouldBe(headers[0], "a moderator's header names no staff screen of its own: the links are filled in the browser");
    }

    [Fact]
    public async Task The_menu_holds_projektai_seimo_nariai_statistika_and_prisijungti_and_marks_the_section()
    {
        using var visitor = NewBrowser();

        // Owner, 2026-11-17: "Projektai" is a dropdown of its tabs, a disclosure that opens without script; 2026-11-19:
        // "Balsavimai" is one of them again, the third. Design 8.28.2: each entry shows its line under its name, for everyone.
        var agenda = HeaderOf(await visitor.Client.GetStringAsync("/darbotvarke"));
        // Owner, 2026-11-19: where the device can hover, "Projektai" is a link to the agenda with the panel on hover; on a touch
        // screen a disclosure beside the same panel (CSS chooses).
        agenda.ShouldContain("<div class=\"drop drop--desc\" id=\"projects-menu\" data-nav-hover>");
        agenda.ShouldContain("<details class=\"drop__touch\" id=\"projects-touch\" data-nav-menu>");
        agenda.ShouldContain("<summary class=\"drop__toggle\" id=\"nav-projects-touch\" aria-current=\"true\">Projektai</summary>");
        agenda.ShouldContain("<div class=\"drop__panel drop__panel--start drop__panel--desc\" id=\"projects-panel\">");
        agenda.ShouldContain("<a class=\"drop__toggle drop__link\" href=\"/darbotvarke\" id=\"nav-projects\" aria-current=\"true\">Projektai</a>");
        var entries = WebUtility.HtmlDecode(agenda);
        entries.ShouldContain("<a href=\"/darbotvarke\" id=\"nav-tab-darbotvarke\" aria-current=\"page\"><span class=\"drop__label\">Darbotvarkė</span><span class=\"drop__desc\">Ką Seimas planuoja svarstyti artimiausiomis dienomis.</span></a>");
        entries.ShouldContain("<a href=\"/projektai\" id=\"nav-tab-projektai\"><span class=\"drop__label\">Projektai</span><span class=\"drop__desc\">Visi projektai, kurių Seimas dar nebaigė svarstyti.</span></a>");
        entries.ShouldContain("<a href=\"/seimas/balsavimai\" id=\"nav-tab-seimas-balsavimai\"><span class=\"drop__label\">Balsavimai</span><span class=\"drop__desc\">Kaip Seimas balsavo: visi balsavimai nuo naujausio.</span></a>");
        entries.ShouldContain("<a href=\"/suvestines\" id=\"nav-tab-suvestines\"><span class=\"drop__label\">Suvestinės</span><span class=\"drop__desc\">Ką apie dalyvių prašymus sužino Seimo nariai.</span></a>");
        Regex.Matches(agenda, "id=\"nav-tab-([a-z-]+)\"").Select(m => m.Groups[1].Value)
            .ShouldBe(["darbotvarke", "projektai", "seimas-balsavimai", "suvestines"]);
        agenda.ShouldContain("<a href=\"/seimo-nariai\" id=\"nav-mps\">Seimo nariai</a>");
        agenda.ShouldContain("<a href=\"/statistika\" id=\"nav-statistics\">Statistika</a>");
        agenda.ShouldContain("<a href=\"/prisijungti\" id=\"nav-sign-in\">Prisijungti</a>");
        Regex.Count(agenda, "<a ").ShouldBe(9, "the logo, Projektai, the four tabs in the dropdown and three items");
        agenda.ShouldNotContain("Kaip skaičiuojama");
        agenda.ShouldNotContain("data-theme-switch", customMessage: "the theme switch is in the footer");

        HeaderOf(await visitor.Client.GetStringAsync("/seimas/XVP-0412"))
            .ShouldContain("<a class=\"drop__toggle drop__link\" href=\"/darbotvarke\" id=\"nav-projects\" aria-current=\"true\">Projektai</a>");
        var votes = HeaderOf(await visitor.Client.GetStringAsync("/seimas/balsavimai"));
        votes.ShouldContain("<a class=\"drop__toggle drop__link\" href=\"/darbotvarke\" id=\"nav-projects\" aria-current=\"true\">Projektai</a>");
        votes.ShouldContain("<a href=\"/seimas/balsavimai\" id=\"nav-tab-seimas-balsavimai\" aria-current=\"page\"><span class=\"drop__label\">Balsavimai</span>");
        Regex.Count(votes, "aria-current=\"page\"").ShouldBe(1);
        HeaderOf(await visitor.Client.GetStringAsync("/seimo-nariai"))
            .ShouldContain("<a href=\"/seimo-nariai\" id=\"nav-mps\" aria-current=\"page\">Seimo nariai</a>");
        var statistics = HeaderOf(await visitor.Client.GetStringAsync("/statistika"));
        statistics.ShouldContain("<a href=\"/statistika\" id=\"nav-statistics\" aria-current=\"page\">Statistika</a>");
        statistics.ShouldContain("<a class=\"drop__toggle drop__link\" href=\"/darbotvarke\" id=\"nav-projects\">Projektai</a>");
        var methodology = HeaderOf(await visitor.Client.GetStringAsync("/metodika"));
        methodology.ShouldNotContain("aria-current");
    }

    [Theory]
    [InlineData("/darbotvarke", "tab-darbotvarke")]
    [InlineData("/projektai", "tab-projektai")]
    [InlineData("/seimas/balsavimai", "tab-seimas-balsavimai")]
    [InlineData("/suvestines", "tab-suvestines")]
    public async Task Each_of_the_four_pages_has_the_tab_row_with_its_own_tab_marked(string path, string tab)
    {
        using var visitor = NewBrowser();
        var html = await visitor.Client.GetStringAsync(path);

        var row = Regex.Match(html, "<nav class=\"row tabs project-tabs\".*?</nav>", RegexOptions.Singleline);
        row.Success.ShouldBeTrue();
        Regex.Matches(row.Value, "href=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
            .ShouldBe(["/darbotvarke", "/projektai", "/seimas/balsavimai", "/suvestines"], "owner, 2026-11-19: Balsavimai is the third tab again");
        row.Value.ShouldNotContain("/statistika", customMessage: "the statistics page is a menu item of its own (owner, 2026-11-17)");
        Regex.Count(row.Value, "aria-current=\"page\"").ShouldBe(1);
        row.Value.ShouldContain($"id=\"{tab}\" aria-current=\"page\"");
        html.IndexOf("<h1", StringComparison.Ordinal).ShouldBeLessThan(row.Index, "under the page's heading");
    }
    /// <summary>
    /// Design 8.28.4: "Kur ką rasti" under the four steps of <c>/metodika</c> lists exactly the items of the menu and the footer
    /// (the menu's, then the footer's own), each with its line and each name a link, and each link leads to its page.
    /// </summary>
    [Fact]
    public async Task Kur_ka_rasti_lists_exactly_the_items_of_the_menu_and_the_footer_and_each_link_leads_to_its_page()
    {
        using var visitor = NewBrowser();
        var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/metodika"));

        var part = Regex.Match(html, "<section class=\"stack\" aria-labelledby=\"where-title\" id=\"kur-ka-rasti\">.*?</section>", RegexOptions.Singleline);
        part.Success.ShouldBeTrue("the part is on the page");
        html.IndexOf("id=\"kaip-tai-veikia\"", StringComparison.Ordinal).ShouldBeLessThan(part.Index, "under the four steps");
        part.Value.ShouldContain("<h2 id=\"where-title\">Kur ką rasti</h2>");
        var links = Regex.Matches(part.Value, "<a href=\"([^\"]+)\" id=\"where-[a-z-]+\">([^<]+)</a></dt>\\s*<dd>([^<]+)</dd>")
            .Select(m => (Path: m.Groups[1].Value, Name: m.Groups[2].Value, Line: m.Groups[3].Value)).ToList();
        links.Select(l => l.Path).ShouldBe(
        [
            "/darbotvarke", "/projektai", "/seimas/balsavimai", "/suvestines", "/seimo-nariai", "/statistika", "/paskyra",
            "/metodika", "/moderavimo-taisykles", "/taisykles", "/privatumas", "/organizacijos", "/atsiliepimai",
        ]);
        links.Select(l => l.Path).Where(p => p != "/paskyra").ShouldBe(FooterLinks, "the footer's links, the account item besides");
        links.ShouldContain(("/seimo-nariai", "Seimo nariai", "Kiekvieno Seimo nario pareigos, balsavimai ir atsakymai."));
        links.ShouldContain(("/statistika", "Statistika", "Kiek projektų sulaukė dalyvių ir kur jų dauguma sutampa su Seimo dauguma."));
        links.ShouldContain(("/paskyra", "Paskyra", "Jūsų profilis, sekami projektai, laiškai ir duomenys."));
        Regex.Count(part.Value, "<dt class=\"where__sub\">").ShouldBe(4, "the four entries of Projektai, indented");

        foreach (var (path, _, _) in links)
        {
            using var response = await visitor.Client.GetAsync(path);
            // The account item sends a visitor to sign in and back (design 8.21); every other page answers itself.
            var expected = path == "/paskyra" ? new[] { HttpStatusCode.OK, HttpStatusCode.Redirect, HttpStatusCode.Found } : [HttpStatusCode.OK];
            response.StatusCode.ShouldBeOneOf(expected, path);
        }
    }

    [Fact]
    public async Task The_methodology_page_is_called_metodika()
    {
        using var visitor = NewBrowser();
        var html = await visitor.Client.GetStringAsync("/metodika");
        html.ShouldContain("<title>Metodika · Lietuva 2.0</title>");
        html.ShouldContain(">Metodika</h1>");
        html.ShouldNotContain("Kaip skaičiuojama");
    }

    [Fact]
    public async Task The_footer_with_the_terms_and_the_privacy_notice_is_on_a_public_page_a_members_page_and_a_token_page()
    {
        using var visitor = NewBrowser();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;

        var pages = new List<(string Where, string Html)>
        {
            ("public", await visitor.Client.GetStringAsync("/")),
            ("member", await member.Client.GetStringAsync("/paskyra")),
            ("account item", await member.Client.GetStringAsync("/projektai")),
        };

        // An MP's preferences page from an email link (design 8.19.4), opened by someone without an account; an unknown token
        // gets the same page as a wrong one, with the layout.
        using (var token = await visitor.Client.GetAsync("/suvestines/nustatymai/" + new string('b', 43)))
        {
            pages.Add(("token", await token.Content.ReadAsStringAsync()));
        }

        foreach (var (where, html) in pages)
        {
            var match = Footer().Match(html);
            match.Success.ShouldBeTrue($"{where}: the page has the footer");
            var footer = WebUtility.HtmlDecode(match.Value);
            foreach (var link in FooterLinks)
            {
                footer.ShouldContain($"href=\"{link}\"", customMessage: $"{where}: {link}");
            }

            footer.ShouldContain(">Naršyti</h2>");
            footer.ShouldContain(">Apie</h2>");
            footer.ShouldContain(">Dalyvauti</h2>");
            footer.ShouldContain(">Naudojimo taisyklės</a>");
            footer.ShouldContain(">Privatumas</a>");
            footer.ShouldContain("data-theme-switch data-permanent hidden", customMessage: $"{where}: the theme switch, hidden until its script runs");
            footer.ShouldContain("Lietuva 2.0 · uždaras bandymas");
        }
    }

    /// <summary>
    /// Owner, 2026-11-19: the footer repeats the menu's sections, so the end of a long page needs no scrolling back to the
    /// top: its first group lists the tabs of "Projektai", "Seimo nariai" and "Statistika" in the menu's order, with the
    /// menu's words, as plain links that mark nothing current (the menu does). The front page lost its row of links for it.
    /// </summary>
    [Fact]
    public async Task The_footer_repeats_the_menus_sections_in_the_menus_order_on_the_front_page_a_tab_and_a_members_page()
    {
        using var visitor = NewBrowser();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;

        foreach (var (where, html) in new[]
                 {
                     ("front page", await visitor.Client.GetStringAsync("/")),
                     ("agenda", await visitor.Client.GetStringAsync("/darbotvarke")),
                     ("statistics", await visitor.Client.GetStringAsync("/statistika")),
                     ("member", await member.Client.GetStringAsync("/paskyra")),
                 })
        {
            var footer = FooterOf(html);
            var group = Regex.Match(footer, "<section class=\"foot__group\" aria-labelledby=\"foot-browse\">.*?</section>", RegexOptions.Singleline);
            group.Success.ShouldBeTrue($"{where}: the group of the menu's sections");
            footer.IndexOf("id=\"foot-browse\"", StringComparison.Ordinal).ShouldBeLessThan(footer.IndexOf("id=\"foot-about\"", StringComparison.Ordinal), $"{where}: the first group");
            group.Value.ShouldContain("<h2 class=\"label\" id=\"foot-browse\">Naršyti</h2>");
            Regex.Matches(group.Value, "<li><a href=\"([^\"]+)\" id=\"([^\"]+)\">([^<]+)</a></li>")
                .Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value))
                .ShouldBe(
                [
                    ("/darbotvarke", "foot-tab-darbotvarke", "Darbotvarkė"),
                    ("/projektai", "foot-tab-projektai", "Projektai"),
                    ("/seimas/balsavimai", "foot-tab-seimas-balsavimai", "Balsavimai"),
                    ("/suvestines", "foot-tab-suvestines", "Suvestinės"),
                    ("/seimo-nariai", "foot-mps", "Seimo nariai"),
                    ("/statistika", "foot-statistics", "Statistika"),
                ],
                $"{where}: the menu's items in its order");
            footer.ShouldNotContain("aria-current", customMessage: $"{where}: the menu marks the current page, the footer does not");
        }

        // The menu holds the same sections in the same order (the dropdown's tabs, then the two items).
        var header = WebUtility.HtmlDecode(HeaderOf(await visitor.Client.GetStringAsync("/metodika")));
        Regex.Matches(header, "<a href=\"(/[^\"]+)\" id=\"nav-(?:tab-[a-z-]+|mps|statistics)\"").Select(m => m.Groups[1].Value)
            .ShouldBe(["/darbotvarke", "/projektai", "/seimas/balsavimai", "/suvestines", "/seimo-nariai", "/statistika"]);
    }

    [Theory]
    [InlineData("/taisykles", "Naudojimo taisyklės", "id=\"kaip-iseiti\"")]
    [InlineData("/privatumas", "Privatumo pranešimas", "id=\"slapukai\"")]
    public async Task The_terms_and_the_privacy_notice_are_static_pages_with_a_version(string path, string title, string part)
    {
        using var visitor = NewBrowser();
        using var response = await visitor.Client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        html.ShouldContain($"<title>{title} · Lietuva 2.0</title>");
        html.ShouldContain($">{title}</h1>");
        html.ShouldContain("Versija 2026-11-17, galioja nuo 2026-11-17.");
        html.ShouldContain(part);
        html.ShouldContain("[bus įrašyta]", customMessage: "what the owner has not given yet is marked, not invented");
    }

    [Fact]
    public async Task The_privacy_notice_lists_every_cookie_and_storage_key_of_the_inventory_and_nothing_else()
    {
        using var visitor = NewBrowser();
        var html = await visitor.Client.GetStringAsync("/privatumas");

        var listed = Regex.Matches(html, "data-storage=\"([^\"]+)\" data-storage-kind=\"([^\"]+)\"").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        listed.ShouldBe(
        [
            ("__Host-lt2", "cookie"),
            ("__Host-lt2.passkey", "cookie"),
            ("__Host-lt2.af", "cookie"),
            ("__Host-lt2.device", "cookie"),
            ("lt2-theme", "local"),
            ("blazor-resource-hash:Lt2.Web.Client", "local"),
            ("lt2.tikslas", "session"),
            ("lt2.kodas", "session"),
        ]);
    }

    [GeneratedRegex("<(a|form)\\b[^>]*?\\b(href|action)=\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LinkOrForm();

    /// <summary>
    /// Every page carries <c>&lt;base href="/"&gt;</c>, so a link whose address is only a fragment ("#part") resolves against
    /// the site's root and leads to the front page, not to a part of the page it is on (the account page's row of parts did).
    /// A link to a part of the same page carries the page's path ("/paskyra#profilis"). This walks the pages as the server
    /// renders them, as a visitor, a member, a moderator and an administrator: every routed page without parameters, then the
    /// site's own links found on them (a few of each kind), and fails on any <c>&lt;a href&gt;</c> or
    /// <c>&lt;form action&gt;</c> that starts with "#". SVG references (<c>&lt;use href&gt;</c>) are no navigation and are
    /// not looked at.
    /// </summary>
    [Fact]
    public async Task No_served_page_has_a_link_whose_address_is_only_a_fragment()
    {
        using var visitor = NewBrowser();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var moderator = await ModeratorAsync();
        var administrator = await AdministratorAsync();

        var seeds = new[] { typeof(Lt2.Web.Client.Pages.Hello).Assembly, typeof(Lt2.Web.Security.BrowserStorage).Assembly }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), false))
            .Select(r => ((Microsoft.AspNetCore.Components.RouteAttribute)r).Template)
            .Where(t => !t.Contains('{', StringComparison.Ordinal))
            .Select(t => "/" + t.TrimStart('/'))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        seeds.ShouldContain("/paskyra");

        var offenders = new List<string>();
        var visited = new Dictionary<string, HashSet<string>>();
        foreach (var (who, browser) in new[] { ("visitor", visitor), ("member", member), ("moderator", moderator.Browser), ("administrator", administrator.Browser) })
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var perKind = new Dictionary<string, int>(StringComparer.Ordinal);
            var queue = new Queue<string>(seeds);
            seen.UnionWith(seeds);
            var read = new HashSet<string>(StringComparer.Ordinal);
            while (queue.Count > 0 && read.Count < 250)
            {
                var path = queue.Dequeue();
                using var response = await browser.Client.GetAsync(path);
                if (response.Headers.Location is { } location && Internal(location.OriginalString) is { } target && seen.Add(target))
                {
                    queue.Enqueue(target);
                }

                if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentType?.MediaType != "text/html")
                {
                    continue;
                }

                read.Add(path);
                var html = await response.Content.ReadAsStringAsync();
                foreach (Match link in LinkOrForm().Matches(html))
                {
                    var address = WebUtility.HtmlDecode(link.Groups[3].Value);
                    if (address.StartsWith('#'))
                    {
                        offenders.Add($"{who} {path}: <{link.Groups[1].Value} {link.Groups[2].Value}=\"{address}\">");
                        continue;
                    }

                    // Follow a few of each kind of the site's own pages (a bill, an MP, a vote …), not every one of them.
                    if (Internal(address) is { } next && !seen.Contains(next))
                    {
                        var kind = Kind(next);
                        var count = perKind.GetValueOrDefault(kind);
                        if (count < 3)
                        {
                            perKind[kind] = count + 1;
                            seen.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            visited[who] = read;
        }

        // The sweep must be able to fail: it read the account page as the member, with its row of parts.
        visited["member"].ShouldContain("/paskyra");
        visited["member"].ShouldContain("/paskyra/saugumas", "the account screens are walked too");
        (await member.Client.GetStringAsync("/paskyra")).ShouldContain("id=\"account-parts\"");
        visited["visitor"].ShouldContain("/");
        visited["moderator"].ShouldContain("/moderavimas");
        offenders.ShouldBeEmpty($"pages read: {string.Join(", ", visited.Select(v => v.Key + " " + v.Value.Count))}");

        static string? Internal(string address)
        {
            if (!address.StartsWith('/') || address.StartsWith("//", StringComparison.Ordinal))
            {
                return null;
            }

            var path = address.Split('#')[0];
            var bare = path.Split('?')[0];
            return bare.StartsWith("/api/", StringComparison.Ordinal) || bare.StartsWith("/_", StringComparison.Ordinal)
                || bare.Split('/')[^1].Contains('.', StringComparison.Ordinal)
                ? null
                : path;
        }

        static string Kind(string path)
        {
            var segments = path.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
            return (segments.Length > 0 ? segments[0] : "") + "/" + segments.Length;
        }
    }

    [Fact]
    public void The_inventory_names_the_cookies_the_code_configures()
    {
        // Design 8.22.1: the notice is built from BrowserStorage.Inventory; these are the names the application really sets.
        var services = Factory.Services;
        var cookies = Lt2.Web.Security.BrowserStorage.Inventory.Where(i => i.Kind == Lt2.Web.Security.BrowserStorage.Cookie).Select(i => i.Name).ToList();
        var antiforgery = services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name;
        var authentication = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        cookies.ShouldBe(
        [
            authentication.Get(IdentityConstants.ApplicationScheme).Cookie.Name!,
            authentication.Get(IdentityConstants.TwoFactorUserIdScheme).Cookie.Name!,
            antiforgery!,
            Lt2.Identity.IdentityModule.DeviceCookieName,
        ]);
    }
}