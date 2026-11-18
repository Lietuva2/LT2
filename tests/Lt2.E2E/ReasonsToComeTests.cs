using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.11.7, 8.12 against the running stack: the agenda reads without JavaScript on a phone, a member finds a bill
/// open at once and follows it, and the share link lands a new member on the bill while a link off the site is ignored.
/// </summary>
public sealed partial class ReasonsToComeTests : E2ETest
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Fact]
    public async Task A_visitor_reads_the_agenda_without_javascript_on_a_phone()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        foreach (var path in new[] { "/darbotvarke", "/", "/seimas/XVP-0533" })
        {
            await page.GotoAsync(Base + path);
            (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"))
                .ShouldBeLessThanOrEqualTo(0, $"{path}: no horizontal scrolling at 390 px");
        }

        await page.GotoAsync(Base + "/darbotvarke");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Seimo darbotvarkė", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#agenda-coming")).ToContainTextAsync("Planuojama");
        await Assertions.Expect(page.Locator("#not-a-poll")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Owner, 2026-11-18: with a real-sized parliament the lists are cut, and every way to the rest works without script: a
    /// day of the agenda opens its other rows in place, <c>/projektai</c> grows by the address landing on the first bill
    /// added, and the bill's "Visi argumentai" is a link to the page with every argument.
    /// </summary>
    [Fact]
    public async Task Without_javascript_a_long_day_opens_in_place_and_the_lists_grow_by_the_address()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        // The stand-in's sitting days hold about forty bills each: ten show, "Rodyti visus" opens the rest in place.
        await page.GotoAsync(Base + "/darbotvarke");
        var day = page.Locator("#agenda-coming section.tl-day:has(details.tl-more)").First;
        await Assertions.Expect(day).ToHaveCountAsync(1, new() { Timeout = E2ESteps.Slow });
        var more = day.Locator("details.tl-more");
        var visible = day.Locator("li.tl-item:visible");
        (await visible.CountAsync()).ShouldBe(10, "ten rows of a long day");
        await more.Locator("summary").ClickAsync();
        (await visible.CountAsync()).ShouldBeGreaterThan(10, "the rest opens in place");
        await E2ESteps.NoSidewaysScrollAsync(page, "/darbotvarke opened at 390");

        // "/projektai": twenty, then "Rodyti daugiau" by the address, landing on the first bill added.
        await page.GotoAsync(Base + "/projektai");
        var rows = page.Locator("#appeals > li");
        await Assertions.Expect(rows).ToHaveCountAsync(20, new() { Timeout = E2ESteps.Slow });
        var added = (await page.Locator("#more").GetAttributeAsync("href"))!;
        added.ShouldStartWith("/projektai?rodyti=40#bill-");
        await page.Locator("#more").ClickAsync();
        await page.WaitForURLAsync(new Regex("rodyti=40#bill-"), new() { Timeout = E2ESteps.Slow });
        (await rows.CountAsync()).ShouldBeGreaterThan(20);
        await Assertions.Expect(page.Locator("#" + added.Split('#')[1])).ToBeInViewportAsync();

        // The bill's arguments: "Visi argumentai" leads to the page with every argument when a side has more than two.
        await page.GotoAsync(Base + "/seimas/XVP-0412");
        var all = page.Locator("a#reasons-all");
        if (await all.CountAsync() > 0)
        {
            var shown = await page.Locator("#reasons .said").CountAsync();
            await all.ClickAsync();
            await page.WaitForURLAsync(new Regex("argumentai=visi"), new() { Timeout = E2ESteps.Slow });
            (await page.Locator("#reasons .said").CountAsync()).ShouldBeGreaterThan(shown);
            await Assertions.Expect(page.Locator("a#reasons-all")).ToContainTextAsync("Rodyti mažiau");
        }
    }

    [Fact]
    public async Task A_member_finds_every_bill_open_follows_it_and_nothing_offers_asking_for_an_appeal()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native");
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Sekanti Projektą");
        await page.GotoAsync(Base + "/seimas/XVP-0618");

        // Design 8.12.1: the side buttons are there at once, whoever took part before; nothing to ask for or propose.
        await Assertions.Expect(page.Locator("#side-for")).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(page.Locator("#side-against")).ToBeEnabledAsync();
        (await page.Locator("main").InnerTextAsync()).ShouldNotContain("Noriu kreipimosi");
        await Assertions.Expect(page.Locator("#request-ask")).ToHaveCountAsync(0);

        var follow = page.Locator("#follow-toggle-bill");
        await Assertions.Expect(follow).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await follow.ClickAsync();
        await Assertions.Expect(follow).ToHaveTextAsync("Nebesekti");
        await follow.ClickAsync();
        await Assertions.Expect(follow).ToHaveTextAsync("Sekti");
        member.ConsoleErrors.ShouldBeEmpty();
    }

    /// <summary>
    /// Design 6.8, 8.11.5: a share line's link is the bill's own page with the code after the "#" (the browser never sends that
    /// part); the page takes the code into the tab and out of the address bar, and a sign-in link on the page opens the
    /// registration with it filled in and comes back to the bill. A sign-in link's page to come back to off the site is ignored.
    /// </summary>
    [Fact]
    public async Task The_share_link_lands_a_new_member_on_the_bill_and_a_link_off_the_site_is_ignored()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var code = Uri.EscapeDataString(SignUpTests.Invitation);
        (await RegisterThroughAsync($"/seimas/XVP-0412#kodas={code}", fromBill: true)).ShouldEndWith("/seimas/XVP-0412");
        (await RegisterThroughAsync($"/prisijungti#kodas={code}&tikslas=%2Fseimas%2FXVP-0412")).ShouldEndWith("/seimas/XVP-0412");
        (await RegisterThroughAsync($"/prisijungti#kodas={code}&tikslas=https%3A%2F%2Fpiktas.example%2Fseimas%2FXVP-0412")).ShouldBe("/paskyra");
    }

    /// <summary>
    /// Design 6.9 (owner, 2026-11-15): the sign-in links on a bill's page bring the person back to the bill, whether they
    /// register from there or sign in with an account they already have.
    /// </summary>
    [Fact]
    public async Task Registering_or_signing_in_from_a_bills_page_comes_back_to_the_bill()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        const string Bill = "/seimas/XVP-0618";
        var email = E2ESteps.NewEmail("is.projekto");

        // A visitor takes a side: the link goes to the sign-in page with the bill after the "#"; they register there.
        await using (var visitor = await PasskeyBrowser.StartAsync("native"))
        {
            var page = visitor.Page;
            await page.GotoAsync(Base + Bill);
            var side = page.Locator("main a.btn--for");
            await Assertions.Expect(side).ToHaveAttributeAsync("href", "/prisijungti#tikslas=%2Fseimas%2FXVP-0618", new() { Timeout = E2ESteps.Slow });
            await side.ClickAsync();

            // The sign-in steps take the part after the "#" out of the address as soon as they have read it, so by now
            // the address may or may not still carry it.
            await page.WaitForURLAsync(new Regex("/prisijungti(#.*)?$"), new() { Timeout = E2ESteps.Slow });
            await SignUpTests.WaitForInteractivityAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Registruokitės su kvietimo kodu." }).ClickAsync();
            await E2ESteps.RegisterHereAsync(page, email, "Iš Projekto Puslapio", SignUpTests.Invitation);
            await page.WaitForURLAsync(new Regex(Bill + "$"), new() { Timeout = E2ESteps.Slow });
            visitor.ConsoleErrors.ShouldBeEmpty();
        }

        // The same person, signed out, follows the bill: they sign in with the emailed code and come back to it.
        await using var returning = await PasskeyBrowser.StartAsync("native");
        var again = returning.Page;
        await again.GotoAsync(Base + Bill);
        var follow = again.Locator("main").GetByRole(AriaRole.Link, new() { Name = "Sekti", Exact = true });
        await Assertions.Expect(follow).ToHaveAttributeAsync("href", "/prisijungti#tikslas=%2Fseimas%2FXVP-0618", new() { Timeout = E2ESteps.Slow });
        await follow.ClickAsync();
        await SignUpTests.WaitForInteractivityAsync(again);
        var registrationCode = await Mailpit.CodeAsync(email);
        await again.GetByLabel("El. paštas").FillAsync(email);
        await again.GetByRole(AriaRole.Button, new() { Name = "Atsiųsti kodą" }).ClickAsync();
        var code = registrationCode;
        for (var tries = 0; code == registrationCode && tries < 90; tries++)
        {
            await Task.Delay(500);
            code = await Mailpit.CodeAsync(email);
        }

        code.ShouldNotBe(registrationCode, "a new sign-in code arrives");
        await again.GetByLabel("Šešių skaitmenų kodas").FillAsync(code);
        await again.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();
        await again.WaitForURLAsync(new Regex(Bill + "$"), new() { Timeout = E2ESteps.Slow });
        returning.ConsoleErrors.ShouldBeEmpty();
    }

    /// <summary>Registers a new member from an invitation link (a path with its query or "#" part); returns where they land.</summary>
    private static async Task<string> RegisterThroughAsync(string link, bool fromBill = false)
    {
        await using var newcomer = await PasskeyBrowser.StartAsync("native");
        var page = newcomer.Page;
        await page.GotoAsync(Base + link);
        if (fromBill)
        {
            // The code left the address bar at once and waits in the tab; the visitor's side button signs in and comes back.
            await page.WaitForFunctionAsync("() => location.hash === ''", null, new() { Timeout = E2ESteps.Slow });
            (await page.EvaluateAsync<string?>("() => sessionStorage.getItem('lt2.kodas')")).ShouldBe(SignUpTests.Invitation);
            await page.Locator("a.btn--for").First.ClickAsync();
            await page.WaitForURLAsync(new Regex("/prisijungti"), new() { Timeout = E2ESteps.Slow });
        }

        await Assertions.Expect(page.GetByLabel("Kvietimo kodas")).ToHaveValueAsync(SignUpTests.Invitation, new() { Timeout = E2ESteps.Slow });
        await E2ESteps.RegisterHereAsync(page, E2ESteps.NewEmail("naujas.narys"), "Pakviesta Narė");
        await page.WaitForURLAsync(Landed(), new() { Timeout = E2ESteps.Slow });
        return new Uri(page.Url).AbsolutePath;
    }

    [GeneratedRegex("(/paskyra|/seimas/XVP-0412)$")]
    private static partial Regex Landed();
}
