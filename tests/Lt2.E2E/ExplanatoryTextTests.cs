using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.15 "Done when", against the running stack: the organisation registration form shows one sentence before its
/// first field and the rest behind "Kaip tai veikia", which opens with JavaScript switched off too; every link to a part of
/// the methodology page lands on a part that exists.
/// </summary>
public sealed class ExplanatoryTextTests
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Fact]
    public async Task The_registration_form_shows_one_sentence_and_the_rest_one_click_away_with_or_without_javascript()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Aiškių Tekstų Narė");
        await page.GotoAsync($"{Base}/organizacijos/nauja");

        // Before the first field: the heading, one sentence, and the closed "Kaip tai veikia".
        var lead = page.Locator("#register-lead");
        await Assertions.Expect(lead).ToBeVisibleAsync();
        (await lead.InnerTextAsync()).Trim().TrimEnd('.').ShouldNotContain(". ", customMessage: "one sentence");
        var more = page.Locator("details#register-more");
        await Assertions.Expect(more.Locator("summary")).ToHaveTextAsync("Kaip tai veikia");
        await Assertions.Expect(page.Locator("#register-what")).ToBeHiddenAsync();
        var visibleBefore = await page.EvaluateAsync<string[]>(
            """
            () => {
              const field = document.querySelector('#org-name');
              return [...document.querySelectorAll('main p, main li')]
                .filter(e => e.offsetParent !== null && (e.compareDocumentPosition(field) & Node.DOCUMENT_POSITION_FOLLOWING))
                .filter(e => !e.closest('noscript, ol.crumbs, summary'))
                .map(e => e.innerText.trim()).filter(t => t.length > 0);
            }
            """);
        visibleBefore.ShouldBe([await lead.InnerTextAsync()], "only the lead explains before the first field");

        await more.Locator("summary").ClickAsync();
        await Assertions.Expect(page.Locator("#register-what li")).ToHaveCountAsync(3);
        await Assertions.Expect(more.Locator("a[href='/metodika#organizacijos']")).ToBeVisibleAsync();

        // Without JavaScript the same disclosure opens: it is the browser's own <details>.
        var browser = page.Context.Browser!;
        await using var noScript = await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false, StorageState = await page.Context.StorageStateAsync(),
        });
        var plain = await noScript.NewPageAsync();
        await plain.GotoAsync($"{Base}/organizacijos/nauja");
        await Assertions.Expect(plain.Locator("#register-what")).ToBeHiddenAsync();
        await plain.Locator("details#register-more > summary").ClickAsync();
        await Assertions.Expect(plain.Locator("#register-what")).ToBeVisibleAsync();
        await Assertions.Expect(plain.Locator("#register-what li")).ToHaveCountAsync(3);
    }

    /// <summary>
    /// Design 8.15: a re-render must not close what the person opened. "Kaip tai veikia" opened while the page's island is
    /// still held back (as on a slow connection) is still open after WebAssembly has started and replaced the markup.
    /// </summary>
    [Fact]
    public async Task A_disclosure_opened_before_the_island_starts_stays_open_after_it_starts()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Skubanti Narė");
        var started = new TaskCompletionSource();
        await page.RouteAsync(IslandsBeforeStartTests.WebAssemblyFiles(), async route =>
        {
            await started.Task;
            await route.ContinueAsync();
        });

        await page.GotoAsync($"{Base}/organizacijos/nauja", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#org-name")).ToBeDisabledAsync();
        await page.Locator("details#register-more > summary").ClickAsync();
        await Assertions.Expect(page.Locator("#register-what")).ToBeVisibleAsync();

        started.SetResult();
        await Assertions.Expect(page.Locator("#org-name")).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator("details#register-more")).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(page.Locator("#register-what")).ToBeVisibleAsync();

        // And it still closes and opens by hand once the island runs.
        await page.Locator("details#register-more > summary").ClickAsync();
        await Assertions.Expect(page.Locator("#register-what")).ToBeHiddenAsync();
        member.ConsoleErrors.ShouldBeEmpty();
    }
    [Fact]
    public async Task Every_link_to_a_part_of_the_methodology_page_lands_on_a_part_that_exists()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT" });
        var page = await context.NewPageAsync();

        var anchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in new[] { "/", "/seimas/XVP-0412", "/projektai", "/darbotvarke", "/suvestines", "/organizacijos", "/o/pavyzdine-mokesciu-aiskumo-asociacija", "/prisijungti" })
        {
            await page.GotoAsync(Base + path);
            foreach (var href in await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('a[href*=\"/metodika#\"]')].map(a => a.getAttribute('href'))"))
            {
                anchors.Add(href[(href.IndexOf('#', StringComparison.Ordinal) + 1)..]);
            }
        }

        anchors.ShouldContain("riba", "the bill's page links \"Kas yra riba?\" there");
        anchors.ShouldContain("suvestiniu-taisykles", "the summaries page links its rules there");
        await page.GotoAsync($"{Base}/metodika");
        foreach (var anchor in anchors.Concat(["planuojamos-datos", "privatumas", "argumentai", "organizacijos"]))
        {
            (await page.Locator($"[id='{anchor}']").CountAsync()).ShouldBe(1, $"/metodika#{anchor} exists");
        }

        await Assertions.Expect(page.Locator("main")).Not.ToContainTextAsync("Method.Limits");
    }

    /// <summary>
    /// The screens design 8.15 changed, at 1280 px and 390 px, for the owner to look at (LT2_E2E_TEXT_SHOTS, default
    /// output/text-audit/shots): the registration form closed and open, a bill's page for a member with a side, the
    /// account page, the summaries, an organisation's profile and the methodology's new parts. Each width is also checked
    /// for sideways scrolling.
    /// </summary>
    [Fact]
    public async Task The_changed_screens_are_photographed_at_both_widths()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var shots = Environment.GetEnvironmentVariable("LT2_E2E_TEXT_SHOTS") is { Length: > 0 } dir ? dir : Path.Combine("output", "text-audit", "shots");
        Directory.CreateDirectory(shots);
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Ekrano Narė");

        // A side on XVP-0412, as a member takes it: the consent first, then the side.
        await page.GotoAsync($"{Base}/seimas/XVP-0412");
        var forSide = page.Locator("#side-for");
        await Assertions.Expect(forSide).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await forSide.ClickAsync();
        var consent = page.Locator("#consent-check");
        if (await consent.CountAsync() > 0)
        {
            await consent.CheckAsync();
            await page.Locator("[aria-labelledby=consent-title] button.btn--for").ClickAsync();
        }

        await Assertions.Expect(page.Locator("#my-side")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        foreach (var (width, height) in new[] { (1280, 900), (390, 844) })
        {
            await page.SetViewportSizeAsync(width, height);
            await ShootAsync(page, "/organizacijos/nauja", $"registration-closed-{width}.png");
            await page.Locator("details#register-more > summary").ClickAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(shots, $"registration-open-{width}.png"), FullPage = true });
            await ShootAsync(page, "/seimas/XVP-0412", $"bill-XVP-0412-member-with-side-{width}.png");
            await ShootAsync(page, "/sveiki", $"account-{width}.png");
            await ShootAsync(page, "/suvestines", $"summaries-{width}.png");
            await ShootAsync(page, "/o/pavyzdine-mokesciu-aiskumo-asociacija", $"organisation-profile-{width}.png");
            await page.Locator("details#label-meaning > summary").ClickAsync();
            await page.Locator("#about").ScreenshotAsync(new() { Path = Path.Combine(shots, $"organisation-label-meaning-open-{width}.png") });
            await ShootAsync(page, "/metodika", $"methodology-{width}.png");
            foreach (var part in new[] { "planuojamos-datos", "privatumas", "organizacijos", "suvestiniu-taisykles", "ribos-zmogui" })
            {
                await page.Locator($"[id='{part}']").ScreenshotAsync(new() { Path = Path.Combine(shots, $"methodology-{part}-{width}.png") });
            }
        }

        // The demonstration bill's numbers are what other tests count on: the side goes again.
        await page.SetViewportSizeAsync(1280, 900);
        await page.GotoAsync($"{Base}/seimas/XVP-0412");
        var withdraw = page.Locator("#my-side button");
        await Assertions.Expect(withdraw).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await withdraw.ClickAsync();
        await Assertions.Expect(page.Locator("#my-side")).ToHaveCountAsync(0, new() { Timeout = 30_000 });

        async Task ShootAsync(IPage at, string path, string file)
        {
            await at.GotoAsync(Base + path);
            await at.WaitForLoadStateAsync(LoadState.NetworkIdle);
            (await at.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).ShouldBeTrue($"{path} does not scroll sideways");
            await at.ScreenshotAsync(new() { Path = Path.Combine(shots, file), FullPage = true });
        }
    }
}
