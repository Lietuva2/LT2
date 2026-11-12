using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 7.3 and 7.5: the parliament pages in a real browser against the stack with the invented parliament, at
/// 1280 px and 390 px, in both themes, without sideways scrolling, and once with JavaScript turned off. Screenshots go
/// to output/pages/. Uses no rate-limited endpoint.
///
/// Runs only when <c>LT2_E2E_BASE_URL</c> is set (see <see cref="SignUpTests"/>).
/// </summary>
public sealed class ParliamentPageTests
{
    private static readonly (string Name, string Path, string Heading)[] Pages =
    [
        ("mps", "/seimo-nariai", "Seimo nariai"),
        ("profile", "/p/jonas-petraitis", "Jonas Petraitis"),
        ("profile-votes", "/p/jonas-petraitis/balsavimai", "Jonas Petraitis: balsavimai"),
        ("former", "/p/tadas-alksninis", "Tadas Alksninis"),
        ("faction", "/frakcijos/regionu-aljanso-frakcija", "Regionų aljanso frakcija"),
        ("committee", "/komitetai/biudzeto-ir-finansu-komitetas", "Biudžeto ir finansų komitetas"),
        ("bill", "/seimas/XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa"),
        ("vote", "/seimas/balsavimai/99100002", "Gyventojų pajamų mokesčio įstatymo pataisa"),
        ("vote-consensus", "/seimas/balsavimai/99100003", "Vietinių rinkliavų įstatymo pataisa"),
        ("vote-note", "/seimas/balsavimai/99100004", "Atliekų tvarkymo įstatymo pataisa"),
        ("methodology", "/metodika", "Kaip skaičiuojama"),
        ("not-found", "/nera-tokio-puslapio", "Puslapis nerastas"),
    ];

    [Theory]
    [InlineData(1280, 800, "light")]
    [InlineData(1280, 800, "dark")]
    [InlineData(390, 844, "light")]
    [InlineData(390, 844, "dark")]
    public async Task Every_parliament_page_fits_the_width_without_sideways_scrolling_in_both_themes(int width, int height, string theme)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await Session.StartAsync(width, height, theme == "dark" ? ColorScheme.Dark : ColorScheme.Light, javaScript: true);
        var page = session.Page;

        foreach (var (name, path, heading) in Pages)
        {
            await page.GotoAsync(SignUpTests.BaseUrl + path);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(heading);
            var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
            overflow.ShouldBeLessThanOrEqualTo(0, $"{path} scrolls sideways by {overflow}px at {width}px ({theme})");
            (await page.EvaluateAsync<string>("() => matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'")).ShouldBe(theme);
            await page.Locator("#invented-data").WaitForAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine("output", "pages", $"{name}-{width}-{theme}.png"), FullPage = true });
        }

        session.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_javascript_every_page_renders_and_the_filters_work()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await Session.StartAsync(390, 844, ColorScheme.Light, javaScript: false);
        var page = session.Page;

        foreach (var (name, path, heading) in Pages)
        {
            await page.GotoAsync(SignUpTests.BaseUrl + path);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(heading);
            await page.ScreenshotAsync(new() { Path = Path.Combine("output", "pages", $"{name}-nojs.png"), FullPage = true });
        }

        // The MP list filters are a plain GET form.
        await page.GotoAsync(SignUpTests.BaseUrl + "/seimo-nariai");
        await page.GetByLabel("Frakcija").SelectOptionAsync(new SelectOptionValue { Label = "Pilietinės sąjungos frakcija" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Rodyti" }).ClickAsync();
        await page.WaitForURLAsync("**/seimo-nariai?*frakcija=pilietines-sajungos-frakcija*");
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Rasta: 3");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Ona Jonaitienė" })).ToBeVisibleAsync();

        // A link from the list to a profile and on to a vote, all without scripts.
        await page.GetByRole(AriaRole.Link, new() { Name = "Ona Jonaitienė" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Ona Jonaitienė");
        await page.Locator("a[href='/seimas/balsavimai/99100002']").ClickAsync();
        await Assertions.Expect(page.Locator("svg.seatmap circle")).ToHaveCountAsync(12);
    }

    private sealed class Session : IAsyncDisposable
    {
        private IPlaywright _playwright = default!;
        private IBrowser _browser = default!;
        private IBrowserContext _context = default!;

        public IPage Page { get; private set; } = default!;

        public List<string> ConsoleErrors { get; } = [];

        public static async Task<Session> StartAsync(int width, int height, ColorScheme scheme, bool javaScript)
        {
            Directory.CreateDirectory(Path.Combine("output", "pages"));
            var session = new Session { _playwright = await Playwright.CreateAsync() };
            session._browser = await session._playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = SignUpTests.Channel, Headless = true });
            session._context = await session._browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                IgnoreHTTPSErrors = true,
                Locale = "lt-LT",
                ColorScheme = scheme,
                JavaScriptEnabled = javaScript,
            });
            session.Page = await session._context.NewPageAsync();
            session.Page.Console += (_, message) =>
            {
                // The not-found page answers 404 on purpose; the browser logs that as a failed resource.
                if (message.Type == "error" && !message.Text.Contains("404", StringComparison.Ordinal))
                {
                    session.ConsoleErrors.Add(message.Text);
                }
            };
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _browser.DisposeAsync();
            _playwright.Dispose();
        }
    }
}
