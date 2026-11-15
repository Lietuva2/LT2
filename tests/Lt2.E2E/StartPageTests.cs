using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.7, the reworked front page, against the running stack ("Done when"): a visitor sees the introduction and the
/// agenda and no form; a member sees their own bills first and no introduction; with JavaScript off the public page is
/// complete and nothing of a member's own is in it; no sideways scroll at 390 px; at 1280 px the first screen shows the
/// agenda's heading. Screenshots for the owner go to LT2_E2E_START_SHOTS (default output/start-page/built).
/// </summary>
public sealed class StartPageTests
{
    private static string Base => E2ESteps.Base;

    private static string Shots => Environment.GetEnvironmentVariable("LT2_E2E_START_SHOTS") is { Length: > 0 } dir ? dir : Path.Combine("output", "start-page", "built");

    [Theory]
    [InlineData(1280, 900)]
    [InlineData(390, 844)]
    public async Task A_visitor_sees_the_introduction_and_the_agenda_and_no_form(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Base + "/");

        await Assertions.Expect(page.Locator("#home-head")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator(".intro-steps > li")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator("#home-register")).ToHaveAttributeAsync("href", "/prisijungti?registruotis=true");
        await Assertions.Expect(page.Locator("main form")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#my-agenda")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#h-home-coming")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#not-a-poll")).ToBeVisibleAsync();
        await E2ESteps.NoSidewaysScrollAsync(page, $"/ at {width}");
        if (width == 1280)
        {
            var heading = await page.Locator("#h-home-coming").BoundingBoxAsync();
            (heading!.Y + heading.Height).ShouldBeLessThanOrEqualTo(height, "the agenda's heading is on the first screen");
        }

        (await page.EvaluateAsync<bool>("() => document.querySelector('.home-how').compareDocumentPosition(document.querySelector('#home-coming')) & Node.DOCUMENT_POSITION_FOLLOWING"))
            .ShouldBeTrue("the steps come before the agenda, on a phone too");
        Directory.CreateDirectory(Shots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, $"visitor-{width}.png"), FullPage = true });
    }

    [Fact]
    public async Task A_member_sees_their_own_bills_first_marked_in_the_rows_and_no_introduction()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Pradžios Narė");

        // The first planned bill with a page: the member follows it and asks for "už" on it.
        await page.GotoAsync(Base + "/");
        var first = page.Locator("#home-coming .tl-item a.tl-item__title").First;
        await Assertions.Expect(first).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        var number = (await first.GetAttributeAsync("href"))!["/seimas/".Length..];
        var rowId = await first.Locator("xpath=ancestor::li[contains(@class, 'tl-item')][1]").GetAttributeAsync("id");
        var date = rowId!["agenda-".Length..("agenda-".Length + 10)];
        (await E2ESteps.ApiAsync(page, "PUT", "/api/v1/me/consents/positions", new { textVersion = "2026-11-13" })).Status.ShouldBeOneOf(204, 200);
        (await E2ESteps.ApiAsync(page, "PUT", $"/api/v1/bills/{number}/record/position", new { side = "for" })).Status.ShouldBe(204);
        (await E2ESteps.ApiAsync(page, "PUT", $"/api/v1/bills/{number}/follow")).Status.ShouldBe(204);
        try
        {
            foreach (var (width, height) in new[] { (1280, 900), (390, 844) })
            {
                await page.SetViewportSizeAsync(width, height);
                await page.GotoAsync(Base + "/");
                await Assertions.Expect(page.Locator("#home-title")).ToHaveTextAsync("Sveiki sugrįžę");
                await Assertions.Expect(page.Locator("#home-head")).ToHaveCountAsync(0);
                await Assertions.Expect(page.Locator(".intro-steps")).ToHaveCountAsync(0);
                await Assertions.Expect(page.Locator("#home-how-link")).ToHaveAttributeAsync("href", "/metodika#kaip-tai-veikia");

                var mine = page.Locator($"#my-agenda-{number}");
                await Assertions.Expect(mine).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
                await Assertions.Expect(mine.Locator(".choice--for")).ToHaveTextAsync("už");
                await Assertions.Expect(mine.Locator($"#my-agenda-share-{number}")).ToBeVisibleAsync();
                var row = page.Locator($"#agenda-{date}-{number}");
                await Assertions.Expect(row.Locator(".mine")).ToContainTextAsync("Sekate", new() { Timeout = E2ESteps.Slow });
                await Assertions.Expect(row.Locator($"#home-action-{date}-{number}")).ToHaveTextAsync("Argumentai ir pozicijos");

                // The page's agenda may be a minute old and still say nobody took a side: never beside the member's own side.
                await Assertions.Expect(row.Locator($"#home-nobody-{date}-{number}")).ToHaveCountAsync(0);
                (await page.EvaluateAsync<bool>("() => document.querySelector('#my-agenda').compareDocumentPosition(document.querySelector('#home-coming')) & Node.DOCUMENT_POSITION_FOLLOWING"))
                    .ShouldBeTrue("the member's own bills come first");
                await E2ESteps.NoSidewaysScrollAsync(page, $"/ for a member at {width}");
                if (width == 1280)
                {
                    var heading = await page.Locator("#h-home-coming").BoundingBoxAsync();
                    (heading!.Y + heading.Height).ShouldBeLessThanOrEqualTo(height, "the agenda's heading is on the first screen");
                }

                Directory.CreateDirectory(Shots);
                await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, $"member-{width}.png"), FullPage = true });
            }

            // The dark theme, once, as the owner looks at it.
            await page.SetViewportSizeAsync(1280, 900);
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
            await page.GotoAsync(Base + "/");
            await Assertions.Expect(page.Locator($"#my-agenda-{number}")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, "member-1280-dark.png"), FullPage = true });
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });

            // With JavaScript off the member's page is the public page: nothing of their own is in it.
            await using var noScript = await page.Context.Browser!.NewContextAsync(new()
            {
                IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false, StorageState = await page.Context.StorageStateAsync(),
            });
            var plain = await noScript.NewPageAsync();
            await plain.GotoAsync(Base + "/");
            await Assertions.Expect(plain.Locator("#home-title")).ToHaveTextAsync("Sveiki sugrįžę");
            await Assertions.Expect(plain.Locator("#home-coming .tl-item").First).ToBeVisibleAsync();
            await Assertions.Expect(plain.Locator("#my-agenda")).ToHaveCountAsync(0);
            await Assertions.Expect(plain.Locator(".mine")).ToHaveCountAsync(0);
            (await plain.Locator("main").InnerTextAsync()).ShouldNotContain("Jūs prašote balsuoti");
            await Assertions.Expect(plain.Locator($"#home-action-{date}-{number}")).ToHaveTextAsync("Nurodyti, ko prašote");
        }
        finally
        {
            // The demonstration bill's numbers are what other tests count on: the side and the follow go again.
            await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{number}/record/position");
            await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{number}/follow");
        }
    }

    [Fact]
    public async Task With_javascript_off_the_public_page_is_complete()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Base + "/");

        await Assertions.Expect(page.Locator("#home-head")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".intro-steps > li")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator("#home-coming .tl-item").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#home-coming .tl-item a.btn").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#home-organisations")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#not-a-poll")).ToBeVisibleAsync();
    }
}
