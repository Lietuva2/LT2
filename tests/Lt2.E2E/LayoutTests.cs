using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.12.5 and 8.12.6 against the stack: the signed-in menu is on one line beside the brand at 1280 px (it wrapped
/// once "Darbotvarkė" was added), and the bill pages, the list, the agenda and the front page have no horizontal scroll
/// at 390 px, for a visitor, a member and a moderator. Full-page screenshots of each go to <c>LT2_E2E_SHOTS</c> (or
/// <c>output/one-object/shots</c>) for a person to look at.
/// </summary>
public sealed class LayoutTests(LayoutTests.ModeratorFixture moderator) : IClassFixture<LayoutTests.ModeratorFixture>
{
    private static readonly string[] Pages =
        ["/seimas/XVP-0412", "/seimas/XVP-0533", "/projektai", "/darbotvarke", "/", "/organizacijos", "/o/pavyzdine-mokesciu-aiskumo-asociacija"];

    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    private static string Shots => Environment.GetEnvironmentVariable("LT2_E2E_SHOTS") is { Length: > 0 } dir ? dir : Path.Combine("output", "one-object", "shots");

    [Fact]
    public async Task For_a_visitor_no_page_scrolls_sideways_at_390_px()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT" });
        var page = await context.NewPageAsync();
        await ShootAllAsync(page, "visitor", checkMenu: false);
    }

    [Fact]
    public async Task For_a_member_the_menu_is_on_one_line_at_1280_px_and_no_page_scrolls_sideways_at_390_px()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 800);
        await PasskeyTests.RegisterAsync(member.Page, "Rodoma Narė");
        await ShootAllAsync(member.Page, "member", checkMenu: true);
        member.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task For_a_moderator_the_menu_is_on_one_line_at_1280_px_and_no_page_scrolls_sideways_at_390_px()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var page = await moderator.PageAsync();
        await ShootAllAsync(page, "moderator", checkMenu: true);
        await page.SetViewportSizeAsync(1280, 900);

        // Design 8.21: the account item of a staff passkey session offers the staff screens it has, read in the browser.
        await page.GotoAsync($"{Base}/darbotvarke");
        await page.Locator("#account-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("[data-account-link=moderation]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-account-link=settings]")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("[data-account-link=invitations]")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("[data-account-name]")).ToHaveTextAsync("Moderatorius L.");
        Directory.CreateDirectory(Shots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, "account-moderator-1280.png") });
        await page.Keyboard.PressAsync("Escape");
        await page.GotoAsync($"{Base}/seimas/XVP-0533");
        await Assertions.Expect(page.Locator("#moderation-panel")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    private static async Task ShootAllAsync(IPage page, string who, bool checkMenu)
    {
        Directory.CreateDirectory(Shots);
        foreach (var width in new[] { 1280, 390 })
        {
            await page.SetViewportSizeAsync(width, width == 1280 ? 900 : 844);
            foreach (var path in Pages)
            {
                await page.GotoAsync(Base + path);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.WaitForTimeoutAsync(500);
                (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"))
                    .ShouldBeLessThanOrEqualTo(0, $"{who} {path} at {width} px: no horizontal scrolling");
                if (checkMenu && width == 1280)
                {
                    var wrapped = await page.EvaluateAsync<string?>(
                        """
                        () => {
                          const brand = document.querySelector('.top__brand').getBoundingClientRect();
                          const items = [...document.querySelectorAll('.top__nav > *')].filter(e => e.offsetParent !== null || e.getClientRects().length > 0)
                            .flatMap(e => [...e.getClientRects()]).filter(r => r.width > 0);
                          const middles = items.map(r => Math.round(r.top + r.height / 2));
                          if (Math.max(...middles) - Math.min(...middles) > 6) return 'the menu items are on more than one line: ' + middles.join(',');
                          const nav = document.querySelector('.top__nav').getBoundingClientRect();
                          if (nav.top >= brand.bottom) return 'the menu is below the brand';
                          return null;
                        }
                        """);
                    wrapped.ShouldBeNull($"{who} {path}");
                }

                var name = path == "/" ? "front" : path.Trim('/').Replace('/', '-');
                await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, $"{name}-{who}-{width}.png"), FullPage = true });
            }
        }
    }

    /// <summary>A moderator for the class.</summary>
    public sealed class ModeratorFixture() : StaffFixture("moderator", "Moderatorius L.");
}
