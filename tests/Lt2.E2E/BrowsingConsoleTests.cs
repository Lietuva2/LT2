using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// A member's browsing leaves the browser's console free of errors. Enhanced navigation (blazor.web.js) removes the
/// islands of the page being left and adds those of the page reached; an island that cannot be created in the browser
/// (a service its component injects that the WebAssembly host does not register) shows only as
/// "No root component exists with SSR component ID N" when the page is left, or when Back or Forward returns to it. The
/// walk goes forward through the pages that hold islands by their links (never <c>goto</c> between them), then Back and
/// Forward over all of them, and fails on the first console error or page error, naming the step.
/// </summary>
public sealed class BrowsingConsoleTests : E2ETest
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Fact]
    public async Task A_member_going_forward_Back_and_Forward_over_pages_with_islands_gets_no_console_errors()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 800);
        await PasskeyTests.RegisterAsync(member.Page, "Naršanti Narė");
        var page = member.Page;

        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                lock (errors) { errors.Add(message.Text.Split('\n')[0]); }
            }
        };
        page.PageError += (_, error) =>
        {
            lock (errors) { errors.Add("page error: " + error.Split('\n')[0]); }
        };

        async Task StepAsync(string step, Func<Task> act)
        {
            await act();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            string[] seen;
            lock (errors) { seen = [.. errors]; errors.Clear(); }
            seen.ShouldBeEmpty($"console errors after: {step}");
        }

        Task ClickAsync(string step, string selector) =>
            StepAsync(step, () => page.Locator(selector).Locator("visible=true").First.ClickAsync());
        Task BackAsync(string step) => StepAsync("Back: " + step, () => page.GoBackAsync());
        Task ForwardAsync(string step) => StepAsync("Forward: " + step, () => page.GoForwardAsync());

        await StepAsync("front page", () => page.GotoAsync(Base + "/"));
        await ClickAsync("menu: Projektai", "nav a[href='/darbotvarke']");
        await ClickAsync("tab: Aktyvūs", "a[href='/projektai']");
        await ClickAsync("tab: Uždaryti", "a[href='/projektai/uzdaryti']");
        await ClickAsync("tab: Aktyvūs again", "a[href='/projektai']");
        await ClickAsync("sort: pagal naujausią veiklą", "main a[href='/projektai?rikiuoti=naujausia']");
        await ClickAsync("a bill", "main a[href='/seimas/XVP-0412']");

        // The bill's "Seimas" panel reads the vote and draws the seat map (an island that fetches after it has started).
        await StepAsync("the Seimas panel", () => page.Locator("label[for='view-seimas']").ClickAsync());
        await Assertions.Expect(page.Locator("#seimas-seats")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await ClickAsync("the vote page from the bill's Seimas panel", "#seimas-vote-page");
        await ClickAsync("menu: Balsavimai", "nav a[href='/seimas/balsavimai']");
        await ClickAsync("a vote of the history", "main a[href^='/seimas/balsavimai/']");
        await ClickAsync("menu: Seimo nariai", "nav a[href='/seimo-nariai']");
        await ClickAsync("an MP's profile", "main a[href^='/p/']");

        await BackAsync("MP list");
        await BackAsync("vote page");
        await BackAsync("history of votes");
        await BackAsync("vote page of the bill's Seimas panel");
        await BackAsync("bill");
        await BackAsync("projektai, sorted");
        await BackAsync("projektai");
        await BackAsync("projektai, closed");
        await BackAsync("projektai, first visit");
        await BackAsync("agenda");
        await ForwardAsync("projektai, first visit");
        await ForwardAsync("projektai, closed");
        await ForwardAsync("projektai");
        await ForwardAsync("projektai, sorted");
        await ForwardAsync("bill");
        await ForwardAsync("vote page of the bill's Seimas panel");
        await ForwardAsync("history of votes");
        await BackAsync("vote page of the bill's Seimas panel");
        await BackAsync("bill");
    }
}
