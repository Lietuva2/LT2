using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.7, the reworked front page, against the running stack ("Done when"): a visitor sees the introduction and the
/// agenda and no form, and a coming bill's "už" and "prieš" are links to sign in that come back to the bill's page; a member
/// sees their own bills first and no introduction, takes a side in a row with one press (the first time after the consent,
/// asked in the row), changes and withdraws it there, and the row then shows the bill's live numbers; with JavaScript off
/// the public page is complete and nothing of a member's own is in it; the page's HTML is the same for two members; no
/// sideways scroll at 390 px; at 1280 px the first screen shows the agenda's heading; at 360, 390 and 1280 px a row's
/// organisation names start at one place and the side buttons end where the bar ends ("The row lines up"). Screenshots for the owner go to
/// LT2_E2E_START_SHOTS (default output/start-page/built).
/// </summary>
public sealed partial class StartPageTests : E2ETest
{
    private static string Base => E2ESteps.Base;

    private static string Shots => Environment.GetEnvironmentVariable("LT2_E2E_START_SHOTS") is { Length: > 0 } dir ? dir : Path.Combine("output", "start-page", "built");

    /// <summary>The coming rows that offer the sides (an open record).</summary>
    private const string AskRows = "#home-coming li.tl-item:has(.tl-ask)";

    [Theory]
    [InlineData(1280, 900)]
    [InlineData(390, 844)]
    public async Task A_visitor_sees_the_introduction_and_the_agenda_and_no_form(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Base + "/");

