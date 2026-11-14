using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.11.7, 8.12 against the running stack: the agenda reads without JavaScript on a phone, a member finds a bill
/// open at once and follows it, and the share link lands a new member on the bill while a link off the site is ignored.
/// </summary>
public sealed partial class ReasonsToComeTests
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
    [Fact]
    public async Task The_share_link_lands_a_new_member_on_the_bill_and_a_link_off_the_site_is_ignored()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        (await RegisterThroughAsync("%2Fseimas%2FXVP-0412")).ShouldEndWith("/seimas/XVP-0412");
        (await RegisterThroughAsync("https%3A%2F%2Fpiktas.example%2Fseimas%2FXVP-0412")).ShouldEndWith("/sveiki");
    }

    /// <summary>Registers a new member from an invitation link with <c>tikslas</c>; returns where they land.</summary>
    private static async Task<string> RegisterThroughAsync(string landing)
    {
        await using var newcomer = await PasskeyBrowser.StartAsync("native");
        var page = newcomer.Page;
        var email = $"naujas.narys.{Guid.NewGuid():N}@pavyzdys.test";
        await page.GotoAsync($"{Base}/prisijungti?kodas={Uri.EscapeDataString(SignUpTests.Invitation)}&tikslas={landing}");
        await SignUpTests.WaitForInteractivityAsync(page);
        await page.GetByLabel("El. paštas").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Registruotis", Exact = true }).ClickAsync();
        await page.GetByLabel("Šešių skaitmenų kodas").FillAsync(await SignUpTests.CodeFromMailpitAsync(email));
        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dar du dalykai" })).ToBeVisibleAsync();
        await SignUpTests.WaitForInteractivityAsync(page);
        await page.GetByLabel("Vardas, kurį matys kiti").FillAsync("Pakviesta Narė");
        await page.GetByLabel("Man yra 18 metų arba daugiau").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Baigti registraciją" }).ClickAsync();
        await page.WaitForURLAsync(Landed());
        return new Uri(page.Url).AbsolutePath;
    }

    [GeneratedRegex("(/sveiki|/seimas/XVP-0412)$")]
    private static partial Regex Landed();
}
