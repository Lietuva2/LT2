using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.21 against the stack: the menu (logo, "Projektai", "Seimo nariai", "Statistika", "Prisijungti" or "Paskyra") on one
/// line at 360, 390 and 1280 px in both themes, for a visitor and a member; the four tabs of "Projektai" reaching each other,
/// and its dropdown (owner, 2026-11-17; "Balsavimai" a tab again, 2026-11-19); the account
/// item with the keyboard, its name and links filled in the browser only. Screenshots go to <c>output/menu</c>.
/// </summary>
public sealed class MenuTests : E2ETest
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
              if (items.length !== 4) return 'expected four menu items, found ' + items.length;
              const middles = [brand, ...items].map(r => Math.round(r.top + r.height / 2));
              if (Math.max(...middles) - Math.min(...middles) > 8) return 'not one line: ' + middles.join(',');
              if (Math.min(...items.map(r => r.left)) < brand.right) return 'an item runs into the brand';
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
        await Assertions.Expect(page.Locator("#nav-projects")).ToHaveTextAsync("Projektai");
        await Assertions.Expect(nav.Locator("> a")).ToHaveTextAsync(["Seimo nariai", "Statistika", "Prisijungti"]);
        await Assertions.Expect(page.Locator("#nav-projects")).ToHaveAttributeAsync("aria-current", "true", new() { Timeout = E2ESteps.Slow });

        // Owner, 2026-11-19: with a mouse, "Projektai" shows its dropdown on hover (its four tabs, "Balsavimai" again) and
        // Escape hides it; the keyboard reaches it by focus; a press on "Projektai" opens the agenda.
        await page.GotoAsync(E2ESteps.Base + "/metodika");
        var panel = page.Locator("#projects-panel");
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await page.Locator("#nav-projects").HoverAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator(".drop__label")).ToHaveTextAsync(["Darbotvarkė", "Projektai", "Balsavimai", "Suvestinės"]);
        await page.Locator("#nav-tab-seimas-balsavimai").HoverAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await page.Locator("main h1").HoverAsync();
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await page.Locator(".top__brand").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#nav-projects")).ToBeFocusedAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#nav-tab-darbotvarke")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#nav-projects")).ToBeFocusedAsync();
        await page.Locator("#nav-projects").ClickAsync();
        await page.WaitForURLAsync(new Regex("/darbotvarke$"), new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#nav-projects-touch")).ToBeHiddenAsync();
        visitor.ConsoleErrors.ShouldBeEmpty();
    }

    /// <summary>
    /// Owner, 2026-11-19: on a touch screen "Projektai" stays a disclosure, opened by a press (also without script), closed by
    /// a press outside; it never leads to a page by itself.
    /// </summary>
    [Fact]
    public async Task On_a_touch_screen_Projektai_opens_its_dropdown_by_a_press()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var visitor = await PasskeyBrowser.StartAsync("native", 1280, 900);
        foreach (var script in new[] { true, false })
        {
            await using var context = await TouchAsync(visitor.Page, 390, script);
            var page = await context.NewPageAsync();
            await page.GotoAsync(E2ESteps.Base + "/metodika");
            await Assertions.Expect(page.Locator("#nav-projects")).ToBeHiddenAsync();
            await page.Locator("#nav-projects-touch").TapAsync();
            await Assertions.Expect(page.Locator("#projects-panel")).ToBeVisibleAsync();
            page.Url.ShouldEndWith("/metodika", customMessage: "a press opens the dropdown, not a page");
            await Assertions.Expect(page.Locator("#projects-panel .drop__label")).ToHaveTextAsync(["Darbotvarkė", "Projektai", "Balsavimai", "Suvestinės"]);
            if (script)
            {
                await page.Locator("footer .foot__groups").TapAsync();
                await Assertions.Expect(page.Locator("#projects-panel")).ToBeHiddenAsync();
                await page.Locator("#nav-projects-touch").TapAsync();
            }

            await page.Locator("#nav-tab-projektai").TapAsync();
            await page.WaitForURLAsync(new Regex("/projektai$"), new() { Timeout = E2ESteps.Slow });
        }

        visitor.ConsoleErrors.ShouldBeEmpty();
    }

    /// <summary>A touch phone's context (no hover, a coarse pointer), with or without script.</summary>
    private static Task<IBrowserContext> TouchAsync(IPage page, int width, bool script = true) =>
        page.Context.Browser!.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = script, HasTouch = true, IsMobile = true,
            ViewportSize = new() { Width = width, Height = 780 },
        });

    /// <summary>
    /// Design 8.28.2, 8.28.7: the dropdown of "Projektai" shows its four entries, each with its line, opens without script,
    /// and fits at 340, 360, 390 and 1280 px with no sideways scroll; the menu is still one line. Screenshots for the owner.
    /// </summary>
    [Fact]
    public async Task The_projects_dropdown_shows_each_entry_with_its_line_and_fits_from_340_px()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var visitor = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = visitor.Page;
        Directory.CreateDirectory(Shots);
        string[] lines =
        [
            "Ką Seimas planuoja svarstyti artimiausiomis dienomis.",
            "Visi projektai, kurių Seimas dar nebaigė svarstyti.",
            "Kaip Seimas balsavo: visi balsavimai nuo naujausio.",
            "Ką apie dalyvių prašymus sužino Seimo nariai.",
        ];
        // On a touch phone (a press) at 340, 360 and 390 px, and with a mouse (hover) at 1280 and 390 px.
        foreach (var (width, touch) in new[] { (340, true), (360, true), (390, true), (1280, false), (390, false) })
        {
            await using var context = touch ? await TouchAsync(page, width) : null;
            var shown = context is null ? page : await context.NewPageAsync();
            await shown.SetViewportSizeAsync(width, width == 1280 ? 900 : 780);
            await shown.GotoAsync(E2ESteps.Base + "/darbotvarke");
            (await MenuProblemAsync(shown)).ShouldBeNull($"the menu at {width} px");
            if (touch)
            {
                await shown.Locator("#nav-projects-touch").TapAsync();
            }
            else
            {
                await shown.Locator("#nav-projects").HoverAsync();
            }

            var panel = shown.Locator("#projects-panel");
            await Assertions.Expect(panel).ToBeVisibleAsync();
            await Assertions.Expect(panel.Locator(".drop__desc")).ToHaveTextAsync(lines);
            await Assertions.Expect(panel).ToHaveCSSAsync("opacity", "1");
            var box = await panel.BoundingBoxAsync();
            box!.X.ShouldBeGreaterThanOrEqualTo(0, $"the panel's left edge at {width} px");
            (box.X + box.Width).ShouldBeLessThanOrEqualTo(width, $"the panel's right edge at {width} px");
            await E2ESteps.NoSidewaysScrollAsync(shown, $"the open dropdown at {width} px");
            await shown.ScreenshotAsync(new() { Path = Path.Combine(Shots, $"dropdown-{width}-{(touch ? "touch" : "mouse")}.png") });
            await shown.Keyboard.PressAsync("Escape");
            await Assertions.Expect(panel).ToBeHiddenAsync();
        }

        await page.SetViewportSizeAsync(1280, 900);

        // Design 8.28.4: "Kur ką rasti" under the steps of /metodika, for the owner's look.
        foreach (var width in new[] { 390, 1280 })
        {
            await page.SetViewportSizeAsync(width, 900);
            await page.GotoAsync(E2ESteps.Base + "/metodika#kur-ka-rasti");
            await Assertions.Expect(page.Locator("#kur-ka-rasti dd")).ToHaveCountAsync(14);
            await E2ESteps.NoSidewaysScrollAsync(page, $"/metodika at {width} px");
            await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, $"where-{width}.png"), FullPage = true });
        }

        // Without script the dropdown still shows on hover, with the same lines.
        await using var noScript = await page.Context.Browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false });
        var plain = await noScript.NewPageAsync();
        await plain.SetViewportSizeAsync(1280, 900);
        await plain.GotoAsync(E2ESteps.Base + "/darbotvarke");
        await plain.Locator("#nav-projects").HoverAsync();
        await Assertions.Expect(plain.Locator("#projects-panel .drop__desc")).ToHaveTextAsync(lines);
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
            ("/projektai", "Projektai", "tab-projektai"),
            ("/seimas/balsavimai", "Balsavimai", "tab-seimas-balsavimai"),
            ("/suvestines", "Suvestinės", "tab-suvestines"),
        ];

        // From the menu item's dropdown to the agenda, then from every tab to every other by its link.
        await page.GotoAsync(E2ESteps.Base + "/metodika");
        await page.Locator("#nav-projects").HoverAsync();
        await page.Locator("#nav-tab-darbotvarke").ClickAsync();
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
                await Assertions.Expect(page.Locator("#nav-projects")).ToHaveAttributeAsync("aria-current", "true");
            }
        }

        // The link under the agenda's voted list stays as a second way to the history of votes; its tab row leads back.
        await page.GotoAsync(E2ESteps.Base + "/darbotvarke");
        await page.Locator("#agenda-votes-older a").ClickAsync();
        await page.WaitForURLAsync(new Regex("/seimas/balsavimai$"), new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#tab-seimas-balsavimai")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(page.Locator("#nav-projects")).ToHaveAttributeAsync("aria-current", "true");
        await E2ESteps.NoSidewaysScrollAsync(page);
        Directory.CreateDirectory(Shots);
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
        await Assertions.Expect(page.Locator("[data-account-link=staff-key]")).ToBeHiddenAsync();
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
        await page.WaitForURLAsync(new Regex("/paskyra$"), new() { Timeout = E2ESteps.Slow });

        // Design 8.21 (owner, 2026-11-16): the account is four screens; the row under the title leads from one to another and
        // marks the screen it stands on.
        var follows = page.Locator("#account-parts").GetByRole(AriaRole.Link, new() { Name = "Sekimas ir laiškai", Exact = true });
        await Assertions.Expect(follows).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await follows.ClickAsync();
        await page.WaitForURLAsync(new Regex("/paskyra/sekimas$"), new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#sekimas-title")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#part-follows")).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(page.Locator("#account-parts")).ToBeVisibleAsync();
        member.ConsoleErrors.ShouldBeEmpty();
    }
}
