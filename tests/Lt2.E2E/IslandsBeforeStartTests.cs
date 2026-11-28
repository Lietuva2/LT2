using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Before a WebAssembly island has started, nothing in it can be used: a field typed into would be emptied when the island
/// takes over, and a form submitted natively would reload the page and lose what was typed. Every page with islands is
/// loaded here with the WebAssembly runtime blocked (so the islands never start), as a visitor, a member, a moderator and
/// an administrator; inside every island (found by Blazor's prerender markers) each input, text area, choice and button
/// must be disabled, and the page must carry the "needs JavaScript" note. A new form or page that forgets
/// <c>disabled="@Locked"</c> fails here.
/// </summary>
public sealed partial class IslandsBeforeStartTests(AppealFlowTests.ModeratorFixture moderator, AdministratorFixture administrator)
    : E2ETest, IClassFixture<AppealFlowTests.ModeratorFixture>, IClassFixture<AdministratorFixture>
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    /// <summary>The .NET runtime and assemblies; blazor.web.js itself still loads, so the markers are read as usual.</summary>
    [GeneratedRegex(@"/_framework/(dotnet\.[^/]*|[^/]+\.(wasm|dat))(\?.*)?$")]
    internal static partial Regex WebAssemblyFiles();

    private sealed class IslandReport
    {
        public int Islands { get; set; }
        public string[] Enabled { get; set; } = [];
        public int NeedsScriptNotes { get; set; }
    }

    /// <summary>
    /// Opens <paramref name="path"/> in a new page of <paramref name="context"/> with the WebAssembly runtime blocked and
    /// asserts that every control inside every island is disabled, that there is at least one island, and that the page
    /// says JavaScript is needed.
    /// </summary>
    internal static async Task NothingUsableBeforeStartAsync(IBrowserContext context, string path)
    {
        var page = await context.NewPageAsync();
        try
        {
            // Blazor's start script removes its markers as soon as it runs, so the islands are noted while the page is
            // parsed: the parser inserts nodes in document order, and every element between an island's start and end
            // markers is tagged with the island's id.
            await page.AddInitScriptAsync(
                """
                (() => {
                  const open = [];
                  window.__lt2Islands = new Set();
                  const tag = (el, id) => { el.setAttribute('data-lt2-island', id); for (const d of el.querySelectorAll('*')) d.setAttribute('data-lt2-island', id); };
                  new MutationObserver(records => {
                    for (const record of records) for (const node of record.addedNodes) {
                      if (node.nodeType === Node.COMMENT_NODE) {
                        const text = node.data.trim();
                        if (!text.startsWith('Blazor:')) continue;
                        let info;
                        try { info = JSON.parse(text.slice('Blazor:'.length)); } catch { continue; }
                        if (info.type === 'webassembly' && info.prerenderId) { open.push(info.prerenderId); window.__lt2Islands.add(info.prerenderId); }
                        else if (!info.type && info.prerenderId && open[open.length - 1] === info.prerenderId) open.pop();
                      } else if (node.nodeType === Node.ELEMENT_NODE && open.length) {
                        tag(node, open[open.length - 1]);
                      }
                    }
                  }).observe(document, { childList: true, subtree: true });
                })();
                """);
            await page.RouteAsync(WebAssemblyFiles(), route => route.AbortAsync());
            await page.GotoAsync(Base + path);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var report = await page.EvaluateAsync<IslandReport>(
                """
                () => {
                  const enabled = [];
                  for (const el of document.querySelectorAll('[data-lt2-island]:is(input, textarea, select, button)')) {
                    if (el.type === 'hidden' || el.matches(':disabled')) continue;
                    // A button that only shows a popover (the action row's "⋯", design 8.16.1; a hint's trigger, design 8.15, as
                    // the one beside "Sekti" since 2026-11-17; a moderator's "Kiti veiksmai", design 8.3) works without script, as a
                    // <details> summary does; the menu's own items must be disabled like any other control.
                    if (el.matches('button:is(.act__more, .hint__trigger, .menu__trigger)[popovertarget]:not([popovertargetaction])')) continue;
                    enabled.push(el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + ' "' + (el.textContent || el.getAttribute('aria-label') || el.name || '').trim().slice(0, 40) + '"');
                  }
                  const notes = [...document.querySelectorAll('noscript')].filter(n => n.textContent.includes('JavaScript')).length;
                  return { islands: window.__lt2Islands.size, enabled: [...new Set(enabled)], needsScriptNotes: notes };
                }
                """);
            report.Islands.ShouldBeGreaterThan(0, $"{path}: the islands are found (the check is not empty)");
            report.Enabled.ShouldBeEmpty($"{path}: usable before WebAssembly has started");
            report.NeedsScriptNotes.ShouldBeGreaterThan(0, $"{path}: says that JavaScript is needed");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task For_a_visitor_public_pages_load_no_WebAssembly_and_nothing_in_an_island_can_be_used_before_it_has_started()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT" });
        // Owner, 2026-11-28: public pages serve a visitor their islands as plain HTML (VisitorIslandTests), so only the pages
        // that are islands themselves are checked for a visitor; the public pages are checked for a member.
        foreach (var path in new[] { "/prisijungti", "/paskyra/el-pastas/grazinti#nebandomas" })
        {
            await NothingUsableBeforeStartAsync(context, path);
        }

        // ... and a visitor's browser never asks for the WebAssembly runtime on a public page.
        var page = await context.NewPageAsync();
        var requested = new List<string>();
        page.Request += (_, request) => { if (WebAssemblyFiles().IsMatch(request.Url)) requested.Add(request.Url); };
        foreach (var path in new[] { "/", "/projektai", "/darbotvarke", "/seimas/XVP-0412", "/seimas/XVP-0533", "/p/jonas-petraitis", "/o/pavyzdine-mokesciu-aiskumo-asociacija", "/statistika" })
        {
            await page.GotoAsync(Base + path);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            requested.ShouldBeEmpty($"{path}: a visitor's browser loads no WebAssembly");
        }

        await page.CloseAsync();
    }

    [Fact]
    public async Task For_a_member_nothing_in_an_island_can_be_used_before_it_has_started()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 800);
        await PasskeyTests.RegisterAsync(member.Page, "Laukianti Pradžios");
        foreach (var path in new[]
                 {
                     "/", "/darbotvarke", "/paskyra", "/paskyra/sekimas", "/paskyra/dalyvavimas", "/paskyra/saugumas", "/seimas/XVP-0412", "/seimas/XVP-0533", "/p/jonas-petraitis", "/organizacijos/nauja", "/o/pavyzdine-mokesciu-aiskumo-asociacija",
                     "/seimo-narys/perimti#nebandomas", "/seimo-narys/biuras#nebandomas", "/seimo-narys/projektai",
                     "/prisijungti/raktas#grizti=%2Fpaskyra", "/statistika",
                 })
        {
            await NothingUsableBeforeStartAsync(member.Page.Context, path);
        }

        // Design 8.25: a member's public page ("Sekti", "Pranešti"), here the member's own.
        var (status, body) = await E2ESteps.ApiAsync(member.Page, "PUT", "/api/v1/me/profile", new { displayName = "Laukianti Pradžios", about = "Bandymas (E2E bandymas).", @public = true, publicConsentVersion = "2026-11-16" });
        status.ShouldBe(200, body);
        var id = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("address").GetString()!;
        await NothingUsableBeforeStartAsync(member.Page.Context, id);
    }

    [Fact]
    public async Task For_a_representative_and_an_invited_member_nothing_in_an_island_can_be_used_before_it_has_started()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var representative = await PasskeyBrowser.StartAsync("native", 1280, 800);
        await PasskeyTests.RegisterAsync(representative.Page, "Laukianti Atstovė");
        var r = representative.Page;
        var (created, createdBody) = await E2ESteps.ApiAsync(r, "POST", "/api/v1/organisations",
            new { name = $"Salų bandymo draugija {DateTime.UtcNow:HHmmssfff} {OrganisationFlowTests.Marker}", description = "Išgalvota organizacija (E2E bandymas)." });
        created.ShouldBe(201, createdBody);
        var slug = JsonDocument.Parse(createdBody).RootElement.GetProperty("slug").GetString()!;
        try
        {
            var (made, inviteBody) = await E2ESteps.ApiAsync(r, "POST", $"/api/v1/organisations/{slug}/invites", new { });
            made.ShouldBe(200, inviteBody);
            var invite = JsonDocument.Parse(inviteBody).RootElement.GetProperty("path").GetString()!;
            invite.ShouldStartWith("/organizacijos/kvietimas#", Case.Sensitive, "design 6.8: the token after the #");

            // The representative's own part of the profile: the managing island with its forms.
            await NothingUsableBeforeStartAsync(r.Context, $"/o/{slug}");

            // Another member opens the one-time link. Before WebAssembly starts the page shows only that JavaScript is
            // needed: the server never sees the token, so it cannot say whose invitation this is. Once the island runs,
            // it reads the token and offers "Tapti atstovu".
            await using var colleague = await PasskeyBrowser.StartAsync("native", 1280, 800);
            await PasskeyTests.RegisterAsync(colleague.Page, "Pakviesta Kolegė");
            await NothingUsableBeforeStartAsync(colleague.Page.Context, invite);
            await colleague.Page.GotoAsync(E2ESteps.Base + invite);
            await Assertions.Expect(colleague.Page.Locator("#invite-accept")).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            (await colleague.Page.ContentAsync()).ShouldNotContain("invite-missing");
        }
        finally
        {
            await E2ESteps.CloseOrganisationAsync(r, slug);
        }    }

    [Fact]
    public async Task For_staff_nothing_in_an_island_can_be_used_before_it_has_started()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var mod = await moderator.PageAsync();
        foreach (var path in new[] { "/moderavimas", "/moderavimas?skiltis=argumentai", "/moderavimas?skiltis=komentarai", "/moderavimas?skiltis=be-aprasymo", "/moderavimas?skiltis=paslepti", "/moderavimas?skiltis=apribojimai", "/moderavimas/atsiliepimai", "/moderavimas/suvestines", "/moderavimas/darbotvarke", "/seimas/XVP-0412", "/seimas/XVP-0533", "/seimas/XVP-0618" })
        {
            await NothingUsableBeforeStartAsync(mod.Context, path);
        }

        var admin = await administrator.PageAsync();
        foreach (var path in new[] { "/administravimas/nustatymai", "/administravimas/kvietimai", "/administravimas/pristatymas", "/administravimas/nesiunciama", "/administravimas/lankomumas", "/administravimas/paskyros", "/paskyra", "/paskyra/dalyvavimas" })
        {
            await NothingUsableBeforeStartAsync(admin.Context, path);
        }
    }
}
