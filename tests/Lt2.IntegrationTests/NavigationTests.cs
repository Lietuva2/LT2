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
        ["/metodika", "/moderavimo-taisykles", "/taisykles", "/privatumas", "/organizacijos", "/atsiliepimai"];

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
        headers[1].ShouldBe(headers[0]);
        headers[2].ShouldBe(headers[0], "a moderator's header names no staff screen of its own: the links are filled in the browser");
    }

    [Fact]
    public async Task The_menu_holds_projektai_seimo_nariai_and_prisijungti_and_marks_the_section()
    {
        using var visitor = NewBrowser();

        var agenda = HeaderOf(await visitor.Client.GetStringAsync("/darbotvarke"));
        agenda.ShouldContain("<a href=\"/darbotvarke\" id=\"nav-projects\" aria-current=\"page\">Projektai</a>");
        agenda.ShouldContain("<a href=\"/seimo-nariai\" id=\"nav-mps\">Seimo nariai</a>");
        agenda.ShouldContain("<a href=\"/prisijungti\" id=\"nav-sign-in\">Prisijungti</a>");
        Regex.Count(agenda, "<a ").ShouldBe(4, "the logo and three items");
        agenda.ShouldNotContain("Kaip skaičiuojama");
        agenda.ShouldNotContain("data-theme-switch", customMessage: "the theme switch is in the footer");

        HeaderOf(await visitor.Client.GetStringAsync("/seimas/XVP-0412"))
            .ShouldContain("<a href=\"/darbotvarke\" id=\"nav-projects\" aria-current=\"true\">Projektai</a>");
        HeaderOf(await visitor.Client.GetStringAsync("/seimas/balsavimai"))
            .ShouldContain("<a href=\"/darbotvarke\" id=\"nav-projects\" aria-current=\"true\">Projektai</a>");
        HeaderOf(await visitor.Client.GetStringAsync("/seimo-nariai"))
            .ShouldContain("<a href=\"/seimo-nariai\" id=\"nav-mps\" aria-current=\"page\">Seimo nariai</a>");
        var methodology = HeaderOf(await visitor.Client.GetStringAsync("/metodika"));
        methodology.ShouldNotContain("aria-current");
    }

    [Theory]
    [InlineData("/darbotvarke", "tab-darbotvarke")]
    [InlineData("/projektai", "tab-projektai")]
    [InlineData("/suvestines", "tab-suvestines")]
    public async Task Each_of_the_three_pages_has_the_tab_row_with_its_own_tab_marked(string path, string tab)
    {
        using var visitor = NewBrowser();
        var html = await visitor.Client.GetStringAsync(path);

        var row = Regex.Match(html, "<nav class=\"row tabs project-tabs\".*?</nav>", RegexOptions.Singleline);
        row.Success.ShouldBeTrue();
        Regex.Count(row.Value, "<a ").ShouldBe(3);
        row.Value.ShouldNotContain("/seimas/balsavimai", customMessage: "the history of votes is no tab");
        Regex.Count(row.Value, "aria-current=\"page\"").ShouldBe(1);
        row.Value.ShouldContain($"id=\"{tab}\" aria-current=\"page\"");
        html.IndexOf("<h1", StringComparison.Ordinal).ShouldBeLessThan(row.Index, "under the page's heading");
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
            ("member", await member.Client.GetStringAsync("/sveiki")),
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

            footer.ShouldContain(">Apie</h2>");
            footer.ShouldContain(">Dalyvauti</h2>");
            footer.ShouldContain(">Naudojimo taisyklės</a>");
            footer.ShouldContain(">Privatumas</a>");
            footer.ShouldContain("data-theme-switch data-permanent hidden", customMessage: $"{where}: the theme switch, hidden until its script runs");
            footer.ShouldContain("Lietuva 2.0 · uždaras bandymas");
        }
    }

    [Theory]
    [InlineData("/taisykles", "Naudojimo taisyklės", "id=\"kaip-iseiti\"", Lt2.Web.Components.Pages.LegalVersions.Terms)]
    [InlineData("/privatumas", "Privatumo pranešimas", "id=\"slapukai\"", Lt2.Web.Components.Pages.LegalVersions.Privacy)]
    public async Task The_terms_and_the_privacy_notice_are_static_pages_with_a_version(string path, string title, string part, string version)
    {
        using var visitor = NewBrowser();
        using var response = await visitor.Client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        html.ShouldContain($"<title>{title} · Lietuva 2.0</title>");
        html.ShouldContain($">{title}</h1>");
        html.ShouldContain($"Versija {version}, galioja nuo {version}.");
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