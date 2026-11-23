using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.18 against the running stack with the invented parliament: the history of votes is reached from the start
/// page, the tab row of the agenda page and a vote's page; a row's title leads to its vote and its number to its bill; "Visi balsavimai" adds the other kinds;
/// the search and "Iki" are a plain GET form that works with JavaScript off; nothing scrolls sideways at 390 px. Paging and
/// the 300 cap need more votes than the stand-in has: the integration tests seed their own.
/// </summary>
public sealed partial class VoteHistoryTests : E2ETest
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Fact]
    public async Task The_start_page_the_agenda_and_a_vote_lead_to_the_history_and_a_row_leads_to_its_vote_and_bill()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT" });
        var page = await context.NewPageAsync();
        var history = page.Locator("#tab-seimas-balsavimai[aria-current=page]");

        await page.GotoAsync(Base + "/");
        await page.Locator("#home-votes-older").ClickAsync();
        await page.WaitForURLAsync(Base + "/seimas/balsavimai", new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(history).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });

        await page.GotoAsync(Base + "/darbotvarke");
        await page.Locator("#tab-seimas-balsavimai").ClickAsync();
        await page.WaitForURLAsync(Base + "/seimas/balsavimai", new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(history).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });

        // The newest stage result: its vote, back up to the history, then its bill.
        var first = page.Locator("#balsavimas-1");
        var vote = await first.Locator("a.tl-item__title").GetAttributeAsync("href");
        var bill = await first.Locator("a.tag").First.GetAttributeAsync("href");
        vote.ShouldNotBeNull().ShouldMatch("^/seimas/balsavimai/\\d+$");
        bill.ShouldNotBeNull().ShouldMatch("^/seimas/[XVI]+P-\\d+$");
        var title = (await first.Locator("a.tl-item__title").InnerTextAsync()).Trim();
        (await first.Locator("a.cta").CountAsync()).ShouldBe(0, "no button: the title leads to the vote");

        await first.Locator("a.tl-item__title").ClickAsync();
        await page.WaitForURLAsync(Base + vote, new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(title, new() { Timeout = E2ESteps.Slow });
        await page.Locator("#to-vote-history").ClickAsync();
        await Assertions.Expect(history).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });

        await page.Locator("#balsavimas-1 a.tag").First.ClickAsync();
        await page.WaitForURLAsync(Base + bill, new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(title, new() { Timeout = E2ESteps.Slow });
    }

    [Fact]
    public async Task Without_javascript_every_vote_the_search_and_iki_work_and_nothing_scrolls_sideways_at_390_px()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        var form = page.Locator("#votes-search");
        var rows = page.Locator("#balsavimai li.tl-item");

        await page.GotoAsync(Base + "/seimas/balsavimai");
        await E2ESteps.NoSidewaysScrollAsync(page);
        // The first page of the stage results, out of all of them (the local stand-in has a few hundred).
        var stageResults = await rows.CountAsync();
        stageResults.ShouldBeGreaterThan(0);
        await Assertions.Expect(page.Locator("#votes-shown")).ToHaveTextAsync(new Regex($"^Rodoma {stageResults} iš \\d+$"));
        var stageTotal = await ShownTotalAsync(page);
        (await rows.Locator(".tag:not(.tag--ink)").AllInnerTextsAsync()).ShouldNotContain("Dėl pasiūlymo", "the default is the stage results");

        // "Visi balsavimai": the amendment and the procedural votes join, with their kind.
        await form.GetByLabel("Kurie balsavimai").SelectOptionAsync(new SelectOptionValue { Label = "Visi balsavimai" });
        await form.GetByRole(AriaRole.Button, new() { Name = "Rodyti" }).ClickAsync();
        await page.WaitForURLAsync("**/seimas/balsavimai?*kas=visi*");
        (await ShownTotalAsync(page)).ShouldBeGreaterThan(stageTotal);
        await E2ESteps.NoSidewaysScrollAsync(page);

        // Their kinds, on a first page that reaches the spring session (the newest page may hold stage results only).
        await form.GetByLabel("Iki").FillAsync("2026-05-19");
        await form.GetByRole(AriaRole.Button, new() { Name = "Rodyti" }).ClickAsync();
        await page.WaitForURLAsync("**/seimas/balsavimai?*iki=2026-05-19*");
        var kinds = await rows.Locator(".tag:not(.tag--ink)").AllInnerTextsAsync();
        kinds.ShouldContain("Dėl pasiūlymo");
        kinds.ShouldContain("Procedūrinis balsavimas");
        await form.GetByLabel("Iki").FillAsync(string.Empty);

        // The search without diacritics, back on the stage results.
        await form.GetByLabel("Kurie balsavimai").SelectOptionAsync(new SelectOptionValue { Label = "Projektų etapų balsavimai" });
        await form.GetByLabel("Projekto numeris arba pavadinimas").FillAsync("gyventoju pajamu mokescio istatymo pataisa");
        await form.GetByRole(AriaRole.Button, new() { Name = "Rodyti" }).ClickAsync();
        await page.WaitForURLAsync("**/seimas/balsavimai?q=gyventoju+pajamu+mokescio+istatymo+pataisa*");
        (await rows.CountAsync()).ShouldBeGreaterThan(0);
        (await rows.Locator("a.tag--ink").AllInnerTextsAsync()).ShouldAllBe(n => n == "XVP-0412");

        // "Iki": only that day's and earlier votes.
        await form.GetByLabel("Projekto numeris arba pavadinimas").FillAsync(string.Empty);
        await form.GetByLabel("Iki").FillAsync("2026-04-30");
        await form.GetByRole(AriaRole.Button, new() { Name = "Rodyti" }).ClickAsync();
        await page.WaitForURLAsync("**/seimas/balsavimai?*iki=2026-04-30*");
        await Assertions.Expect(form.GetByLabel("Iki")).ToHaveValueAsync("2026-04-30");
        var days = await page.Locator("#balsavimai section.tl-day").EvaluateAllAsync<string[]>("s => s.map(e => e.id)");
        days.ShouldNotBeEmpty();
        days.ShouldAllBe(d => string.CompareOrdinal(d, "diena-2026-04-30") <= 0);
        await E2ESteps.NoSidewaysScrollAsync(page);

        // Clearing goes back to the plain list.
        await page.Locator("#votes-clear").ClickAsync();
        await page.WaitForURLAsync(new Regex("/seimas/balsavimai$"));
        (await rows.CountAsync()).ShouldBe(stageResults);
    }

    /// <summary>The N of "Rodoma n iš N": how many votes the filter found in all.</summary>
    private static async Task<int> ShownTotalAsync(IPage page)
    {
        var text = await page.Locator("#votes-shown").InnerTextAsync();
        return int.Parse(Regex.Match(text, @"iš (\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
