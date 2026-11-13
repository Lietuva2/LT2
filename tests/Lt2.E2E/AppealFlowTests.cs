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
    [InlineData(1280, 800, "XVP-0533")]
    [InlineData(390, 844, "XVP-0618")]
    public async Task A_member_opens_an_appeal_a_moderator_publishes_it_and_another_takes_a_side_writes_and_endorses(int width, int height, string bill)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var mod = await moderator.PageAsync();
        await HideAppealsOnAsync(mod, bill);

        // 1. The initiator opens the appeal on the bill's appeal page.
        await using var initiator = await PasskeyBrowser.StartAsync("native", width, height);
        var a = initiator.Page;
        await PasskeyTests.RegisterAsync(a, "Rūta Mockutė");
        await a.GotoAsync($"{Base}/seimas/{bill}/kreipimasis");
        await Assertions.Expect(a.Locator("#no-appeal")).ToBeVisibleAsync();
        await a.Locator("#open-description:not([disabled])").WaitForAsync(new() { Timeout = 60_000 });
        await a.Locator("#open-side-against").CheckAsync();
        await a.Locator("#open-description").FillAsync("Projektas keičia mokesčių tvarką: įvedamas naujas tarifas didelėms pajamoms.");
        await a.Locator("#open-reason").FillAsync("Pakeitimas paliestų vienkartinius įvykius, pavyzdžiui šeimos verslo pardavimą.");
        await a.Locator("#open-threshold").FillAsync("100");
        await a.Locator("#open-consent").CheckAsync();
        await a.GetByRole(AriaRole.Button, new() { Name = "Pateikti moderatoriui" }).ClickAsync();
        await Assertions.Expect(a.Locator("#pending-note")).ToContainTextAsync("Kreipimasis laukia moderatoriaus", new() { Timeout = 30_000 });
        await NoSidewaysScrollAsync(a);
        await a.ScreenshotAsync(new() { Path = Shot($"appeal-pending-{width}.png"), FullPage = true });

        // 2. The moderator publishes it from the queue.
        await mod.GotoAsync($"{Base}/moderavimas");
        var item = mod.Locator("article").Filter(new() { HasText = bill }).Filter(new() { HasText = "Projektas keičia mokesčių tvarką" });
        await Assertions.Expect(item).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await item.Locator("button:not([disabled])", new() { HasText = "Paskelbti" }).WaitForAsync(new() { Timeout = 60_000 });
        await item.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();
        await Assertions.Expect(mod.GetByText("Atlikta: paskelbta.")).ToBeVisibleAsync();

        // 3. A second member, before taking any side, writes an argument: nothing is preselected, so they must choose.
        await using var second = await PasskeyBrowser.StartAsync("native", width, height);
        var b = second.Page;
        await PasskeyTests.RegisterAsync(b, "Giedrė L.");
        await b.GotoAsync($"{Base}/seimas/{bill}/kreipimasis");
        await Assertions.Expect(b.Locator("#count-for .figure__num")).ToHaveTextAsync("0");
        var add = b.Locator("#add-reason");
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();
        await Assertions.Expect(b.Locator("#write-reason")).ToContainTextAsync("nepriklausomai nuo to, ko prašote patys");
        await Assertions.Expect(b.Locator("#reason-side-for")).Not.ToBeCheckedAsync();
        await Assertions.Expect(b.Locator("#reason-side-against")).Not.ToBeCheckedAsync();
        await b.Locator("#reason-text").FillAsync("Papildomos pajamos reikalingos gynybai, o riba paliestų mažai mokėtojų.");
        await b.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();
        await Assertions.Expect(b.GetByText("Pasirinkite, ar argumentas už, ar prieš.")).ToBeVisibleAsync();
        await b.Locator("#reason-side-for").CheckAsync();
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
        await Assertions.Expect(b.Locator("#count-against .figure__num")).ToHaveTextAsync("1");
        await Assertions.Expect(b.Locator("#reasons-note")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        // 5. ... and marks their argument "Man svarbu", now that it is on their own side.
        await mine.GetByRole(AriaRole.Button, new() { Name = "Man svarbu · 0" }).ClickAsync();
        await Assertions.Expect(mine.GetByRole(AriaRole.Button, new() { Name = "Man svarbu · 1" })).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(b.Locator("#not-a-poll")).ToContainTextAsync("Tai nėra reprezentatyvi apklausa");
        await NoSidewaysScrollAsync(b);
        await b.ScreenshotAsync(new() { Path = Shot($"appeal-open-{width}.png"), FullPage = true });

        // The initiator sees the counts but not the second member's side.
        await a.ReloadAsync();
        await Assertions.Expect(a.Locator("#count-for .figure__num")).ToHaveTextAsync("1");
        await Assertions.Expect(a.Locator("#my-side")).ToContainTextAsync("Jūs prašote balsuoti prieš.", new() { Timeout = 30_000 });

        // 6. The initiator, who asks to vote against, gets "prieš" preselected and writes an argument for instead.
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
        await HideAppealsOnAsync(mod, bill);
    }

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
        await Assertions.Expect(page.GetByText("Pranešti apie turinį moderatoriams gali prisijungę Lietuva 2.0 naudotojai:").Last).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("a[href='mailto:moderatoriai@lt2.localhost']").Last).ToBeVisibleAsync();
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
        const string Text = "Šis argumentas parašytas, kad moderatorius apribotų autoriaus rašymą.";

        await using var author = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var w = author.Page;
        await PasskeyTests.RegisterAsync(w, "Ribojama Autorė");
        await w.GotoAsync($"{Base}/seimas/XVP-0412/kreipimasis");
        var add = w.Locator("#add-reason");
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();
        await w.Locator("#reason-side-against").CheckAsync();
        await w.Locator("#reason-text").FillAsync(Text);
        await w.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();
        await Assertions.Expect(w.GetByText("Argumentas paskelbtas.")).ToBeVisibleAsync();

        // The moderator opens the author's screen from the queue and restricts until lifted.
        await mod.GotoAsync($"{Base}/moderavimas");
        var item = mod.Locator("article").Filter(new() { HasText = Text });
        await Assertions.Expect(item).ToBeVisibleAsync(new() { Timeout = 60_000 });
        var open = item.GetByRole(AriaRole.Button, new() { Name = "Riboti autoriaus rašymą…" });
        await Assertions.Expect(open).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await open.ClickAsync();
        await Assertions.Expect(item.GetByText("Riboti rašymą: Ribojama Autorė")).ToBeVisibleAsync();
        await item.Locator("input[id^='restrict-lifted-']").CheckAsync();
        await item.Locator("textarea[id^='restrict-statement-']").FillAsync("Bandymas: rašymas apribojamas, kol bus atšauktas.");
        await item.GetByRole(AriaRole.Button, new() { Name = "Riboti rašymą", Exact = true }).ClickAsync();
        await Assertions.Expect(mod.GetByText("Rašymas apribotas; naudotojui išsiųstas laiškas.")).ToBeVisibleAsync();
        await Assertions.Expect(mod.Locator("#restrictions article").Filter(new() { HasText = "Ribojama Autorė" })).ToBeVisibleAsync();
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
                if (r.text === text) out.push((await post('/api/v1/moderation/reasons/' + r.id + '/hide', { statement: 'Bandymo duomenys išvalomi.' })).status);
              return JSON.stringify(out);
            }
            """,
            Text);
        JsonSerializer.Deserialize<int[]>(statuses)!.ShouldBe([204, 204]);
    }

    private static string Shot(string name)
    {
        Directory.CreateDirectory(Path.Combine("output", "appeals"));
        return Path.Combine("output", "appeals", name);
    }

    private static async Task NoSidewaysScrollAsync(IPage page) =>
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"))
            .ShouldBeLessThanOrEqualTo(0, "no horizontal scrolling");

    /// <summary>Hides, through the API as the moderator, any appeal on the bill left by an earlier run.</summary>
    private static async Task HideAppealsOnAsync(IPage moderatorPage, string bill)
    {
        var json = await moderatorPage.EvaluateAsync<string>(
            """
            async (bill) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const ids = [];
              const open = await fetch('/api/v1/bills/' + bill + '/appeal');
              if (open.ok) ids.push((await open.json()).id);
              const queue = await (await fetch('/api/v1/moderation/queue')).json();
              for (const a of queue.appeals) if (a.bill.number === bill) ids.push(a.id);
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
            bill);
        JsonSerializer.Deserialize<int[]>(json)!.ShouldAllBe(status => status == 204);
    }

    /// <summary>One moderator for the class: registered, given a passkey, granted the role by the migrator, activated.</summary>
    public sealed class ModeratorFixture : IAsyncLifetime
    {
        private PasskeyBrowser? _browser;

        public async Task<IPage> PageAsync()
        {
            if (_browser is not null)
            {
                return _browser.Page;
            }

            _browser = await PasskeyBrowser.StartAsync("native");
            var page = _browser.Page;
            Directory.CreateDirectory(Path.Combine("output", "appeals"));
            var log = Path.Combine("output", "appeals", "moderator-console.txt");
            File.WriteAllText(log, "");
            page.Console += (_, message) => File.AppendAllText(log, $"{message.Type}: {message.Text}{Environment.NewLine}");
            page.PageError += (_, error) => File.AppendAllText(log, $"pageerror: {error}{Environment.NewLine}");
            page.Response += (_, response) =>
            {
                if (response.Status >= 400)
                {
                    File.AppendAllText(log, $"http {response.Status}: {response.Url}{Environment.NewLine}");
                }
            };
            var email = await PasskeyTests.RegisterAsync(page, "Moderatorius M.");
            await PasskeyTests.AddPasskeyAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Atsijungti" }).ClickAsync();
            await page.WaitForURLAsync(new Regex("/$"));
            await PasskeyTests.SignInWithPasskeyAsync(page);
            var output = await Compose.RunAsync("run", "--rm", "--no-deps", "migrator", "--grant-role", "moderator", email);
            var code = PasskeyTests.ActivationCode().Match(output).Value;
            code.ShouldNotBeNullOrEmpty("the migrator prints the activation code");
            await page.ReloadAsync();
            await SignUpTests.WaitForInteractivityAsync(page);
            await page.GetByLabel("Aktyvinimo kodas").FillAsync(code);
            await page.GetByRole(AriaRole.Button, new() { Name = "Aktyvinti" }).ClickAsync();
            await Assertions.Expect(page.GetByText("Vaidmuo aktyvintas.")).ToBeVisibleAsync();
            return page;
        }

        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
            }
        }
    }
}