        await Assertions.Expect(page.Locator("#home-head")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.Locator(".intro-steps > li")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator("#home-register")).ToHaveAttributeAsync("href", "/prisijungti?registruotis=true");
        await Assertions.Expect(page.Locator("main form")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#my-agenda")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#h-home-coming")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#not-a-poll")).ToBeVisibleAsync();

        // Design 8.16: the agenda is on a sheet. Design 8.7 (owner, 2026-11-16): no block about organisations.
        await Assertions.Expect(page.Locator("#home-coming")).ToHaveClassAsync(new Regex(@"\bsheet\b"));
        await Assertions.Expect(page.Locator("main a[href='/organizacijos/nauja']")).ToHaveCountAsync(0);

        // Design 8.7 (owner, 2026-11-16): the trial note has the same left and right edges as the invented-data notice.
        var invented = (await page.Locator("#invented-data").BoundingBoxAsync())!;
        var trial = (await page.Locator("#home-sign-in .note").BoundingBoxAsync())!;
        Math.Abs(trial.X - invented.X).ShouldBeLessThanOrEqualTo(1, "the two notes start at the same place");
        Math.Abs(trial.X + trial.Width - (invented.X + invented.Width)).ShouldBeLessThanOrEqualTo(1, "the two notes end at the same place");
        await E2ESteps.NoSidewaysScrollAsync(page, $"/ at {width}");
        if (width == 1280)
        {
            var heading = await page.Locator("#h-home-coming").BoundingBoxAsync();
            (heading!.Y + heading.Height).ShouldBeLessThanOrEqualTo(height, "the agenda's heading is on the first screen");
        }

        (await page.EvaluateAsync<bool>("() => document.querySelector('.home-how').compareDocumentPosition(document.querySelector('#home-coming')) & Node.DOCUMENT_POSITION_FOLLOWING"))
            .ShouldBeTrue("the steps come before the agenda, on a phone too");
        await SidesFitSideBySideAsync(page.Locator(AskRows).First);
        Directory.CreateDirectory(Shots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, $"visitor-{width}.png"), FullPage = true });
    }

    [Theory]
    [InlineData(360, 740)]
    [InlineData(390, 844)]
    [InlineData(1280, 900)]
    public async Task A_row_lines_up_at_every_width(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Base + "/");

        // The demonstration bill: several organisations with a statement, the numbers and the sides to take.
        var row = page.Locator("#home-coming li.tl-item[id$='-XVP-0412']:has(.tl-ask)").First;
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        (await row.Locator(".org-item__name").CountAsync()).ShouldBeGreaterThan(1, "the demonstration bill names several organisations");
        (await row.Locator(".tl-item__side .nowrap").CountAsync()).ShouldBeGreaterThan(1, "the demonstration bill has numbers, in parts");
        await RowLinesUpAsync(row, $"XVP-0412 at {width}");
        await E2ESteps.NoSidewaysScrollAsync(page, $"/ at {width}");
    }

    [Fact]
    public async Task A_visitors_sides_are_links_to_sign_in_that_come_back_to_the_bill()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });

        // No script needed: the links work with JavaScript off.
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Base + "/");
        var row = page.Locator(AskRows).First;
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        var number = (await row.Locator("a.tl-item__title").GetAttributeAsync("href"))!["/seimas/".Length..];
        var expected = "/prisijungti#tikslas=" + Uri.EscapeDataString("/seimas/" + number);
        await Assertions.Expect(row.Locator(".tl-ask")).ToContainTextAsync("Prašau balsuoti");
        (await page.Locator(AskRows).AllInnerTextsAsync()).ShouldAllBe(t => !t.Contains("Niekas nenurodė"), "no \"nobody yet\" beside the sides (owner, 2026-11-19)");
        foreach (var (side, word) in new[] { ("for", "už"), ("against", "prieš") })
        {
            var link = row.Locator($".tl-ask a.btn--{side}");
            await Assertions.Expect(link).ToHaveAttributeAsync("href", expected);
            await Assertions.Expect(link).ToHaveTextAsync(word);
            await Assertions.Expect(link).ToHaveAttributeAsync("aria-label", $"Prašau balsuoti {word} projektą {number}");
        }

        await Assertions.Expect(page.Locator("#home-coming .tl-ask button")).ToHaveCountAsync(0, new() { Timeout = 1000 });
        await row.Locator(".tl-ask a.btn--for").ClickAsync();
        await page.WaitForURLAsync(new Regex("/prisijungti"), new() { Timeout = E2ESteps.Slow });
        new Uri(page.Url).Fragment.ShouldBe("#tikslas=" + Uri.EscapeDataString("/seimas/" + number), "signing in comes back to the bill's page");
    }

    [Fact]
    public async Task A_member_takes_a_side_in_a_row_with_one_press_after_the_consent_changes_it_and_withdraws_it()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Eilutės Narė");
        await page.GotoAsync(Base + "/");
        var row = page.Locator(AskRows).First;
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        var number = (await row.Locator("a.tl-item__title").GetAttributeAsync("href"))!["/seimas/".Length..];
        var place = (await row.GetAttributeAsync("id"))!["agenda-".Length..];
        var forButton = page.Locator($"#home-for-{place}");
        var againstButton = page.Locator($"#home-against-{place}");
        await Assertions.Expect(forButton).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(forButton).ToHaveAttributeAsync("aria-label", $"Prašau balsuoti už projektą {number}");
        try
        {
            // The first side ever: the consent is asked in the row, across its width; nothing is saved before it.
            await forButton.ClickAsync();
            var consent = page.Locator($"#home-consent-{place}");
            await Assertions.Expect(consent).ToBeVisibleAsync();
            await Assertions.Expect(consent).ToContainTextAsync("Prieš išsaugant jūsų pasirinkimą");
            (await MySideAsync(page, number)).ShouldBeNull("nothing saved before the consent");
            var consentBox = await consent.BoundingBoxAsync();
            var rowBox = await row.BoundingBoxAsync();
            ((double)consentBox!.Width).ShouldBeGreaterThan(rowBox!.Width * 0.9, "across the row's whole width");
            await page.Locator($"#home-{place}-consent-cancel").ClickAsync();
            await Assertions.Expect(consent).ToHaveCountAsync(0);
            (await MySideAsync(page, number)).ShouldBeNull("\"Atšaukti\" leaves the row as it was");
            await Assertions.Expect(forButton).ToHaveAttributeAsync("aria-pressed", "false");

            await forButton.ClickAsync();
            await page.Locator($"#home-{place}-consent-check").CheckAsync();
            await page.Locator($"#home-{place}-consent-submit").ClickAsync();
            await Assertions.Expect(forButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = E2ESteps.Slow });
            (await MySideAsync(page, number)).ShouldBe("for");

            // The row's numbers are the bill's live ones, the member's side among them; a first side follows the bill
            // (design 8.11.2), and the mark under the title and the panel show it without a reload.
            await Assertions.Expect(page.Locator($"#home-counts-{place}")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            var shown = Regex.Replace(await page.Locator($"#home-counts-{place}").InnerTextAsync(), @"[\s\u00A0\u202F]", "");
            shown.ShouldStartWith("už" + await LiveForAsync(page, number) + "(", Case.Sensitive, "the bill's live numbers, the member's side among them");
            await Assertions.Expect(row.Locator(".mine")).ToContainTextAsync("Sekate");
            await Assertions.Expect(row.Locator(".mine .choice--for")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator($"#my-agenda-{number}")).ToBeVisibleAsync();
            // Owner, 2026-11-19: "Sekate" lighting up is enough: no line says the bill is followed now.
            (await row.InnerTextAsync()).ShouldNotContain("Dabar sekate");

            // Owner, 2026-11-19 ("minimise non-essential texts"): no link to the arguments (the title leads to the bill), and no
            // line on taking the side back, after the press either; the pressed button's title says it.
            await Assertions.Expect(row.Locator(".tl-item__side a")).ToHaveCountAsync(0);
            await Assertions.Expect(forButton).ToHaveAttributeAsync("title", "Paspaudę dar kartą, atsiimsite.");
            (await row.InnerTextAsync()).ShouldNotContain("Paspaudę dar kartą");
            (await row.InnerTextAsync()).ShouldNotContain("Argumentai ir pozicijos");

            // Owner, 2026-11-17 ("followed only"): unfollowed, the bill leaves "Jūsų projektai"; the side stays in its row.
            var (unfollowed, unfollowedBody) = await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{number}/follow");
            unfollowed.ShouldBe(204, unfollowedBody);
            await page.ReloadAsync();
            await Assertions.Expect(row.Locator(".mine .choice--for")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.Locator($"#my-agenda-{number}")).ToHaveCountAsync(0);
            var (refollowed, refollowedBody) = await E2ESteps.ApiAsync(page, "PUT", $"/api/v1/bills/{number}/follow");
            refollowed.ShouldBe(204, refollowedBody);
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator($"#my-agenda-{number}")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });

            // Reloaded, the row shows the count they are part of (design 8.7, "Cached"): the side marked the page's copy
            // out of date; within the copy's first two seconds the island reads the live numbers instead. The server's
            // own page shows it too once the copy may be replaced.
            var live = await LiveForAsync(page, number);
            await page.ReloadAsync();
            await Assertions.Expect(forButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = E2ESteps.Slow });
            // The number as the page writes it, with its thousands separator (the demonstration bill, first by activity since
            // 2026-11-18, has thousands): compared without the spaces.
            await Assertions.Expect(page.Locator($"#home-counts-{place}")).ToBeVisibleAsync();
            Regex.Replace(await page.Locator($"#home-counts-{place}").InnerTextAsync(), @"[\s\u00A0\u202F]", "")
                .ShouldStartWith("už" + live.ToString(System.Globalization.CultureInfo.InvariantCulture) + "(", Case.Sensitive);
            (await ServerCountAsync(page, place, number)).ShouldBe(live, "the server's page after a side, without waiting for the minute");

            // The other button changes it, with one press: no consent again.
            await againstButton.ClickAsync();
            await Assertions.Expect(againstButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(forButton).ToHaveAttributeAsync("aria-pressed", "false");
            await Assertions.Expect(page.Locator($"#home-consent-{place}")).ToHaveCountAsync(0);
            (await MySideAsync(page, number)).ShouldBe("against");
            await Assertions.Expect(row.Locator(".mine .choice--against")).ToBeVisibleAsync();

            // Pressing the pressed button again takes the side back (owner, 2026-11-18: no "Atsiimti"); the follow stays.
            await Assertions.Expect(againstButton).ToHaveAttributeAsync("title", "Paspaudę dar kartą, atsiimsite.");
            await Assertions.Expect(page.Locator($"#home-withdraw-{place}")).ToHaveCountAsync(0);
            await againstButton.ClickAsync();
            await Assertions.Expect(againstButton).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(row.Locator(".tl-ask button[title]")).ToHaveCountAsync(0);
            (await MySideAsync(page, number)).ShouldBeNull();
            await Assertions.Expect(row.Locator(".mine")).ToContainTextAsync("Sekate");
            await E2ESteps.NoSidewaysScrollAsync(page, "/ after taking a side in a row");
        }
        finally
        {
            await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{number}/record/position");
            await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{number}/follow");
        }
    }

    [Fact]
    public async Task A_member_sees_their_own_bills_first_marked_in_the_rows_and_no_introduction()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Pradžios Narė");

        // The first planned bill with sides: the member asks for "už" on it, which also follows it (design 8.11.2).
        await page.GotoAsync(Base + "/");
        var first = page.Locator(AskRows).First;
        await Assertions.Expect(first).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        var number = (await first.Locator("a.tl-item__title").GetAttributeAsync("href"))!["/seimas/".Length..];
        var place = (await first.GetAttributeAsync("id"))!["agenda-".Length..];
        var other = page.Locator(AskRows).Nth(1);
        var otherPlace = (await other.GetAttributeAsync("id"))!["agenda-".Length..];
        (await E2ESteps.ApiAsync(page, "PUT", "/api/v1/me/consents/positions", new { textVersion = "2026-11-18" })).Status.ShouldBeOneOf(204, 200);
        (await E2ESteps.ApiAsync(page, "PUT", $"/api/v1/bills/{number}/record/position", new { side = "for" })).Status.ShouldBe(204);
        try
        {
            // For the owner's screenshots, one more row shows the consent open: the answer is made to say the consent is
            // missing (only in this browser), so pressing "prieš" there asks for it; nothing is saved.
            await page.RouteAsync("**/api/v1/me/bills?*", async route =>
            {
                var response = await route.FetchAsync();
                var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.TextAsync())!;
                json["hasPositionConsent"] = JsonSerializer.SerializeToElement(false);
                await route.FulfillAsync(new() { Response = response, Body = JsonSerializer.Serialize(json) });
            });
            foreach (var (width, height) in new[] { (1280, 900), (390, 844), (360, 740) })
            {
                await page.SetViewportSizeAsync(width, height);
                await page.GotoAsync(Base + "/");
                await Assertions.Expect(page.Locator("#home-title")).ToHaveTextAsync("Sveiki sugrįžę");
                await Assertions.Expect(page.Locator("#home-head")).ToHaveCountAsync(0);
                await Assertions.Expect(page.Locator(".intro-steps")).ToHaveCountAsync(0);
                // Owner, 2026-11-17: no "Kaip tai veikia" and "Mano paskyra" links in the member's head; both are reachable elsewhere.
                await Assertions.Expect(page.Locator("#home-welcome a")).ToHaveCountAsync(0);

                var mine = page.Locator($"#my-agenda-{number}");
                await Assertions.Expect(mine).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
                await Assertions.Expect(mine.Locator(".choice--for")).ToHaveTextAsync("už");
                // Owner, 2026-11-19: no share link in the panel (the bill's page keeps it).
                await Assertions.Expect(mine.Locator("button")).ToHaveCountAsync(0);
                var row = page.Locator($"#agenda-{place}");
                await Assertions.Expect(row.Locator(".mine")).ToContainTextAsync("Sekate", new() { Timeout = E2ESteps.Slow });
                await Assertions.Expect(page.Locator($"#home-for-{place}")).ToHaveAttributeAsync("aria-pressed", "true");
                (await row.InnerTextAsync()).ShouldNotContain("Argumentai ir pozicijos");
                await SidesFitSideBySideAsync(row);
                await RowLinesUpAsync(row, $"a member's row at {width}");

                // The page's agenda may be a minute old and still say nobody took a side: never beside the member's own side.
                await Assertions.Expect(row.Locator($"#home-nobody-{place}")).ToHaveCountAsync(0);
                (await page.EvaluateAsync<bool>("() => document.querySelector('#my-agenda').compareDocumentPosition(document.querySelector('#home-coming')) & Node.DOCUMENT_POSITION_FOLLOWING"))
                    .ShouldBeTrue("the member's own bills come first");
                if (width == 1280)
                {
                    var heading = await page.Locator("#h-home-coming").BoundingBoxAsync();
                    (heading!.Y + heading.Height).ShouldBeLessThanOrEqualTo(height, "the agenda's heading is on the first screen");
                }

                await page.Locator($"#home-against-{otherPlace}").ClickAsync();
                await Assertions.Expect(page.Locator($"#home-consent-{otherPlace}")).ToBeVisibleAsync();
                await E2ESteps.NoSidewaysScrollAsync(page, $"/ for a member at {width}, with the consent open in a row");
                Directory.CreateDirectory(Shots);
                await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, $"member-{width}.png"), FullPage = true });
            }

            // The dark theme, once, as the owner looks at it.
            await page.SetViewportSizeAsync(1280, 900);
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
            await page.GotoAsync(Base + "/");
            await Assertions.Expect(page.Locator($"#my-agenda-{number}")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.Locator($"#home-for-{place}")).ToHaveAttributeAsync("aria-pressed", "true");
            await page.Locator($"#home-against-{otherPlace}").ClickAsync();
            await Assertions.Expect(page.Locator($"#home-consent-{otherPlace}")).ToBeVisibleAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, "member-1280-dark.png"), FullPage = true });
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
            await page.UnrouteAllAsync();

            // With JavaScript off the member's page is the public page: nothing of their own is in it, and the sides are
            // disabled, the same for every member.
            await using var noScript = await page.Context.Browser!.NewContextAsync(new()
            {
                IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false, StorageState = await page.Context.StorageStateAsync(),
            });
            var plain = await noScript.NewPageAsync();
            await plain.GotoAsync(Base + "/");
            await Assertions.Expect(plain.Locator("#home-title")).ToHaveTextAsync("Sveiki sugrįžę");
            await Assertions.Expect(plain.Locator("#home-coming .tl-item").First).ToBeVisibleAsync();
            await Assertions.Expect(plain.Locator("#my-agenda")).ToHaveCountAsync(0);
            await Assertions.Expect(plain.Locator(".mine")).ToHaveCountAsync(0);
            (await plain.Locator("main").InnerTextAsync()).ShouldNotContain("Jūs prašote balsuoti");
            await Assertions.Expect(plain.Locator($"#home-for-{place}")).ToBeDisabledAsync();
            await Assertions.Expect(plain.Locator($"#home-for-{place}")).ToHaveAttributeAsync("aria-pressed", "false");
            await Assertions.Expect(plain.Locator($"#home-for-{place}")).Not.ToHaveAttributeAsync("title", new Regex("."));
        }
        finally
        {
            // The demonstration bill's numbers are what other tests count on: the side and the follow go again.
            await page.UnrouteAllAsync();
            await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{number}/record/position");
            await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{number}/follow");
        }
    }

    /// <summary>
    /// Owner, 2026-11-19: the agenda page's rows are the front page's (one component). A member takes a side in a row of
    /// <c>/darbotvarke</c> after the consent asked in the row, changes it and takes it back by pressing it again; a day grown
    /// in place ("Rodyti daugiau", owner 2026-11-19) brings rows whose buttons work too. A visitor's sides there are links to sign in.
    /// </summary>
    [Fact]
    public async Task On_the_agenda_page_a_member_takes_a_side_in_a_row_and_in_a_row_of_a_day_grown_in_place()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = member.Page;
        await PasskeyTests.RegisterAsync(page, "Darbotvarkės Narė");
        await page.GotoAsync(Base + "/darbotvarke");
        const string Rows = "#agenda-coming li.tl-item:has(.tl-ask)";
        var row = page.Locator(Rows).First;
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        var number = (await row.Locator("a.tl-item__title").GetAttributeAsync("href"))!["/seimas/".Length..];
        var place = (await row.GetAttributeAsync("id"))!["agenda-".Length..];
        var forButton = page.Locator($"#home-for-{place}");
        var againstButton = page.Locator($"#home-against-{place}");
        string? addedNumber = null;
        try
        {
            await Assertions.Expect(forButton).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.Locator("#my-agenda")).ToHaveCountAsync(0, new() { Timeout = 1000 });
            await Assertions.Expect(page.Locator("#home-title")).ToHaveCountAsync(0);

            // The first side ever: the consent in the row; then the side, changed, and taken back by pressing it again.
            await forButton.ClickAsync();
            await Assertions.Expect(page.Locator($"#home-consent-{place}")).ToBeVisibleAsync();
            (await MySideAsync(page, number)).ShouldBeNull("nothing saved before the consent");
            await page.Locator($"#home-{place}-consent-check").CheckAsync();
            await page.Locator($"#home-{place}-consent-submit").ClickAsync();
            await Assertions.Expect(forButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = E2ESteps.Slow });
            (await MySideAsync(page, number)).ShouldBe("for");
            await Assertions.Expect(page.Locator($"#home-counts-{place}")).ToBeVisibleAsync();
            await Assertions.Expect(row.Locator(".mine")).ToContainTextAsync("Sekate");
            await Assertions.Expect(row.Locator(".tl-item__side a")).ToHaveCountAsync(0);
            (await row.InnerTextAsync()).ShouldNotContain("Paspaudę dar kartą");
            await againstButton.ClickAsync();
            await Assertions.Expect(againstButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = E2ESteps.Slow });
            (await MySideAsync(page, number)).ShouldBe("against");
            await againstButton.ClickAsync();
            await Assertions.Expect(againstButton).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = E2ESteps.Slow });
            (await MySideAsync(page, number)).ShouldBeNull();
            await forButton.ClickAsync();
            await Assertions.Expect(forButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = E2ESteps.Slow });

            // A day grown in place by "Rodyti daugiau": the rows added carry islands that start and take a side, and the rows
            // already shown keep theirs (each island is keyed by its row). The link lands on the first row added.
            var more = page.Locator("#agenda-coming section.tl-day a.show-more__link").First;
            await Assertions.Expect(more).ToHaveCountAsync(1);
            var landing = (await more.GetAttributeAsync("href"))!.Split('#')[1];
            var day = page.Locator("#diena-" + Regex.Match(landing, @"^agenda-(\d{4}-\d{2}-\d{2})-").Groups[1].Value);
            await Assertions.Expect(day.Locator("li.tl-item")).ToHaveCountAsync(10);
            await more.ClickAsync();
            await page.WaitForURLAsync(new Regex("diena=.*:20#agenda-"), new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.Locator("#" + landing)).ToBeInViewportAsync(new() { Timeout = E2ESteps.Slow });
            var added = day.Locator("li.tl-item:has(.tl-ask)").Last;
            await Assertions.Expect(added).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            addedNumber = (await added.Locator("a.tl-item__title").GetAttributeAsync("href"))!["/seimas/".Length..];
            var addedPlace = (await added.GetAttributeAsync("id"))!["agenda-".Length..];
            var addedFor = page.Locator($"#home-for-{addedPlace}");
            await Assertions.Expect(addedFor).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await addedFor.ClickAsync();
            await Assertions.Expect(addedFor).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = E2ESteps.Slow });
            (await MySideAsync(page, addedNumber)).ShouldBe("for", "the consent is given: one press");
            await Assertions.Expect(page.Locator($"#home-consent-{addedPlace}")).ToHaveCountAsync(0);
            await addedFor.ClickAsync();
            await Assertions.Expect(addedFor).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = E2ESteps.Slow });
            (await MySideAsync(page, addedNumber)).ShouldBeNull();
            await Assertions.Expect(forButton).ToHaveAttributeAsync("aria-pressed", "true");
            (await MySideAsync(page, number)).ShouldBe("for");
            member.ConsoleErrors.ShouldBeEmpty();
        }
        finally
        {
            foreach (var bill in new[] { number, addedNumber }.OfType<string>())
            {
                await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{bill}/record/position");
                await E2ESteps.ApiAsync(page, "DELETE", $"/api/v1/bills/{bill}/follow");
            }
        }

        // A visitor's sides on the agenda page are links to sign in that come back to the bill's page, without script.
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false });
        var visitor = await context.NewPageAsync();
        await visitor.GotoAsync(Base + "/darbotvarke");
        var visitorRow = visitor.Locator($"#agenda-{place}");
        await Assertions.Expect(visitorRow.Locator(".tl-ask a.btn--for")).ToHaveAttributeAsync("href", "/prisijungti#tikslas=" + Uri.EscapeDataString("/seimas/" + number));
        await Assertions.Expect(visitor.Locator("#agenda-coming .tl-ask button")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_pages_html_is_the_same_for_two_members()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var first = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await PasskeyTests.RegisterAsync(first.Page, "Pirmoji Pradžios");
        await using var second = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await PasskeyTests.RegisterAsync(second.Page, "Antrasis Pradžios");

        // The first one has a side and a follow, the second none: still the same page.
        await first.Page.GotoAsync(Base + "/");
        var row = first.Page.Locator(AskRows).First;
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        var number = (await row.Locator("a.tl-item__title").GetAttributeAsync("href"))!["/seimas/".Length..];
        (await E2ESteps.ApiAsync(first.Page, "PUT", "/api/v1/me/consents/positions", new { textVersion = "2026-11-18" })).Status.ShouldBeOneOf(204, 200);
        (await E2ESteps.ApiAsync(first.Page, "PUT", $"/api/v1/bills/{number}/record/position", new { side = "for" })).Status.ShouldBe(204);
        try
        {
            // Read one after the other. The side just taken marks the front page's kept copy out of date, and the copy is read
            // again on the first request once it is 2 s old (design 8.7, FrontPageFreshness), or when its minute ends: a pair
            // read across that moment holds two copies of the agenda (pre-merge run of 2026-11-17). So the pair is read again
            // until both come from one copy; a difference between the members themselves would never go away and still fails.
            string firstHtml, secondHtml;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (true)
            {
                firstHtml = System.Net.WebUtility.HtmlDecode(await (await first.Page.Context.APIRequest.GetAsync(Base + "/")).TextAsync());
                secondHtml = System.Net.WebUtility.HtmlDecode(await (await second.Page.Context.APIRequest.GetAsync(Base + "/")).TextAsync());
                if (Frameless(firstHtml) == Frameless(secondHtml) || DateTime.UtcNow > deadline)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            firstHtml.ShouldContain("Sveiki sugrįžę", customMessage: "the members' version of the page");
            firstHtml.ShouldContain($"id=\"home-for-{(await row.GetAttributeAsync("id"))!["agenda-".Length..]}\"");
            Frameless(firstHtml).ShouldBe(Frameless(secondHtml), "the same page for every member, apart from the frame");
            firstHtml.ShouldNotContain("aria-pressed=\"true\"");
            firstHtml.ShouldNotContain("Pirmoji Pradžios");
            secondHtml.ShouldNotContain("Antrasis Pradžios");
        }
        finally
        {
            await E2ESteps.ApiAsync(first.Page, "DELETE", $"/api/v1/bills/{number}/record/position");
            await E2ESteps.ApiAsync(first.Page, "DELETE", $"/api/v1/bills/{number}/follow");
        }
    }

    [Fact]
    public async Task With_javascript_off_the_public_page_is_complete()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync(Base + "/");

        await Assertions.Expect(page.Locator("#home-head")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".intro-steps > li")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator("#home-coming .tl-item").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#home-coming .tl-item a.btn").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#not-a-poll")).ToBeVisibleAsync();
    }

    /// <summary>"už" and "prieš" stand side by side in the row, both whole.</summary>
    private static async Task SidesFitSideBySideAsync(ILocator row)
    {
        var sides = row.Locator(".tl-ask__sides > *");
        (await sides.CountAsync()).ShouldBe(2);
        var a = (await sides.Nth(0).BoundingBoxAsync())!;
        var b = (await sides.Nth(1).BoundingBoxAsync())!;
        ((double)Math.Abs(a.Y - b.Y)).ShouldBeLessThan(2, "on one line");
        (await sides.EvaluateAllAsync<bool>("els => els.every(e => e.scrollWidth <= e.clientWidth + 1)")).ShouldBeTrue("the words fit in the buttons");
    }

    /// <summary>
    /// Design 8.7, "The row lines up": every organisation's name starts at the same place, its stance before it; the two side
    /// buttons are exactly as wide as the bar; each part of a line of numbers stays on one line.
    /// </summary>
    private static async Task RowLinesUpAsync(ILocator row, string where)
    {
        var names = row.Locator(".org-item__name");
        var lefts = new List<float>();
        for (var i = 0; i < await names.CountAsync(); i++)
        {
            var name = names.Nth(i);
            lefts.Add((await name.BoundingBoxAsync())!.X);
            (await name.EvaluateAsync<string>("e => e.previousElementSibling?.className ?? ''")).ShouldStartWith("choice", Case.Sensitive, $"the stance before the name: {where}");
        }

        if (lefts.Count > 0)
        {
            ((double)(lefts.Max() - lefts.Min())).ShouldBeLessThanOrEqualTo(1, $"every organisation's name starts at the same place: {where}");
        }

        var sides = (await row.Locator(".tl-ask__sides").BoundingBoxAsync())!;
        var bar = (await row.Locator(".splitbar").First.BoundingBoxAsync())!;
        ((double)Math.Abs(sides.X + sides.Width - (bar.X + bar.Width))).ShouldBeLessThanOrEqualTo(1, $"the buttons end where the bar ends: {where}");
        ((double)Math.Abs(sides.X - bar.X)).ShouldBeLessThanOrEqualTo(1, $"the buttons start where the bar starts: {where}");
        var parts = row.Locator(".tl-item__side .nowrap");

        // One line is less than two font sizes high; a part broken over two lines is more.
        (await parts.EvaluateAllAsync<string[]>("els => els.filter(e => e.getBoundingClientRect().height >= 2 * parseFloat(getComputedStyle(e).fontSize)).map(e => e.textContent)"))
            .ShouldBeEmpty($"a line of numbers breaks only between its parts: {where}");
    }

    /// <summary>The member's own side on the bill, from their own answer.</summary>
    private static async Task<string?> MySideAsync(IPage page, string number)
    {
        var (status, body) = await E2ESteps.ApiAsync(page, "GET", $"/api/v1/bills/{number}/record/mine");
        status.ShouldBe(200, body);
        var side = JsonDocument.Parse(body).RootElement.GetProperty("side");
        return side.ValueKind == JsonValueKind.Null ? null : side.GetString();
    }

    /// <summary>The bill's live "for" count from its public answer.</summary>
    private static async Task<int> LiveForAsync(IPage page, string number)
    {
        var (status, body) = await E2ESteps.ApiAsync(page, "GET", $"/api/v1/bills/{number}/record");
        status.ShouldBe(200, body);
        var count = JsonDocument.Parse(body).RootElement.GetProperty("counts").GetProperty("for").GetInt32();
        count.ShouldBeGreaterThan(0, "the member's own side is in it");
        return count;
    }

    /// <summary>
    /// The "for" count the server renders in a row (no script), read until it may have been rebuilt: a copy marked out of
    /// date is replaced by the first request once it is two seconds old, so this asks for at most a few seconds.
    /// </summary>
    private static async Task<int?> ServerCountAsync(IPage page, string place, string number)
    {
        var pattern = new Regex("id=\"home-counts-" + Regex.Escape(place) + "\"[^>]*>\\s*<span class=\"nowrap\"><span class=\"choice choice--for\">[^<]*</span>\\s*([\\d\\u00A0 ]+?)\\s*\\(");
        int? last = null;
        var until = DateTime.UtcNow.AddSeconds(10);
        var live = await LiveForAsync(page, number);
        while (DateTime.UtcNow < until)
        {
            var html = System.Net.WebUtility.HtmlDecode(await (await page.Context.APIRequest.GetAsync(Base + "/")).TextAsync());
            var match = pattern.Match(html);
            last = match.Success ? int.Parse(Regex.Replace(match.Groups[1].Value, @"\D", ""), System.Globalization.CultureInfo.InvariantCulture) : null;
            if (last == live)
            {
                break;
            }

            // The copy may still be younger than two seconds: ask again shortly (a poll, not a fixed wait for a result).
            await page.WaitForTimeoutAsync(300);
        }

        return last;
    }

    /// <summary>The page without what the frame varies per request and per session: antiforgery tokens and Blazor's markers.</summary>
    private static string Frameless(string html)
    {
        html = AntiforgeryValue().Replace(html, "value=\"…\"");
        html = BlazorState().Replace(html, "<!--Blazor-state-->");
        return PrerenderId().Replace(html, "\"prerenderId\":\"…\"");
    }

    [GeneratedRegex("value=\"[A-Za-z0-9_\\-]{40,}\"")]
    private static partial Regex AntiforgeryValue();

    [GeneratedRegex("<!--Blazor-(WebAssembly-)?Component-State:[^>]*-->")]
    private static partial Regex BlazorState();

    [GeneratedRegex("\"prerenderId\":\"[^\"]*\"")]
    private static partial Regex PrerenderId();
}
