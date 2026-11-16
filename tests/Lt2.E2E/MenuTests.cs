using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.21 against the stack: the menu (logo, "Projektai", "Seimo nariai", "Prisijungti" or "Paskyra") on one line at 360,
/// 390 and 1280 px in both themes, for a visitor and a member; the four tabs of "Projektai" reaching each other; the account
/// item with the keyboard, its name and links filled in the browser only. Screenshots go to <c>output/menu</c>.
/// </summary>
public sealed class MenuTests
{
    private static readonly int[] Widths = [360, 390, 1280];

    private static string Shots => Path.Combine("output", "menu");

    /// <summary>Null when the brand and every visible item of the menu share one line; otherwise what is wrong.</summary>
    private static Task<string?> MenuProblemAsync(IPage page) =>
        page.EvaluateAsync<string?>(
            """
            () => {
              const brand = document.querySelector('.top__brand').getBoundingClientRect();
              const items = [...document.querySelectorAll('.top__nav > *')].map(e => e.matches('details') ? e.querySelector('summary') : e)
                .flatMap(e => [...e.getClientRects()]).filter(r => r.width > 0);
              if (items.length !== 3) return 'expected three menu items, found ' + items.length;
              const middles = [brand, ...items].map(r => Math.round(r.top + r.height / 2));
              if (Math.max(...middles) - Math.min(...middles) > 8) return 'not one line: ' + middles.join(',');
              const right = Math.max(...items.map(r => r.right));
              if (right > document.documentElement.clientWidth) return 'past the right edge: ' + right;
              return null;
            }
            """);

