using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.22 against the stack: a member whose accepted terms of use are older than the version in force sees the one screen
/// after signing in, reads, exports and is refused a write until pressing "Sutinku", then goes on; the terms and the privacy
/// notice as pages (screenshots at 1280 and 390 px to <c>output/terms</c>); and nothing kept in the browser outside the
/// inventory the privacy notice lists (8.22.1), for a visitor and for a member.
/// </summary>
public sealed class TermsTests : E2ETest
{
    private static string Shots => Path.Combine("output", "terms");

    [Fact]
    public async Task A_member_with_an_older_version_of_the_terms_sees_the_screen_after_signing_in_and_accepts()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 390, 844);
        var page = member.Page;
        var email = await PasskeyTests.RegisterAsync(page, "Senesnė Narė");
        await E2ESteps.SignOutOnAccountAsync(page);
        await page.WaitForURLAsync(new Regex("/$"), new() { Timeout = E2ESteps.Slow });

        // The account accepted an earlier version, as every account will after the terms change.
        (await E2ESteps.SqlAsync(
            $"update identity.consent set text_version = '2026-01-01' where kind = 'terms' and user_id = (select id from identity.\"user\" where email = '{email}') returning text_version"))
            .Trim().ShouldStartWith("2026-01-01");

        // Signing in from a bill: the screen first, and back to the bill afterwards.
        await page.GotoAsync(E2ESteps.Base + "/prisijungti#tikslas=/seimas/XVP-0412");
        await SignUpTests.WaitForInteractivityAsync(page);
        await page.GetByLabel("El. paštas").FillAsync(email);
        // The registration's code is still in Mailpit (and no longer works): wait for the new one.
        var codes = await Mailpit.CountAsync(Mailpit.CodeQuery(email));
        await page.GetByRole(AriaRole.Button, new() { Name = "Atsiųsti kodą" }).ClickAsync();
        await page.GetByLabel("Šešių skaitmenų kodas").FillAsync(await Mailpit.NewCodeAsync(email, codes));
        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/taisykles/sutikimas"), new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Naudojimo taisyklės");
        var accept = page.Locator("#terms-accept-button");
        await Assertions.Expect(accept).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#terms-sign-out")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#terms-accept-read")).ToHaveAttributeAsync("href", "/taisykles");
        Directory.CreateDirectory(Shots);
        await E2ESteps.ShootBothAsync(page, Shots, "acceptance");

        // Before accepting: GET /me says so, reading and the export work, a write is refused with terms.required.
        var (meStatus, meBody) = await E2ESteps.ApiAsync(page, "GET", "/api/v1/me");
        meStatus.ShouldBe(200);
        JsonDocument.Parse(meBody).RootElement.GetProperty("termsRequired").GetBoolean().ShouldBeTrue();
        (await E2ESteps.ApiAsync(page, "GET", "/api/v1/me/export")).Status.ShouldBe(200);
        (await E2ESteps.ApiAsync(page, "GET", "/api/v1/bills/XVP-0412/record")).Status.ShouldBe(200);
        var (refused, refusedBody) = await E2ESteps.ApiAsync(page, "PUT", "/api/v1/me/consents/positions", new { textVersion = "2026-11-18" });
        refused.ShouldBe(403);
        JsonDocument.Parse(refusedBody).RootElement.GetProperty("code").GetString().ShouldBe("terms.required");

        // The account page says so too, with the way to the screen, the export and deleting.
        await page.GotoAsync(E2ESteps.Base + "/paskyra/saugumas");
        await Assertions.Expect(page.Locator("#terms-required")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#export")).ToBeVisibleAsync();

        // The island checks before start for this screen (AGENTS.md: a new island page is walked with WebAssembly blocked).
        await IslandsBeforeStartTests.NothingUsableBeforeStartAsync(page.Context, "/taisykles/sutikimas?grizti=%2Fseimas%2FXVP-0412");

        // "Sutinku": accepted, and on to the bill.
        await page.GotoAsync(E2ESteps.Base + "/taisykles/sutikimas?grizti=%2Fseimas%2FXVP-0412");
        await Assertions.Expect(accept).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await accept.ClickAsync();
        await page.WaitForURLAsync(new Regex("/seimas/XVP-0412$"), new() { Timeout = E2ESteps.Slow });
        (await E2ESteps.SqlAsync(
            $"select string_agg(text_version, ',' order by text_version) from identity.consent where kind = 'terms' and user_id = (select id from identity.\"user\" where email = '{email}')"))
            .Trim().ShouldBe("2026-01-01,2026-11-17");
        var (me, after) = await E2ESteps.ApiAsync(page, "GET", "/api/v1/me");
        me.ShouldBe(200);
        JsonDocument.Parse(after).RootElement.GetProperty("termsRequired").GetBoolean().ShouldBeFalse();
        (await E2ESteps.ApiAsync(page, "PUT", "/api/v1/me/consents/positions", new { textVersion = "2026-11-18" })).Status.ShouldBe(204);

        // Signing in again goes straight on.
        await page.GotoAsync(E2ESteps.Base + "/paskyra");
        await Assertions.Expect(page.Locator("#terms-required")).ToHaveCountAsync(0);
        member.ConsoleErrors.Where(e => !e.Contains("403", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_terms_and_the_privacy_notice_are_pages_reached_from_the_footer_without_script()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false });
        var page = await context.NewPageAsync();

        await page.GotoAsync(E2ESteps.Base + "/seimas/XVP-0412");
        await page.Locator("footer").GetByRole(AriaRole.Link, new() { Name = "Naudojimo taisyklės" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/taisykles$"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Naudojimo taisyklės");
        await Assertions.Expect(page.Locator("#legal-version")).ToHaveTextAsync("Versija 2026-11-17, galioja nuo 2026-11-17.");
        await E2ESteps.ShootBothAsync(page, Shots, "terms");

        await page.Locator("footer").GetByRole(AriaRole.Link, new() { Name = "Privatumas" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/privatumas$"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Privatumo pranešimas");
        await Assertions.Expect(page.Locator("#storage-inventory tbody tr")).ToHaveCountAsync(8);
        await E2ESteps.ShootBothAsync(page, Shots, "privacy");
    }

    /// <summary>The inventory as the privacy notice lists it: each row's cookie or key and where it is kept.</summary>
    private static async Task<HashSet<(string Name, string Kind)>> InventoryAsync(IPage page)
    {
        var listed = await page.EvaluateAsync<string[][]>(
            """
            async () => {
              const html = await (await fetch('/privatumas')).text();
              const doc = new DOMParser().parseFromString(html, 'text/html');
              return [...doc.querySelectorAll('[data-storage]')].map(r => [r.dataset.storage, r.dataset.storageKind]);
            }
            """);
        listed.Length.ShouldBeGreaterThan(0, "the notice lists the inventory");
        return listed.Select(r => (r[0], r[1])).ToHashSet();
    }

    /// <summary>Every cookie of the site and every key of this page's local and session storage, outside the inventory.</summary>
    private static async Task<List<string>> OutsideAsync(IPage page, HashSet<(string Name, string Kind)> inventory, string where)
    {
        var outside = new List<string>();
        foreach (var cookie in await page.Context.CookiesAsync([E2ESteps.Base]))
        {
            if (!inventory.Contains((cookie.Name, "cookie")))
            {
                outside.Add($"{where}: cookie {cookie.Name}");
            }
        }

        var keys = await page.EvaluateAsync<string[][]>(
            "() => [...Object.keys(localStorage).map(k => ['local', k]), ...Object.keys(sessionStorage).map(k => ['session', k])]");

        // The browser's other stores, which the inventory has no place for: nothing may be in them.
        var others = await page.EvaluateAsync<string[]>(
            "async () => [...(await caches.keys()).map(k => 'cache ' + k), ...((await indexedDB.databases?.()) ?? []).map(d => 'indexedDB ' + d.name)]");
        outside.AddRange(others.Select(o => $"{where}: {o}"));
        foreach (var key in keys)
        {
            if (!inventory.Contains((key[1], key[0])))
            {
                outside.Add($"{where}: {key[0]} storage {key[1]}");
            }
        }

        return outside;
    }

    [Fact]
    public async Task Nothing_is_kept_in_the_browser_outside_the_inventory_the_privacy_notice_lists()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        string[] paths = ["/", "/seimas/XVP-0412", "/p/jonas-petraitis", "/prisijungti", "/darbotvarke"];
        var outside = new List<string>();
        var seen = new HashSet<string>();

        // A visitor, then choosing a theme.
        await using (var visitor = await PasskeyBrowser.StartAsync("native", 1280, 900))
        {
            var page = visitor.Page;
            await page.GotoAsync(E2ESteps.Base + "/");
            var inventory = await InventoryAsync(page);
            foreach (var path in paths)
            {
                await page.GotoAsync(E2ESteps.Base + path);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                outside.AddRange(await OutsideAsync(page, inventory, "visitor " + path));
            }

            await page.Locator("[data-theme-switch]").ClickAsync();
            await page.GotoAsync(E2ESteps.Base + "/seimas/XVP-0412");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            outside.AddRange(await OutsideAsync(page, inventory, "visitor after choosing a theme"));
            (await page.EvaluateAsync<string?>("() => localStorage.getItem('lt2-theme')")).ShouldBe("light", "the check sees what the site stores");
            foreach (var cookie in await page.Context.CookiesAsync([E2ESteps.Base]))
            {
                seen.Add(cookie.Name);
            }
        }

        // A member: registered (the sign-in form's session storage on the way), then the account page and the same pages.
        await using (var member = await PasskeyBrowser.StartAsync("native", 1280, 900))
        {
            var page = member.Page;
            await page.GotoAsync(E2ESteps.Base + "/");
            var inventory = await InventoryAsync(page);
            await page.GotoAsync(E2ESteps.Base + "/prisijungti?registruotis=true#tikslas=/seimas/XVP-0412");
            await SignUpTests.WaitForInteractivityAsync(page);
            outside.AddRange(await OutsideAsync(page, inventory, "member registering"));
            (await page.EvaluateAsync<string?>("() => sessionStorage.getItem('lt2.return-path')")).ShouldBe("/seimas/XVP-0412", "the check sees what the site stores");
            await E2ESteps.RegisterHereAsync(page, E2ESteps.NewEmail(), "Atminties Narė", SignUpTests.Invitation);
            await page.WaitForURLAsync(new Regex("/seimas/XVP-0412$"), new() { Timeout = E2ESteps.Slow });
            foreach (var path in paths.Concat(["/paskyra", "/paskyra/sekimas", "/paskyra/dalyvavimas", "/paskyra/saugumas"]))
            {
                await page.GotoAsync(E2ESteps.Base + path);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                outside.AddRange(await OutsideAsync(page, inventory, "member " + path));
            }

            await page.Locator("#account-toggle").ClickAsync();
            await Assertions.Expect(page.Locator("[data-account-name]")).ToHaveTextAsync("Atminties Narė");
            outside.AddRange(await OutsideAsync(page, inventory, "member with the account item open"));
            foreach (var cookie in await page.Context.CookiesAsync([E2ESteps.Base]))
            {
                seen.Add(cookie.Name);
            }
        }

        outside.ShouldBeEmpty();

        // The check is not empty: the cookies the inventory names were really there to be compared.
        seen.ShouldContain("__Host-lt2");
        seen.ShouldContain("__Host-lt2.af");
        seen.ShouldContain("__Host-lt2.device");
    }
}
