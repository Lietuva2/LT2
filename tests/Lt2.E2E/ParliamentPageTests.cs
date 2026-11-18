using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 7.3 and 7.5: the parliament pages in a real browser against the stack with the invented parliament, at
/// 1280 px and 390 px, in both themes, without sideways scrolling, and once with JavaScript turned off. Screenshots go
/// to output/pages/. Uses no rate-limited endpoint.
///
/// Runs only when <c>LT2_E2E_BASE_URL</c> is set (see <see cref="SignUpTests"/>).
/// </summary>
public sealed class ParliamentPageTests : E2ETest
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
        ("votes", "/seimas/balsavimai", "Seimo balsavimai"),
        ("vote", "/seimas/balsavimai/99100002", "Gyventojų pajamų mokesčio įstatymo pataisa"),
        ("vote-consensus", "/seimas/balsavimai/99100003", "Vietinių rinkliavų įstatymo pataisa"),
        ("vote-note", "/seimas/balsavimai/99100004", "Atliekų tvarkymo įstatymo pataisa"),
        ("methodology", "/metodika", "Metodika"),
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
    public async Task Without_javascript_every_page_renders_the_filters_work_and_there_is_no_dead_theme_button()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await Session.StartAsync(390, 844, ColorScheme.Light, javaScript: false);
        var page = session.Page;

        // Every request with its outcome and the page's load events, and a trace kept only on failure: this test once failed
        // twice in full runs waiting for a page's "load" (2026-11-16); the next occurrence must say what was outstanding.
        var network = Path.Combine("output", "pages", "nojs-network.txt");
        File.WriteAllText(network, "");
        void Note(string line) => File.AppendAllText(network, $"{DateTime.UtcNow:HH:mm:ss.fff} {line}{Environment.NewLine}");
        page.Request += (_, r) => Note($"request {r.Method} {r.Url} ({r.ResourceType})");
        page.RequestFinished += (_, r) => Note($"finished {r.Url}");
        page.RequestFailed += (_, r) => Note($"failed {r.Url}: {r.Failure}");
        page.Load += (_, _) => Note($"load {page.Url}");
        page.DOMContentLoaded += (_, _) => Note($"domcontentloaded {page.Url}");
        page.FrameNavigated += (_, f) => Note($"navigated {f.Url}");
        await session.Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Title = "no javascript" });
        try
        {
            await WithoutJavaScriptAsync(page);
        }
        catch
        {
            await session.Context.Tracing.StopAsync(new() { Path = Path.Combine("output", "pages", $"nojs-trace-{Guid.NewGuid():N}.zip") });
            throw;
        }

        await session.Context.Tracing.StopAsync();
    }

    /// <summary>
    /// The page is at an address matching <paramref name="pattern"/>, polled. With scripts off, <c>WaitForURLAsync</c> once waited
    /// its 30 s for a "load" that had fired 35 ms after the click (2026-11-16, the network log above); the checks after this wait
    /// for the page's content themselves.
    /// </summary>
    private static Task AtAsync(IPage page, string pattern) =>
        Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(pattern), new() { Timeout = E2ESteps.Slow });
    private static async Task WithoutJavaScriptAsync(IPage page)
    {
        foreach (var (name, path, heading) in Pages)
        {
            await page.GotoAsync(SignUpTests.BaseUrl + path);
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(heading);
            await Assertions.Expect(page.Locator("[data-theme-switch]")).ToBeHiddenAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine("output", "pages", $"{name}-nojs.png"), FullPage = true });
        }

        // The MP list filters are a plain GET form.
        await page.GotoAsync(SignUpTests.BaseUrl + "/seimo-nariai");
        await page.GetByLabel("Frakcija").SelectOptionAsync(new SelectOptionValue { Label = "Pilietinės sąjungos frakcija" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Rodyti" }).ClickAsync();
        await AtAsync(page, @"/seimo-nariai\?.*frakcija=pilietines-sajungos-frakcija");
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Rasta: 28");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Ona Jonaitienė" })).ToBeVisibleAsync();

        // A link from the list to a profile and on to a vote, all without scripts.
        await page.GetByRole(AriaRole.Link, new() { Name = "Ona Jonaitienė" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Ona Jonaitienė");
        // Her latest votes are whatever the invented Seimas held last; any of them leads to a vote page.
        await page.Locator("main a[href^='/seimas/balsavimai/9']").First.ClickAsync();
        await AtAsync(page, @"/seimas/balsavimai/\d+$");
        await Assertions.Expect(page.Locator("svg.seatmap rect.seat")).ToHaveCountAsync(141);
        await page.GotoAsync(SignUpTests.BaseUrl + "/seimas/balsavimai/99100002");

        // Design 8.17: the ballots' search and faction are a plain GET form and the ballot a plain link, all landing on the list.
        var ballotSearch = page.Locator("#ballot-search");
        await ballotSearch.GetByLabel("Vardas ar pavardė").FillAsync("azuol");
        await ballotSearch.GetByRole(AriaRole.Button, new() { Name = "Rodyti", Exact = true }).ClickAsync();
        await AtAsync(page, @"/seimas/balsavimai/99100002\?q=azuol[^#]*#balsai$");
        await Assertions.Expect(page.Locator("#ballot-rows > li")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#ballot-rows")).ToContainTextAsync("Marius Ąžuolaitis");
        await page.Locator("#ballot-clear").ClickAsync();
        await AtAsync(page, @"/seimas/balsavimai/99100002#balsai$");
        await page.Locator("#ballots-pries").ClickAsync();
        await AtAsync(page, @"/seimas/balsavimai/99100002\?balsas=pries#balsai$");
        await Assertions.Expect(page.Locator("#ballot-rows > li")).ToHaveCountAsync(20);
        await Assertions.Expect(page.Locator("#ballots-shown")).ToHaveTextAsync("Rodoma 20 iš 38");
        await ballotSearch.GetByLabel("Frakcija").SelectOptionAsync(new SelectOptionValue { Label = "Pilietinės sąjungos frakcija" });
        await ballotSearch.GetByRole(AriaRole.Button, new() { Name = "Rodyti", Exact = true }).ClickAsync();
        await AtAsync(page, @"/seimas/balsavimai/99100002\?[^#]*frakcija=pilietines-sajungos-frakcija[^#]*#balsai$");
        await Assertions.Expect(page.Locator("#ballot-rows > li")).ToHaveCountAsync(8);
        await Assertions.Expect(page.Locator("#ballots-pries")).ToHaveAttributeAsync("aria-current", "true");
        await Assertions.Expect(page.Locator("#ballots-shown")).ToHaveTextAsync("Rodoma 8 iš 8");
    }

    [Fact]
    public async Task The_theme_switch_cycles_system_light_dark_and_keeps_the_choice_across_pages()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await Session.StartAsync(1280, 800, ColorScheme.Light, javaScript: true);
        var page = session.Page;
        await page.GotoAsync(SignUpTests.BaseUrl + "/seimo-nariai");

        // Design 8.21: the switch is in the footer, for visitors too; nothing of it is in the header.
        var button = page.Locator("[data-theme-switch]");
        await Assertions.Expect(page.Locator("footer [data-theme-switch]")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("header [data-theme-switch]")).ToHaveCountAsync(0);
        await Assertions.Expect(button).ToHaveTextAsync("Tema: automatinė");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Tema: šviesi");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Tema: tamsi");
        (await page.EvaluateAsync<string>("() => document.documentElement.dataset.theme")).ShouldBe("dark");
        var ground = await page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
        ground.ShouldBe("rgb(14, 20, 17)", "the dark ground token");

        // Enhanced navigation to another page: the choice and the button stay.
        await page.GetByRole(AriaRole.Link, new() { Name = "Jonas Petraitis" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jonas Petraitis");
        await Assertions.Expect(button).ToBeVisibleAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Tema: tamsi");

        // A full reload applies the stored choice before the page is shown.
        await page.ReloadAsync();
        (await page.EvaluateAsync<string>("() => document.documentElement.dataset.theme")).ShouldBe("dark");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Tema: automatinė");
        (await page.EvaluateAsync<bool>("() => document.documentElement.hasAttribute('data-theme')")).ShouldBeFalse();
        session.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_tag_that_is_a_link_keeps_the_tag_look_without_an_underline_until_pointed_at()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var session = await Session.StartAsync(1280, 800, ColorScheme.Light, javaScript: true);
        var page = session.Page;

        foreach (var path in new[] { "/p/jonas-petraitis", "/seimo-nariai" })
        {
            await page.GotoAsync(SignUpTests.BaseUrl + path);
            var tags = page.Locator("a.tag");
            (await tags.CountAsync()).ShouldBeGreaterThan(0, path);
            foreach (var tag in await tags.AllAsync())
            {
                (await tag.EvaluateAsync<string>("t => getComputedStyle(t).textDecorationLine")).ShouldBe("none", path);
            }

            var first = page.Locator("a.tag.tag--ink").First;
            (await first.EvaluateAsync<string>("t => getComputedStyle(t).color")).ShouldBe("rgb(255, 255, 255)", "the ink tag's own colour (--sheet), not the link colour");
            await first.HoverAsync();
            (await first.EvaluateAsync<string>("t => getComputedStyle(t).textDecorationLine")).ShouldBe("underline", path);
        }
    }

    private sealed class Session : IAsyncDisposable
    {
        private IPlaywright _playwright = default!;
        private IBrowser _browser = default!;
        private IBrowserContext _context = default!;

        public IPage Page { get; private set; } = default!;

        public IBrowserContext Context => _context;

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
            await E2ETest.KeepScreenAsync(Page);
            await _context.DisposeAsync();
            await _browser.DisposeAsync();
            _playwright.Dispose();
        }
    }
}