    private static async Task ShootMenuAsync(IPage page, string who)
    {
        Directory.CreateDirectory(Shots);
        foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await page.EmulateMediaAsync(new() { ColorScheme = scheme });
            foreach (var width in Widths)
            {
                await page.SetViewportSizeAsync(width, width == 1280 ? 900 : 780);
                foreach (var path in new[] { "/", "/seimas/XVP-0412" })
                {
                    await page.GotoAsync(E2ESteps.Base + path);
                    await Assertions.Expect(page.Locator("#nav-projects")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
                    (await MenuProblemAsync(page)).ShouldBeNull($"{who} {path} at {width} px");
                    await E2ESteps.NoSidewaysScrollAsync(page);
                }

                var name = $"{who}-{width}-{(scheme == ColorScheme.Dark ? "dark" : "light")}";
                await page.Locator("header").ScreenshotAsync(new() { Path = Path.Combine(Shots, $"header-{name}.png") });
                await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, $"bill-{name}.png"), FullPage = true });
            }
        }

        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await page.SetViewportSizeAsync(1280, 900);
    }

    [Fact]
    public async Task For_a_visitor_the_menu_is_one_line_at_360_390_and_1280_px_in_both_themes()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var visitor = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = visitor.Page;
        await ShootMenuAsync(page, "visitor");

        var nav = page.Locator("nav.top__nav");
        await Assertions.Expect(nav.GetByRole(AriaRole.Link)).ToHaveTextAsync(["Projektai", "Seimo nariai", "Prisijungti"]);
        await Assertions.Expect(page.Locator("#nav-projects")).ToHaveAttributeAsync("href", "/darbotvarke");
        await Assertions.Expect(page.Locator("#nav-projects")).ToHaveAttributeAsync("aria-current", "true", new() { Timeout = E2ESteps.Slow });
        visitor.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task For_a_member_the_menu_is_one_line_at_360_390_and_1280_px_in_both_themes()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await PasskeyTests.RegisterAsync(member.Page, "Meniu Narė");
        await ShootMenuAsync(member.Page, "member");
        await Assertions.Expect(member.Page.Locator("#account-toggle")).ToHaveTextAsync("Paskyra");
        member.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_of_the_four_tabs_reaches_the_other_three_and_marks_its_page()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var visitor = await PasskeyBrowser.StartAsync("native", 390, 844);
        var page = visitor.Page;
        (string Path, string Tab, string Id)[] tabs =
        [
            ("/darbotvarke", "Darbotvarkė", "tab-darbotvarke"),
            ("/projektai", "Visi projektai", "tab-projektai"),
            ("/seimas/balsavimai", "Balsavimai", "tab-seimas-balsavimai"),
            ("/suvestines", "Suvestinės", "tab-suvestines"),
        ];

        // From the menu item to the agenda, then from every tab to every other by its link.
        await page.GotoAsync(E2ESteps.Base + "/metodika");
        await page.Locator("#nav-projects").ClickAsync();
        await page.WaitForURLAsync(new Regex("/darbotvarke$"), new() { Timeout = E2ESteps.Slow });
        foreach (var from in tabs)
        {
            foreach (var to in tabs.Where(t => t != from))
            {
                await page.GotoAsync(E2ESteps.Base + from.Path);
                var row = page.Locator("#project-tabs");
                await Assertions.Expect(row.Locator("[aria-current=page]")).ToHaveTextAsync(from.Tab);
                await row.GetByRole(AriaRole.Link, new() { Name = to.Tab, Exact = true }).ClickAsync();
                await page.WaitForURLAsync(new Regex(Regex.Escape(to.Path) + "$"), new() { Timeout = E2ESteps.Slow });
                await Assertions.Expect(page.Locator($"#{to.Id}")).ToHaveAttributeAsync("aria-current", "page");
                await Assertions.Expect(page.Locator("#nav-projects")).ToHaveAttributeAsync("aria-current", to.Path == "/darbotvarke" ? "page" : "true");
            }
        }

        Directory.CreateDirectory(Shots);
        await page.GotoAsync(E2ESteps.Base + "/seimas/balsavimai");
        await page.Locator("main").ScreenshotAsync(new() { Path = Path.Combine(Shots, "tabs-390.png") });
        visitor.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_account_item_opens_and_closes_with_the_keyboard_and_shows_the_name_only_from_the_browser()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 390, 844);
        var page = member.Page;
        const string Name = "Klaviatūros Narė";
        await PasskeyTests.RegisterAsync(page, Name);
        await page.GotoAsync(E2ESteps.Base + "/projektai");

        // The page as served holds nothing about the person.
        var served = await E2ESteps.ApiAsync(page, "GET", "/projektai");
        served.Status.ShouldBe(200);
        System.Net.WebUtility.HtmlDecode(served.Body).ShouldNotContain(Name);
        served.Body.ShouldContain("id=\"account-menu\"");

        var item = page.Locator("#account-menu");
        var toggle = page.Locator("#account-toggle");
        await Assertions.Expect(item).Not.ToHaveAttributeAsync("open", "");

        // Keyboard: focus the toggle, Enter opens, the name comes from GET /me; Tab moves through the links.
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(item).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(page.Locator("[data-account-name]")).ToHaveTextAsync(Name);
        await Assertions.Expect(page.Locator("[data-account-link=moderation]")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("[data-account-link=mp]")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("[data-account-link=terms]")).ToBeHiddenAsync();
        Directory.CreateDirectory(Shots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, "account-open-390.png") });
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#nav-account")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#nav-sign-out button")).ToBeFocusedAsync();

        // Escape closes and gives the focus back to "Paskyra".
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(item).Not.ToHaveAttributeAsync("open", "");
        await Assertions.Expect(toggle).ToBeFocusedAsync();

        // Space opens it again; leaving it with Tab closes it.
        await page.Keyboard.PressAsync(" ");
        await Assertions.Expect(item).ToHaveAttributeAsync("open", "");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(item).Not.ToHaveAttributeAsync("open", "");

        // A press outside closes it.
        await toggle.ClickAsync();
        await Assertions.Expect(item).ToHaveAttributeAsync("open", "");
        await page.Mouse.ClickAsync(8, 600);
        await Assertions.Expect(item).Not.ToHaveAttributeAsync("open", "");

        // "Mano paskyra" leads to the account page; nothing about the item is stored.
        await toggle.ClickAsync();
        await page.Locator("#nav-account").ClickAsync();
        await page.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });
        member.ConsoleErrors.ShouldBeEmpty();
    }
}
