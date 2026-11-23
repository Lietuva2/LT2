using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// The calendar button beside a typed date field (owner, 2026-11-23), on the history of votes' "Iki": the button is shown
/// only with JavaScript; a press makes the hidden native date input over the text field; a day picked there (set here, as a
/// native popup cannot be driven) lands in the text field as <c>yyyy-MM-dd</c> and is what the form sends; typing by hand
/// still works; with JavaScript off the button stays hidden. The native popup itself is not under test.
/// </summary>
public sealed class DatePickTests : E2ETest
{
    private const string VotesPath = "/seimas/balsavimai";

    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Fact]
    public async Task A_press_makes_the_native_date_input_over_the_field_and_a_picked_day_lands_in_the_text_field_and_is_sent()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await PageAsync(1280, 900, javaScript: true);
        var page = session.Page;
        var (text, button) = (page.Locator("#votes-to"), page.Locator("button[data-date-pick='votes-to']"));

        await Assertions.Expect(button).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-label", "Pasirinkti datą iš kalendoriaus");
        (await text.GetAttributeAsync("type")).ShouldBe("text");
        var (fieldBox, buttonBox) = ((await text.BoundingBoxAsync())!, (await button.BoundingBoxAsync())!);
        buttonBox.Height.ShouldBe(fieldBox.Height, 1, "the button is as tall as the field");
        buttonBox.X.ShouldBeGreaterThanOrEqualTo(fieldBox.X + fieldBox.Width - 1, "the button stands right after the field");
        (await page.Locator("input[type=date]").CountAsync()).ShouldBe(0, "no date input before a press");

        await button.ClickAsync();
        var native = page.Locator("input[type=date].date-pick__native");
        (await native.CountAsync()).ShouldBe(1);
        (await native.GetAttributeAsync("aria-hidden")).ShouldBe("true");
        (await native.GetAttributeAsync("tabindex")).ShouldBe("-1");
        var box = (await native.BoundingBoxAsync())!;
        (box.X, box.Y, box.Width, box.Height).ShouldBe((fieldBox.X, fieldBox.Y, fieldBox.Width, fieldBox.Height), "it lies over the text field");

        // What the browser's popup would do on a pick: the input's value (always ISO) changes and "change" fires.
        await native.EvaluateAsync("el => { el.value = '2026-03-05'; el.dispatchEvent(new Event('change', { bubbles: true })); }");
        await Assertions.Expect(text).ToHaveValueAsync("2026-03-05");
        await Assertions.Expect(text).ToBeFocusedAsync();

        // A second press reuses the one input and starts from what the text field holds.
        await button.ClickAsync();
        (await page.Locator("input[type=date]").CountAsync()).ShouldBe(1);
        (await native.InputValueAsync()).ShouldBe("2026-03-05");

        await page.Locator("#votes-search button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(u => u.Contains("iki=2026-03-05", StringComparison.Ordinal), new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#votes-to")).ToHaveValueAsync("2026-03-05");
        session.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Typing_a_day_by_hand_still_works_and_the_native_input_starts_empty_for_a_text_that_is_not_a_day()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await PageAsync(1280, 900, javaScript: true);
        var page = session.Page;
        var (text, button) = (page.Locator("#votes-to"), page.Locator("button[data-date-pick='votes-to']"));

        await text.FillAsync("2025-12-31");
        await page.Locator("#votes-search button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(u => u.Contains("iki=2025-12-31", StringComparison.Ordinal), new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("#votes-to")).ToHaveValueAsync("2025-12-31");

        await page.Locator("#votes-to").FillAsync("vakar");
        await button.ClickAsync();
        (await page.Locator("input[type=date].date-pick__native").InputValueAsync()).ShouldBe("");
        await Assertions.Expect(page.Locator("#votes-to")).ToHaveValueAsync("vakar");
        session.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_javascript_the_button_stays_hidden_and_the_field_works_as_before()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await PageAsync(1280, 900, javaScript: false);
        var page = session.Page;

        await Assertions.Expect(page.Locator("#votes-to")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("button[data-date-pick='votes-to']")).ToBeHiddenAsync();
        await page.Locator("#votes-to").FillAsync("2025-12-31");
        await page.Locator("#votes-search button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(u => u.Contains("iki=2025-12-31", StringComparison.Ordinal), new() { Timeout = E2ESteps.Slow });
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
        page.PageError += (_, error) => session.ConsoleErrors.Add("page error: " + error);
        await page.GotoAsync(Base + VotesPath);
        await Assertions.Expect(page.Locator("#votes-to")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
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
