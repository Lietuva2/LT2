using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// The main path of appeals through real browsers against the stack (design 8.1, 10), at 1280 px and 390 px: a member
/// opens an appeal, a moderator publishes it in a staff-passkey session, a second member writes an argument before
/// taking any side (choosing its side), then takes a side (giving the consent first) and marks it "Man svarbu"; the
/// initiator writes one for the other side. And the public appeal page once with JavaScript off.
/// Each width uses its own bill; the moderator hides the appeal at the end so the bill is free for the next run.
/// </summary>
public sealed class AppealFlowTests(AppealFlowTests.ModeratorFixture moderator) : IClassFixture<AppealFlowTests.ModeratorFixture>
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Theory]
    [InlineData(1280, 800, "XVP-0533", "XVP-0618")]
    [InlineData(390, 844, "XVP-0618", "XVP-0533")]
    public async Task A_member_opens_an_appeal_a_moderator_publishes_it_and_another_takes_a_side_writes_and_endorses(int width, int height, string preferred, string other)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var mod = await moderator.PageAsync();
        var bill = await FreeBillAsync(mod, preferred, other);
        Assert.SkipWhen(bill is null, Taken(preferred, other));

        // 1. The initiator opens the appeal on the bill's appeal page: a neutral description and a threshold, nothing else.
        await using var initiator = await PasskeyBrowser.StartAsync("native", width, height);
        var a = initiator.Page;
        await PasskeyTests.RegisterAsync(a, "Rūta Mockutė");

        // Before WebAssembly has started (held back here, as on a slow connection) the form cannot be used: a click on
        // the button and typing do nothing, nothing is sent and the page does not reload. Then it works as usual.
        var started = new TaskCompletionSource();
        await a.RouteAsync(IslandsBeforeStartTests.WebAssemblyFiles(), async route =>
        {
            await started.Task;
            await route.ContinueAsync();
        });
        var opens = 0;
        a.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.EndsWith($"/bills/{bill}/appeal", StringComparison.Ordinal))
            {
                opens++;
            }
        };
        await a.GotoAsync($"{Base}/seimas/{bill}/kreipimasis", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var navigations = 0;
        a.FrameNavigated += (_, frame) =>
        {
            if (frame == a.MainFrame)
            {
                navigations++;
            }
        };
        await Assertions.Expect(a.Locator("#no-appeal")).ToBeVisibleAsync();
        var submit = a.GetByRole(AriaRole.Button, new() { Name = "Pateikti moderatoriui" });
        await Assertions.Expect(a.Locator("#open-description")).ToBeDisabledAsync();
        await Assertions.Expect(submit).ToBeDisabledAsync();
        await a.Locator("#open-description").ClickAsync(new() { Force = true });
        await a.Keyboard.TypeAsync("Parašyta per anksti.");
        await a.Keyboard.PressAsync("Enter");
        await submit.ClickAsync(new() { Force = true });
        await a.WaitForTimeoutAsync(1_000);
        navigations.ShouldBe(0, "the page did not reload");
        opens.ShouldBe(0, "nothing was sent");
        started.SetResult();
        await a.Locator("#open-description:not([disabled])").WaitForAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(a.Locator("#open-description")).ToHaveValueAsync("");
        await Assertions.Expect(a.Locator("#open-appeal input[type=radio]")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        await Assertions.Expect(a.Locator("#open-appeal input[type=checkbox]")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        await a.Locator("#open-description").FillAsync($"Projektas keičia mokesčių tvarką: įvedamas naujas tarifas didelėms pajamoms. {Marker}");
        await a.Locator("#open-threshold").FillAsync("100");
        await a.GetByRole(AriaRole.Button, new() { Name = "Pateikti moderatoriui" }).ClickAsync();
        await Assertions.Expect(a.Locator("#pending-note")).ToContainTextAsync("Kreipimasis laukia moderatoriaus", new() { Timeout = 30_000 });
        opens.ShouldBe(1);
        await IslandsBeforeStartTests.NothingUsableBeforeStartAsync(a.Context, $"/seimas/{bill}/kreipimasis");
        await NoSidewaysScrollAsync(a);
        await a.ScreenshotAsync(new() { Path = Shot($"appeal-pending-{width}.png"), FullPage = true });

        // 2. The moderator publishes it from the queue.
        await mod.GotoAsync($"{Base}/moderavimas");
        var item = mod.Locator("article").Filter(new() { HasText = bill }).Filter(new() { HasText = "Projektas keičia mokesčių tvarką" });
        await Assertions.Expect(item).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await ActAsync(item, "publish");
        await Assertions.Expect(item.GetByText("Atlikta: paskelbta.")).ToBeVisibleAsync();

        // 3. A second member, before taking any side, writes an argument: nothing is preselected, so they must choose.
        // A long display name: under an argument the actions still share one line (the name goes above them).
        await using var second = await PasskeyBrowser.StartAsync("native", width, height);
        var b = second.Page;
        await PasskeyTests.RegisterAsync(b, "Giedrė Konstancija Vaitkevičiūtė-Žemaitienė");
        await b.GotoAsync($"{Base}/seimas/{bill}/kreipimasis");
        await Assertions.Expect(b.Locator("#count-for .figure__num")).ToHaveTextAsync("0");
        var add = b.Locator("#add-reason");
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();
        await Assertions.Expect(b.Locator("#write-reason")).ToContainTextAsync("nepriklausomai nuo to, ko prašote patys");
        // The side is the two-part switch "Už | Prieš" (owner, 2026-11-13): with no side of one's own, neither half.
        await Assertions.Expect(b.Locator("#reason-side .switch__half")).ToHaveTextAsync(["Už", "Prieš"]);
        await Assertions.Expect(b.Locator("#reason-side-for")).Not.ToBeCheckedAsync();
        await Assertions.Expect(b.Locator("#reason-side-against")).Not.ToBeCheckedAsync();
        (await HalfColourAsync(b, "for")).ShouldBe("rgba(0, 0, 0, 0)", "an unchosen half is not filled: the switch's own background shows");
        await b.Locator("#reason-text").FillAsync("Papildomos pajamos reikalingos gynybai, o riba paliestų mažai mokėtojų.");
        await b.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();
        await Assertions.Expect(b.GetByText("Pasirinkite, ar argumentas už, ar prieš.")).ToBeVisibleAsync();
        await b.Locator("#reason-side .switch__half--for").ClickAsync();
        await Assertions.Expect(b.Locator("#reason-side-for")).ToBeCheckedAsync();
        (await HalfColourAsync(b, "for")).ShouldBe(await TokenColourAsync(b, "--for"), "the chosen half in its side's colour");
        await b.Locator("#reason-text").FocusAsync();
        await b.Keyboard.PressAsync("Shift+Tab");
        (await b.Locator("#reason-side .switch__half--for").EvaluateAsync<string>("e => getComputedStyle(e).outlineStyle")).ShouldBe("solid", "a clear focus ring from the keyboard");
        await b.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(b.Locator("#reason-side-against")).ToBeCheckedAsync();
        (await HalfColourAsync(b, "against")).ShouldBe(await TokenColourAsync(b, "--against"));
        await b.Locator("#write-reason").ScreenshotAsync(new() { Path = Shot($"switch-against-light-{width}.png") });
        await b.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await b.Locator("#write-reason").ScreenshotAsync(new() { Path = Shot($"switch-against-dark-{width}.png") });
        (await HalfColourAsync(b, "against")).ShouldBe(await TokenColourAsync(b, "--against"), "the dark theme's side colour");
        await b.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await b.Locator("#reason-side .switch__half--for").ClickAsync();
        await b.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();
        await Assertions.Expect(b.GetByText("Argumentas paskelbtas.")).ToBeVisibleAsync();
        var mine = b.Locator("#reasons-for .said").Filter(new() { HasText = "Papildomos pajamos reikalingos gynybai" });
        await Assertions.Expect(mine).ToBeVisibleAsync();
        await Assertions.Expect(b.Locator("#count-for .figure__num")).ToHaveTextAsync("0", new() { Timeout = 10_000 });

        // 4. ... then takes a side: the consent is asked first, then the side is counted and shown only to them.
        var forButton = b.Locator("#side-for");
        await Assertions.Expect(forButton).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await forButton.ClickAsync();
        await Assertions.Expect(b.Locator("#consent-title")).ToBeVisibleAsync();
        await b.Locator("#consent-check").CheckAsync();
        await b.GetByRole(AriaRole.Button, new() { Name = "Sutinku ir prašau balsuoti už" }).ClickAsync();
        await Assertions.Expect(b.Locator("#my-side")).ToContainTextAsync("Jūs prašote balsuoti už. Tai matote tik jūs.", new() { Timeout = 30_000 });
        await Assertions.Expect(b.Locator("#count-for .figure__num")).ToHaveTextAsync("1");
        await Assertions.Expect(b.Locator("#count-against .figure__num")).ToHaveTextAsync("0", new() { Timeout = 5_000 });
        await Assertions.Expect(b.Locator("#reasons-note")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        // 5. ... and marks their argument "Man svarbu", now that it is on their own side.
        await mine.GetByRole(AriaRole.Button, new() { Name = "Man svarbu · 0" }).ClickAsync();
        await Assertions.Expect(mine.GetByRole(AriaRole.Button, new() { Name = "Man svarbu · 1" })).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(b.Locator("#not-a-poll")).ToContainTextAsync("Tai nėra reprezentatyvi apklausa");
        await Assertions.Expect(mine.GetByRole(AriaRole.Button, new() { Name = "Atsiimti", Exact = true })).ToBeVisibleAsync();
        await ActionsOnOneLineAsync(b);
        await NoSidewaysScrollAsync(b);
        await b.ScreenshotAsync(new() { Path = Shot($"appeal-open-{width}.png"), FullPage = true });

        // Both columns of the demonstration appeal, signed in: "Man svarbu" and "Pranešti" on one line under each.
        await b.GotoAsync($"{Base}/seimas/XVP-0412/kreipimasis");
        // "Pranešti" is enabled once the island has started ("Man svarbu" stays disabled: no side on this bill).
        await Assertions.Expect(b.Locator("#reasons-against .said__actions button.linkish").First).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await ActionsOnOneLineAsync(b);
        await b.Locator("#reasons").ScreenshotAsync(new() { Path = Shot($"argument-actions-{width}.png") });

        // The initiator sees the counts but not the second member's side; the initiator has none until they take one.
        await a.ReloadAsync();
        await Assertions.Expect(a.Locator("#count-for .figure__num")).ToHaveTextAsync("1");
        var againstButton = a.Locator("#side-against");
        await Assertions.Expect(againstButton).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await againstButton.ClickAsync();
        await a.Locator("#consent-check").CheckAsync();
        await a.GetByRole(AriaRole.Button, new() { Name = "Sutinku ir prašau balsuoti prieš" }).ClickAsync();
        await Assertions.Expect(a.Locator("#my-side")).ToContainTextAsync("Jūs prašote balsuoti prieš.", new() { Timeout = 30_000 });

        // 6. The initiator, who now asks to vote against, gets "prieš" preselected and writes an argument for instead.
        var addMore = a.Locator("#add-reason");
        await Assertions.Expect(addMore).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await addMore.ClickAsync();
        await Assertions.Expect(a.Locator("#reason-side-against")).ToBeCheckedAsync();
        await a.Locator("#reason-side-for").CheckAsync();
        await a.Locator("#reason-text").FillAsync("Pripažįstu, kad biudžetui papildomų pajamų reikia.");
        await a.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();
        await Assertions.Expect(a.GetByText("Argumentas paskelbtas.")).ToBeVisibleAsync();
        await Assertions.Expect(a.Locator("#reasons-for .said").Filter(new() { HasText = "Pripažįstu, kad biudžetui" })).ToBeVisibleAsync();
        await Assertions.Expect(a.Locator("#count-against .figure__num")).ToHaveTextAsync("1");
        await Assertions.Expect(a.Locator("#reasons-note")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        await NoSidewaysScrollAsync(a);
        await a.ScreenshotAsync(new() { Path = Shot($"appeal-argument-other-side-{width}.png"), FullPage = true });

        // 7. ... and withdraws it again: the button is beside one's own arguments only, and asks first.
        var own = a.Locator("#reasons-for .said").Filter(new() { HasText = "Pripažįstu, kad biudžetui" });
        await Assertions.Expect(a.Locator("#reasons-for .said").Filter(new() { HasText = "Papildomos pajamos reikalingos gynybai" })
            .GetByRole(AriaRole.Button, new() { Name = "Atsiimti" })).ToHaveCountAsync(0);
        await own.GetByRole(AriaRole.Button, new() { Name = "Atsiimti", Exact = true }).ClickAsync();
        await a.GetByRole(AriaRole.Button, new() { Name = "Atsiimti argumentą" }).ClickAsync();
        await Assertions.Expect(a.GetByText("Argumentas atsiimtas.")).ToBeVisibleAsync();
        await Assertions.Expect(own).ToHaveCountAsync(0);

        initiator.ConsoleErrors.ShouldBeEmpty();
        second.ConsoleErrors.ShouldBeEmpty();
        await HideOwnAppealsOnAsync(mod, bill!);
    }

    /// <summary>The background colour of one half of the side switch.</summary>
    private static Task<string> HalfColourAsync(IPage page, string side) =>
        page.Locator($"#reason-side .switch__half--{side}").EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");

    /// <summary>A colour token of the current theme, as the browser computes it (rgb).</summary>
    private static Task<string> TokenColourAsync(IPage page, string token) =>
        page.EvaluateAsync<string>(
            "t => { const d = document.createElement('div'); d.style.backgroundColor = 'var(' + t + ')'; document.body.append(d); const c = getComputedStyle(d).backgroundColor; d.remove(); return c; }",
            token);

    [Fact]
    public async Task The_public_appeal_page_reads_without_javascript()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            JavaScriptEnabled = false,
            IgnoreHTTPSErrors = true,
            Locale = "lt-LT",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();

        // The demonstration appeal (migrator --demo-appeal) on XVP-0412.
        await page.GotoAsync($"{Base}/seimas/XVP-0412/kreipimasis");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Gyventojų pajamų mokesčio įstatymo pataisa");
        await Assertions.Expect(page.Locator("#count-for .figure__num")).ToHaveTextAsync("6 800");
        await Assertions.Expect(page.Locator("#count-against .figure__num")).ToHaveTextAsync("4 280");
        await Assertions.Expect(page.Locator("#count-for")).ToContainTextAsync("iš jų 6 150 – paskyros senesnės nei 30 d.");
        await Assertions.Expect(page.Locator("#count-against")).ToContainTextAsync("iš jų 3 900 – paskyros senesnės nei 30 d.");
        await Assertions.Expect(page.GetByText("Didžiausias pajamas gaunantys dabar moka mažesnį efektyvųjį tarifą nei samdomi darbuotojai.")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Man svarbu · 1 920")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#not-a-poll")).ToHaveTextAsync("Tai nėra reprezentatyvi apklausa: skaičiai rodo savanoriškai dalyvaujančių Lietuva 2.0 naudotojų nuomonę.");
        await Assertions.Expect(page.Locator("a.btn--for")).ToHaveAttributeAsync("href", "/prisijungti");

        // Reporting without JavaScript and without a session: the disclosure opens and says how.
        await page.GetByText("Pranešti apie kreipimąsi").ClickAsync();
        // In the page itself (the feedback balloon, closed, holds the same address too).
        await Assertions.Expect(page.Locator("main").GetByText("Pranešti apie turinį moderatoriams gali prisijungę Lietuva 2.0 naudotojai:").Last).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("main a[href='mailto:moderatoriai@lt2.localhost']").Last).ToBeVisibleAsync();
        await NoSidewaysScrollAsync(page);
        await page.ScreenshotAsync(new() { Path = Shot("appeal-no-javascript-390.png"), FullPage = true });

        await page.GotoAsync($"{Base}/kreipimaisi");
        await Assertions.Expect(page.Locator("#appeals a[href='/seimas/XVP-0412/kreipimasis']")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_summary_of_the_demonstration_appeal_reads_without_javascript_and_names_nobody(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        // A moderator approves one urgent summary for XVP-0412 (a dry run; once a week, so a later run finds it made).
        var mod = await moderator.PageAsync();
        var status = await mod.EvaluateAsync<int>(
            """
            async () => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const appeal = await (await fetch('/api/v1/bills/XVP-0412/appeal')).json();
              const r = await fetch('/api/v1/moderation/appeals/' + appeal.id + '/urgent', {
                method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
                body: JSON.stringify({ statement: 'Demonstracinė suvestinė.' }) });
              return r.status;
            }
            """);
        status.ShouldBeOneOf(201, 409);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            JavaScriptEnabled = false,
            IgnoreHTTPSErrors = true,
            Locale = "lt-LT",
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{Base}/suvestines");
        await Assertions.Expect(page.Locator("#dry-run")).ToHaveTextAsync("Bandymas: suvestinės sudaromos, bet Seimo nariams nesiunčiamos.");
        await Assertions.Expect(page.GetByText("Gyventojų pajamų mokesčio įstatymo pataisa").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("6 800").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#not-a-poll")).ToContainTextAsync("Tai nėra reprezentatyvi apklausa");
        (await page.ContentAsync()).ShouldNotContain("Rūta Mockutė", customMessage: "a summary names nobody");
        await NoSidewaysScrollAsync(page);
        await page.ScreenshotAsync(new() { Path = Shot($"summary-no-javascript-{width}.png"), FullPage = true });

        // The planned date the watcher read from the stand-in's invented agenda page.
        await page.GotoAsync($"{Base}/seimas/XVP-0412/kreipimasis");
        await Assertions.Expect(page.Locator("#planned")).ToContainTextAsync("Planuojama");
    }

    /// <summary>
    /// Design 8.3: a moderator restricts an argument's author from the queue (until lifted, with a statement), and the
    /// author then sees, in place of the writing box, that writing is restricted and why. Cleaned up through the API.
    /// </summary>
    [Fact]
    public async Task A_moderator_restricts_an_author_from_the_queue_and_the_author_sees_the_notice()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var mod = await moderator.PageAsync();
        const string Prefix = "Šis argumentas parašytas, kad moderatorius apribotų autoriaus rašymą";
        var Text = $"{Prefix} (bandymas {DateTime.UtcNow:HHmmss}).";

        await using var author = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var w = author.Page;
        await PasskeyTests.RegisterAsync(w, "Ribojama Autorė");
        await w.GotoAsync($"{Base}/seimas/XVP-0412/kreipimasis");
        var add = w.Locator("#add-reason");
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();
        await w.Locator("#reason-side .switch__half--against").ClickAsync();
        await w.Locator("#reason-text").FillAsync(Text);
        await w.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();

        // An earlier run's argument may be offered as similar first; then publish this one anyway.
        var published = w.GetByText("Argumentas paskelbtas.");
        var anyway = w.GetByRole(AriaRole.Button, new() { Name = "Vis tiek paskelbti savo" });
        await Assertions.Expect(published.Or(anyway)).ToBeVisibleAsync();
        if (await anyway.IsVisibleAsync())
        {
            await anyway.ClickAsync();
        }

        await Assertions.Expect(published).ToBeVisibleAsync();

        // The moderator opens the author's screen from the queue and restricts until lifted.
        await mod.GotoAsync($"{Base}/moderavimas");
        // The item whose own argument this is (another item may list it as a merge candidate).
        var item = mod.Locator("article").Filter(new() { Has = mod.Locator(".said > p.bubble", new() { HasTextString = Text }) });
        await Assertions.Expect(item).ToBeVisibleAsync(new() { Timeout = 60_000 });
        // "Riboti autoriaus rašymą" is one of the item's actions (design 8.3); the length is the two-part switch.
        var open = item.Locator("button[id^='action-restrict-']");
        await Assertions.Expect(open).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await open.ClickAsync();
        await Assertions.Expect(item.GetByText("Riboti rašymą: Ribojama Autorė")).ToBeVisibleAsync();
        await Assertions.Expect(item.Locator("button[id^='action-hide-']")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        await Assertions.Expect(item.Locator("input[id^='restrict-until-']")).ToHaveAttributeAsync("type", "text");
        await Assertions.Expect(item.Locator("input[id^='restrict-until-']")).ToHaveAttributeAsync("placeholder", "2026-11-19");
        await item.Locator("input[id^='restrict-lifted-']").CheckAsync();
        await item.Locator("textarea[id^='restrict-statement-']").FillAsync("Bandymas: rašymas apribojamas, kol bus atšauktas.");
        await item.GetByRole(AriaRole.Button, new() { Name = "Riboti rašymą", Exact = true }).ClickAsync();
        await Assertions.Expect(mod.GetByText("Rašymas apribotas; naudotojui išsiųstas laiškas.")).ToBeVisibleAsync();
        await Assertions.Expect(mod.Locator("#restrictions article").Filter(new() { HasText = "Ribojama Autorė" }).Last).ToBeVisibleAsync();
        await mod.ScreenshotAsync(new() { Path = Shot("moderator-restricts-1280.png"), FullPage = true });

        // The author sees the notice instead of the writing box.
        await w.ReloadAsync();
        var notice = w.Locator(".writing-restricted");
        await Assertions.Expect(notice).ToContainTextAsync("Moderatorius apribojo jūsų galimybę rašyti.", new() { Timeout = 30_000 });
        await Assertions.Expect(notice).ToContainTextAsync("kol jį atšauks moderatorius");
        await Assertions.Expect(notice).ToContainTextAsync("Priežastis: Bandymas: rašymas apribojamas, kol bus atšauktas.");
        await Assertions.Expect(w.Locator("#add-reason")).ToHaveCountAsync(0);
        await NoSidewaysScrollAsync(w);
        await w.ScreenshotAsync(new() { Path = Shot("author-restricted-1280.png"), FullPage = true });
        author.ConsoleErrors.ShouldBeEmpty();

        // Clean up: lift the restriction and hide the argument, so the demonstration appeal stays as it was.
        var statuses = await mod.EvaluateAsync<string>(
            """
            async (text) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const post = (url, body) => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }, body: JSON.stringify(body) });
              const out = [];
              for (const r of await (await fetch('/api/v1/moderation/restrictions')).json())
                if (r.displayName === 'Ribojama Autorė') out.push((await post('/api/v1/moderation/restrictions/' + r.id + '/lift', { statement: 'Bandymo pabaiga.' })).status);
              const appeal = await (await fetch('/api/v1/bills/XVP-0412/appeal')).json();
              for (const r of appeal.reasons)
                if (r.text.startsWith(text)) out.push((await post('/api/v1/moderation/reasons/' + r.id + '/hide', { statement: 'Bandymo duomenys išvalomi.' })).status);
              return JSON.stringify(out);
            }
            """,
            Prefix);
        // At least this run's restriction and argument; an earlier, interrupted run's leftovers go too.
        var done = JsonSerializer.Deserialize<int[]>(statuses)!;
        done.Length.ShouldBeGreaterThanOrEqualTo(2);
        done.ShouldAllBe(status => status == 204);
    }

    /// <summary>
    /// Design 8.1 and 8.3 (owner, 2026-11-13): two members propose an appeal on one bill and the form says how many
    /// wait; the queue shows the proposals together, oldest first; the moderator publishes the first with its own action,
    /// and the second author is emailed with the link and no names. Then, the appeal reported, the actions of an open
    /// appeal are offered (a wrongly typed date is not sent), it is hidden, and restored from the list of hidden appeals.
    /// </summary>
    [Fact]
    public async Task Two_members_propose_a_moderator_publishes_one_hides_and_restores_it_and_the_other_author_is_emailed()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var mod = await moderator.PageAsync();
        var bill = await FreeBillAsync(mod, "XVP-0618", "XVP-0533");
        Assert.SkipWhen(bill is null, Taken("XVP-0618", "XVP-0533"));
        var run = DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var firstText = $"Pirmasis pasiūlymas: projektas keičia pajamų mokesčio tarifus ({run}). {Marker}";
        var secondText = $"Antrasis pasiūlymas tam pačiam projektui ({run}). {Marker}";

        await using var first = await PasskeyBrowser.StartAsync("native", 1280, 800);
        await PasskeyTests.RegisterAsync(first.Page, "Pirmoji Siūlytoja");
        await ProposeAsync(first.Page, bill!, firstText);

        // The second member sees, before writing, that one proposal waits: a number, no name, no text.
        await using var second = await PasskeyBrowser.StartAsync("native", 390, 844);
        var s = second.Page;
        var secondEmail = await PasskeyTests.RegisterAsync(s, "Antrasis Siūlytojas");
        await s.GotoAsync($"{Base}/seimas/{bill}/kreipimasis");
        var waiting = s.Locator("#proposals-waiting");
        await Assertions.Expect(waiting).ToContainTextAsync("laukiančių moderatoriaus:");
        (await waiting.TextContentAsync())!.ShouldNotContain("Pirmoji");
        await s.ScreenshotAsync(new() { Path = Shot("proposals-waiting-390.png"), FullPage = true });
        await ProposeAsync(s, bill!, secondText);

        // The queue: both together, the oldest first, with the note; the moderator publishes the first.
        await mod.GotoAsync($"{Base}/moderavimas");
        await Assertions.Expect(mod.Locator($"#proposals-{bill}")).ToContainTextAsync("Skelbkite seniausią pasiūlymą", new() { Timeout = 60_000 });
        var firstItem = mod.Locator("article").Filter(new() { HasText = firstText });
        var secondItem = mod.Locator("article").Filter(new() { HasText = secondText });
        await Assertions.Expect(secondItem).ToBeVisibleAsync();
        (await firstItem.BoundingBoxAsync())!.Y.ShouldBeLessThan((await secondItem.BoundingBoxAsync())!.Y, "the oldest proposal first");
        await Assertions.Expect(firstItem.Locator("button[id^='action-']")).ToHaveTextAsync(["Skelbti", "Grąžinti autoriui", "Paslėpti", "Keisti ribą", "Riboti autoriaus rašymą"], new() { Timeout = 60_000 });
        await firstItem.ScreenshotAsync(new() { Path = Shot("queue-proposal-actions-1280.png") });
        await mod.SetViewportSizeAsync(390, 844);
        await mod.ScreenshotAsync(new() { Path = Shot("queue-proposals-390.png"), FullPage = true });
        await NoSidewaysScrollAsync(mod);
        await mod.SetViewportSizeAsync(1280, 900);
        await ActAsync(firstItem, "publish");
        await Assertions.Expect(firstItem.GetByText("Atlikta: paskelbta.")).ToBeVisibleAsync();
        await Assertions.Expect(firstItem.Locator("button[id^='action-']")).ToHaveCountAsync(0, new() { Timeout = 5_000 });

        // The second author is told, with the link, that this is no judgement of their text; nobody is named.
        var mail = await MailAsync(secondEmail, "kitas kreipimosi pasiūlymas");
        mail.ShouldContain($"/seimas/{bill}/kreipimasis");
        mail.ShouldContain("Tai nėra jūsų teksto vertinimas");
        mail.ShouldNotContain("Pirmoji Siūlytoja");
        await s.ReloadAsync();
        await Assertions.Expect(s.GetByText(firstText)).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Reported, the open appeal comes to the queue with the actions of an open appeal.
        var reported = await s.EvaluateAsync<int>(
            """
            async (bill) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const appeal = await (await fetch('/api/v1/bills/' + bill + '/appeal')).json();
              const r = await fetch('/api/v1/reports', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
                body: JSON.stringify({ targetKind: 'appeal', targetId: appeal.id, ground: 'other', text: 'Bandymas.' }) });
              return r.status;
            }
            """,
            bill);
        reported.ShouldBe(204);
        await mod.GotoAsync($"{Base}/moderavimas");
        var open = mod.Locator("article").Filter(new() { HasText = firstText });
        await Assertions.Expect(open.Locator("button[id^='action-']")).ToHaveTextAsync(
            ["Palikti", "Paslėpti", "Keisti ribą", "Pataisyti planuojamą datą", "Sulaikyti siuntimą", "Patvirtinti skubią suvestinę", "Riboti autoriaus rašymą"],
            new() { Timeout = 60_000 });

        // The planned date is typed as text in the form yyyy-MM-dd; a wrong one is refused before anything is sent.
        await open.Locator("button[id^='action-planned-']").ClickAsync();
        var date = open.Locator("input[id^='planned-']");
        await Assertions.Expect(date).ToHaveAttributeAsync("type", "text");
        await Assertions.Expect(date).ToHaveAttributeAsync("inputmode", "numeric");
        await Assertions.Expect(date).ToHaveAttributeAsync("placeholder", "2026-11-19");
        await Assertions.Expect(open.Locator("select[id^='stage-'] option")).ToHaveTextAsync(["Pateikimas", "Svarstymas", "Priėmimas"]);
        await Assertions.Expect(open.Locator("button[id^='action-hide-']")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        await date.FillAsync("19.11.2026");
        await open.Locator("textarea[id^='statement-']").FillAsync("Bandymas: data neteisingo pavidalo.");
        await open.Locator("button[id^='action-submit-']").ClickAsync();
        await Assertions.Expect(open.GetByText("Įrašykite datą skaičiais: metai-mėnuo-diena, pvz., 2026-11-19.")).ToBeVisibleAsync();
        await open.ScreenshotAsync(new() { Path = Shot("queue-planned-date-1280.png") });
        await open.GetByRole(AriaRole.Button, new() { Name = "Atšaukti" }).ClickAsync();

        // Hidden from the queue, then restored from the list of hidden appeals.
        await ActAsync(open, "hide", "Bandymas: paslepiama ir atkuriama.");
        await Assertions.Expect(open.GetByText("Atlikta: paslėpta.")).ToBeVisibleAsync();
        await mod.GotoAsync($"{Base}/moderavimas/paslepti");
        var hidden = mod.Locator("article[id^='hidden-appeal-']").Filter(new() { HasText = firstText });
        await Assertions.Expect(hidden).ToContainTextAsync("Bandymas: paslepiama ir atkuriama.", new() { Timeout = 60_000 });
        await Assertions.Expect(hidden).ToContainTextAsync("Atkūrus bus paskelbtas");
        await Assertions.Expect(hidden).ToContainTextAsync("Moderatorius M.");
        var restore = hidden.Locator("button[id^='restore-open-']");
        await Assertions.Expect(restore).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await restore.ClickAsync();
        await hidden.Locator("textarea[id^='restore-statement-']").FillAsync("Bandymas: atkuriama.");
        await hidden.Locator("form button[type=submit]").ClickAsync();
        await Assertions.Expect(hidden.GetByText("Atkurta. Autorius gaus jūsų paaiškinimą.")).ToBeVisibleAsync();
        await hidden.ScreenshotAsync(new() { Path = Shot("hidden-restored-1280.png") });
        await s.ReloadAsync();
        await Assertions.Expect(s.GetByText(firstText)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(s.Locator("#appeal-status")).ToHaveCountAsync(0, new() { Timeout = 5_000 });

        first.ConsoleErrors.ShouldBeEmpty();
        second.ConsoleErrors.ShouldBeEmpty();
        await HideOwnAppealsOnAsync(mod, bill!);
    }

    private static readonly string MailpitUrl = Environment.GetEnvironmentVariable("LT2_E2E_MAILPIT_URL") ?? "http://localhost:8025";

    /// <summary>Proposes an appeal on the bill's page and waits for the pending note.</summary>
    private static async Task ProposeAsync(IPage page, string bill, string description)
    {
        await page.GotoAsync($"{Base}/seimas/{bill}/kreipimasis");
        var field = page.Locator("#open-description");
        await Assertions.Expect(field).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await field.FillAsync(description);
        await page.GetByRole(AriaRole.Button, new() { Name = "Pateikti moderatoriui" }).ClickAsync();
        await Assertions.Expect(page.Locator("#pending-note")).ToContainTextAsync("Kreipimasis laukia moderatoriaus", new() { Timeout = 30_000 });
    }

    /// <summary>The text of the newest email to <paramref name="to"/> whose subject has <paramref name="subject"/>.</summary>
    private static async Task<string> MailAsync(string to, string subject)
    {
        using var http = new HttpClient { BaseAddress = new Uri(MailpitUrl) };
        for (var attempt = 0; ; attempt++)
        {
            var search = JsonDocument.Parse(await http.GetStringAsync($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{to}\" subject:\"{subject}\"")}")).RootElement;
            if (search.GetProperty("messages").GetArrayLength() > 0)
            {
                var id = search.GetProperty("messages")[0].GetProperty("ID").GetString();
                return JsonDocument.Parse(await http.GetStringAsync($"/api/v1/message/{id}")).RootElement.GetProperty("Text").GetString()!;
            }

            attempt.ShouldBeLessThan(60, $"an email to {to} about \"{subject}\" arrives in Mailpit");
            await Task.Delay(500);
        }
    }

    private static string Shot(string name)
    {
        Directory.CreateDirectory(Path.Combine("output", "appeals"));
        return Path.Combine("output", "appeals", name);
    }

    private static async Task NoSidewaysScrollAsync(IPage page) =>
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"))
            .ShouldBeLessThanOrEqualTo(0, "no horizontal scrolling");

    /// <summary>
    /// Every appeal and proposal a test writes carries this in its description, so the tests recognise their own and touch
    /// nothing else (owner, 2026-11-13: a test hid appeals a person had opened by hand on the same stack).
    /// </summary>
    internal const string Marker = "(E2E bandymas)";

    /// <summary>
    /// The bill a test may use: the first of <paramref name="bills"/> that no published appeal holds, or whose published
    /// appeal a test created (it is hidden first). A bill held by a published appeal the tests did not create is left
    /// alone; null when every one is held so.
    /// </summary>
    private static async Task<string?> FreeBillAsync(IPage moderatorPage, params string[] bills)
    {
        foreach (var bill in bills)
        {
            var state = await moderatorPage.EvaluateAsync<string>(
                """
                async ([bill, marker]) => {
                  const r = await fetch('/api/v1/bills/' + bill + '/appeal');
                  if (!r.ok) return 'free';
                  const a = await r.json();
                  if (a.status !== 'open' && a.status !== 'closed') return 'free';
                  return a.description.includes(marker) ? 'ours' : 'taken';
                }
                """,
                new[] { bill, Marker });
            if (state != "taken")
            {
                await HideOwnAppealsOnAsync(moderatorPage, bill);
                return bill;
            }
        }

        return null;
    }

    private static string Taken(params string[] bills) =>
        $"Published appeals that the tests did not create hold {string.Join(" and ", bills)}. The tests touch only what they created " +
        $"(descriptions with \"{Marker}\"), so this one does not run; hide or close those appeals to run it.";

    /// <summary>
    /// Hides, through the API as the moderator, what earlier runs left on the bill: the published appeal and the waiting
    /// proposals whose description carries <see cref="Marker"/>. Nobody else's appeal or proposal is touched.
    /// </summary>
    private static async Task HideOwnAppealsOnAsync(IPage moderatorPage, string bill)
    {
        var json = await moderatorPage.EvaluateAsync<string>(
            """
            async ([bill, marker]) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const ids = [];
              const open = await fetch('/api/v1/bills/' + bill + '/appeal');
              if (open.ok) { const a = await open.json(); if (a.description.includes(marker)) ids.push(a.id); }
              const queue = await (await fetch('/api/v1/moderation/queue')).json();
              for (const a of queue.appeals) if (a.bill.number === bill && a.description.includes(marker)) ids.push(a.id);
              const results = [];
              // An open appeal can also be in the queue (on the agenda under its threshold, flagged, reported).
              for (const id of [...new Set(ids)]) {
                const r = await fetch('/api/v1/moderation/appeals/' + id + '/hide', {
                  method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
                  body: JSON.stringify({ statement: 'Bandymo duomenys išvalomi.' }) });
                results.push(r.status);
              }
              return JSON.stringify(results);
            }
            """,
            new[] { bill, Marker });
        JsonSerializer.Deserialize<int[]>(json)!.ShouldAllBe(status => status == 204);
    }

    /// <summary>A queue item's action: its button opens only its own fields; then its submit button.</summary>
    private static async Task ActAsync(ILocator item, string action, string? statement = null)
    {
        var open = item.Locator($"button[id^='action-{action}-']");
        await Assertions.Expect(open).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await open.ClickAsync();
        await Assertions.Expect(item.Locator("p[id^='action-sentence-']")).ToBeVisibleAsync();
        if (statement is not null)
        {
            await item.Locator("textarea[id^='statement-']").FillAsync(statement);
        }

        await item.Locator("button[id^='action-submit-']").ClickAsync();
    }

    /// <summary>Under each argument, "Man svarbu", "Pranešti" and "Atsiimti" share one line (owner, 2026-11-13).</summary>
    internal static async Task ActionsOnOneLineAsync(IPage page)
    {
        var report = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('#reasons .said__actions')].flatMap(group => {
              const tops = [...group.children].filter(c => c.offsetParent !== null).map(c => Math.round(c.getBoundingClientRect().top + c.getBoundingClientRect().height / 2));
              const by = group.closest('.said__by');
              const name = by?.querySelector('.said__name')?.getBoundingClientRect();
              const box = group.getBoundingClientRect();
              const out = [];
              if (tops.length > 1 && Math.max(...tops) - Math.min(...tops) > 3) out.push(group.id + ': the actions wrap (' + tops.join(',') + ')');
              if (box.right > by.getBoundingClientRect().right + 1) out.push(group.id + ': the actions overflow their column');
              // On one line the taller "Man svarbu" starts a little above the centred name; above means wholly above it.
              if (name && box.bottom <= name.top + 1) out.push(group.id + ': the actions sit above the name');
              return out;
            })
            """);
        report.ShouldBeEmpty();
        (await page.Locator("#reasons .said__actions").CountAsync()).ShouldBeGreaterThan(0, "the check is not empty");
    }

    /// <summary>One moderator for the class: registered, given a passkey, granted the role by the migrator, activated.</summary>
    public sealed class ModeratorFixture() : StaffFixture("moderator", "Moderatorius M.");
}
