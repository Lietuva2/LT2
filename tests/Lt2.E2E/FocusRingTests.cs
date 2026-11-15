using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Blazor focuses the page's h1 on load (FocusOnNavigate) so screen readers start at the title. Nobody has clicked
/// yet, so the browser counts that as keyboard focus and our focus ring was drawn around the heading; on pages that
/// run in WebAssembly the heading is then re-rendered and the ring blinked. A heading is not a control: it keeps the
/// focus but gets no ring, while links and buttons still get one.
///
/// Runs only when <c>LT2_E2E_BASE_URL</c> is set (see <see cref="SignUpTests"/>). Uses no rate-limited endpoint.
/// </summary>
public sealed class FocusRingTests
{
    [Fact]
    public async Task The_heading_focused_on_load_has_no_ring_and_a_tabbed_link_still_has_one()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true, Locale = "lt-LT" });
        var page = await context.NewPageAsync();

        await page.GotoAsync(SignUpTests.BaseUrl!);
        var heading = page.GetByRole(AriaRole.Heading, new() { Level = 1 });
        await Assertions.Expect(heading).ToBeFocusedAsync();
        (await heading.EvaluateAsync<bool>("h => h.matches(':focus-visible')")).ShouldBeTrue("the browser counts the load focus as keyboard focus");
        (await heading.EvaluateAsync<string>("h => getComputedStyle(h).outlineStyle")).ShouldBe("none");

        // Tab on to the first link. How many presses that takes depends on whether the sign-in island has started: before
        // it, its fields are disabled and the first press reaches a link; after it, the fields and buttons come first.
        var tag = string.Empty;
        for (var presses = 0; presses < 10 && tag != "A"; presses++)
        {
            await page.Keyboard.PressAsync("Tab");
            tag = await page.EvaluateAsync<string>("() => document.activeElement.tagName");
        }

        tag.ShouldBe("A");
        (await page.EvaluateAsync<string>("() => getComputedStyle(document.activeElement).outlineStyle")).ShouldBe("solid");
    }
}
