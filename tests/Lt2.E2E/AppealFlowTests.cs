using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// The main path of appeals through real browsers against the stack (design 8.1, 10), at 1280 px and 390 px: a member
/// opens an appeal, a moderator publishes it in a staff-passkey session, a second member takes a side (giving the
/// consent first), writes an argument and marks one "Man svarbu". And the public appeal page once with JavaScript off.
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

        // 3. A second member takes a side: the consent is asked first, then the side is counted and shown only to them.
        await using var second = await PasskeyBrowser.StartAsync("native", width, height);
        var b = second.Page;
        await PasskeyTests.RegisterAsync(b, "Giedrė L.");
        await b.GotoAsync($"{Base}/seimas/{bill}/kreipimasis");
        await Assertions.Expect(b.Locator("#count-for .figure__num")).ToHaveTextAsync("0");
        var forButton = b.Locator("#side-for");
        await Assertions.Expect(forButton).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await forButton.ClickAsync();
        await Assertions.Expect(b.Locator("#consent-title")).ToBeVisibleAsync();
        await b.Locator("#consent-check").CheckAsync();
        await b.GetByRole(AriaRole.Button, new() { Name = "Sutinku ir prašau balsuoti už" }).ClickAsync();
        await Assertions.Expect(b.Locator("#my-side")).ToContainTextAsync("Jūs prašote balsuoti už. Tai matote tik jūs.", new() { Timeout = 30_000 });
        await Assertions.Expect(b.Locator("#count-for .figure__num")).ToHaveTextAsync("1");
        await Assertions.Expect(b.Locator("#count-against .figure__num")).ToHaveTextAsync("1");

        // 4. ... writes an argument, which is published at once ...
        var add = b.Locator("#add-reason");
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();
        await b.Locator("#reason-text").FillAsync("Papildomos pajamos reikalingos gynybai, o riba paliestų mažai mokėtojų.");
        await b.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();
        await Assertions.Expect(b.GetByText("Argumentas paskelbtas.")).ToBeVisibleAsync();
        var mine = b.Locator("#reasons-for .said").Filter(new() { HasText = "Papildomos pajamos reikalingos gynybai" });
        await Assertions.Expect(mine).ToBeVisibleAsync();

        // 5. ... and marks it "Man svarbu".
        await mine.GetByRole(AriaRole.Button, new() { Name = "Man svarbu · 0" }).ClickAsync();
        await Assertions.Expect(mine.GetByRole(AriaRole.Button, new() { Name = "Man svarbu · 1" })).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(b.Locator("#not-a-poll")).ToContainTextAsync("Tai nėra reprezentatyvi apklausa");
        await NoSidewaysScrollAsync(b);
        await b.ScreenshotAsync(new() { Path = Shot($"appeal-open-{width}.png"), FullPage = true });

        // The initiator sees the counts but not the second member's side.
        await a.ReloadAsync();
        await Assertions.Expect(a.Locator("#count-for .figure__num")).ToHaveTextAsync("1");
        await Assertions.Expect(a.Locator("#my-side")).ToContainTextAsync("Jūs prašote balsuoti prieš.", new() { Timeout = 30_000 });

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
        await NoSidewaysScrollAsync(page);
        await page.ScreenshotAsync(new() { Path = Shot("appeal-no-javascript-390.png"), FullPage = true });

        await page.GotoAsync($"{Base}/kreipimaisi");
        await Assertions.Expect(page.Locator("#appeals a[href='/seimas/XVP-0412/kreipimasis']")).ToBeVisibleAsync();
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
              for (const id of ids) {
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
