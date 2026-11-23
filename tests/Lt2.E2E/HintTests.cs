using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.15, "A hint", against the running stack on the bill's page "Kas yra riba?": hover opens it, keyboard focus
/// opens it and its link can be tabbed to, Escape closes it and gives the focus back, a press keeps it open until a press
/// elsewhere, it stays inside a 390 px screen, and with JavaScript off a press still opens it.
/// </summary>
public sealed class HintTests : E2ETest
{
    private const string Bill = "/seimas/XVP-0412";

    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Fact]
    public async Task Hover_opens_the_hint_and_leaving_the_word_and_the_panel_closes_it()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await PageAsync(1280, 900, javaScript: true);
        var page = session.Page;
        var (trigger, panel) = (page.Locator("#threshold-what"), page.Locator("#threshold-what-panel"));
        await Assertions.Expect(panel).ToBeHiddenAsync();

        await trigger.HoverAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(panel).ToContainTextAsync("Riba – dalyvių skaičius");
        await Assertions.Expect(panel).ToContainTextAsync("Dabar –");

        // The pointer can move into the panel and stay there.
        await panel.HoverAsync();
        await page.WaitForTimeoutAsync(800);
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator("a[href='/metodika#threshold']")).ToBeVisibleAsync();

        await page.Mouse.MoveAsync(5, 5);
        await Assertions.Expect(panel).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Keyboard_focus_opens_the_hint_its_link_is_reached_by_tab_and_escape_closes_it_back_on_the_word()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await PageAsync(1280, 900, javaScript: true);
        var page = session.Page;
        var (trigger, panel) = (page.Locator("#threshold-what"), page.Locator("#threshold-what-panel"));

        // Reach the trigger by the keyboard: focus it, step back and forth.
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(trigger).ToBeFocusedAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(panel.Locator("a")).ToBeFocusedAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await Assertions.Expect(trigger).ToBeFocusedAsync();
        await page.WaitForTimeoutAsync(800);
        await Assertions.Expect(panel).ToBeHiddenAsync(new() { Timeout = 1 });

        // Enter opens it again and keeps it open.
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        session.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_press_keeps_the_hint_open_until_a_press_elsewhere_and_a_second_press_closes_it()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await PageAsync(1280, 900, javaScript: true);
        var page = session.Page;
        var (trigger, panel) = (page.Locator("#threshold-what"), page.Locator("#threshold-what-panel"));

        await trigger.ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await page.Mouse.MoveAsync(5, 5);
        await page.WaitForTimeoutAsync(1000);
        await Assertions.Expect(panel).ToBeVisibleAsync();

        await page.Locator("h1").ClickAsync();
        await Assertions.Expect(panel).ToBeHiddenAsync();

        await trigger.ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await trigger.ClickAsync();
        await Assertions.Expect(panel).ToBeHiddenAsync();

        // Beside the word: under it or above it, not in the middle of the screen.
        await trigger.ClickAsync();
        var word = (await trigger.BoundingBoxAsync())!;
        var box = (await panel.BoundingBoxAsync())!;
        (box.Y >= word.Y + word.Height - 1 || box.Y + box.Height <= word.Y + 1).ShouldBeTrue($"the panel {box.Y}–{box.Y + box.Height} is beside the word {word.Y}–{word.Y + word.Height}");
        session.ConsoleErrors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task On_a_390_px_screen_a_tap_opens_the_hint_inside_the_screen_with_or_without_javascript(bool javaScript)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await PageAsync(390, 844, javaScript);
        var page = session.Page;
        var (trigger, panel) = (page.Locator("#threshold-what"), page.Locator("#threshold-what-panel"));
        await Assertions.Expect(panel).ToBeHiddenAsync();

        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await Assertions.Expect(panel).ToContainTextAsync("Riba – dalyvių skaičius");
        var box = (await panel.BoundingBoxAsync())!;
        box.X.ShouldBeGreaterThanOrEqualTo(0f);
        box.Y.ShouldBeGreaterThanOrEqualTo(0f);
        (box.X + box.Width).ShouldBeLessThanOrEqualTo(390f);
        (box.Y + box.Height).ShouldBeLessThanOrEqualTo(844f);
        (await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).ShouldBeTrue("no sideways scrolling");

        await page.Locator("h1").ClickAsync();
        await Assertions.Expect(panel).ToBeHiddenAsync();
        if (javaScript)
        {
            session.ConsoleErrors.ShouldBeEmpty();
        }
    }

    private static async Task<Session> PageAsync(int width, int height, bool javaScript)
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        var context = await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = javaScript, ViewportSize = new() { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();
        var session = new Session(playwright, browser, page);
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                session.ConsoleErrors.Add(message.Text);
            }
        };
        await page.GotoAsync(Base + Bill);
        await Assertions.Expect(page.Locator("#threshold-what")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        return session;
    }

    private sealed class Session(IPlaywright playwright, IBrowser browser, IPage page) : IAsyncDisposable
    {
        public IPage Page { get; } = page;

        public List<string> ConsoleErrors { get; } = [];

        public async ValueTask DisposeAsync()
        {
            await E2ETest.KeepScreenAsync(Page);
            await browser.DisposeAsync();
            playwright.Dispose();
        }
    }
}
