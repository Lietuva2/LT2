using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// The main path on a bill through real browsers against the stack (design 8.12, 10), at 1280 px and 390 px: every active
/// bill is open at once, so a member writes an argument before taking any side (choosing its side), then takes a side
/// (giving the consent first) and marks it "Man svarbu"; another takes the other side and writes one for the first side.
/// A moderator moderates on the bill's page: writes the description, publishes a waiting argument, hides one, hides and
/// restores the bill. And the public bill page once with JavaScript off. Each test takes back what it did through the
/// API (sides cleared, arguments withdrawn), so the bills stay as they were.
/// </summary>
public sealed class AppealFlowTests(AppealFlowTests.ModeratorFixture moderator) : E2ETest, IClassFixture<AppealFlowTests.ModeratorFixture>
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Theory]
    [InlineData(1280, 800, "XVP-0533")]
    [InlineData(390, 844, "XVP-0618")]
    public async Task A_member_takes_part_on_a_bill_nobody_started_writes_and_endorses_and_another_takes_the_other_side(int width, int height, string bill)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        // 1. A member opens the bill's page. Before WebAssembly has started (held back here, as on a slow connection) the
        // side buttons cannot be used: a click sends nothing and the page does not reload. Then they work as usual.
        await using var first = await PasskeyBrowser.StartAsync("native", width, height);
        var a = first.Page;
        await PasskeyTests.RegisterAsync(a, "Rūta Mockutė");
        var started = new TaskCompletionSource();
        await a.RouteAsync(IslandsBeforeStartTests.WebAssemblyFiles(), async route =>
        {
            await started.Task;
            await route.ContinueAsync();
        });
        var sides = 0;
        a.Request += (_, request) =>
        {
            if (request.Method == "PUT" && request.Url.EndsWith($"/bills/{bill}/record/position", StringComparison.Ordinal))
            {
                sides++;
            }
        };
        await a.GotoAsync($"{Base}/seimas/{bill}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var navigations = 0;
        a.FrameNavigated += (_, frame) =>
        {
            if (frame == a.MainFrame)
            {
                navigations++;
            }
        };
        var forBefore = await CountAsync(a, "for");
        var againstBefore = await CountAsync(a, "against");
        await Assertions.Expect(a.Locator("#side-for")).ToBeDisabledAsync();
        await a.Locator("#side-for").ClickAsync(new() { Force = true });
        await a.WaitForTimeoutAsync(1_000);
        navigations.ShouldBe(0, "the page did not reload");
        sides.ShouldBe(0, "nothing was sent");
        started.SetResult();
        await a.Locator("#side-for:not([disabled])").WaitForAsync(new() { Timeout = 60_000 });

        // Nothing to propose, ask for or wait for (design 8.12.4); no moderator's controls for a member.
        var main = await a.Locator("main").InnerTextAsync();
        foreach (var gone in new[] { "Noriu kreipimosi", "Pateikti moderatoriui", "Atidaryti kreipimąsi", "iniciator" })
        {
            main.ShouldNotContain(gone, Case.Insensitive);
        }

        await Assertions.Expect(a.Locator("#moderation-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(a.Locator("[id^='moderate-']")).ToHaveCountAsync(0);
        await IslandsBeforeStartTests.NothingUsableBeforeStartAsync(a.Context, $"/seimas/{bill}");

        // 2. Before taking any side the member writes an argument: nothing is preselected, so they must choose.
        // A long display name: under an argument the actions still share one line (the name goes above them).
        await using var second = await PasskeyBrowser.StartAsync("native", width, height);
        var b = second.Page;
        await PasskeyTests.RegisterAsync(b, "Giedrė Konstancija Vaitkevičiūtė-Žemaitienė");
        await b.GotoAsync($"{Base}/seimas/{bill}");
        var add = b.Locator("#add-reason");
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();
        // Design 8.15 (annex B25): "either side" is said by the switch itself and the methodology's "Argumentai".
        await Assertions.Expect(b.Locator("#write-reason")).Not.ToContainTextAsync("nepriklausomai nuo to, ko prašote patys");
        await Assertions.Expect(b.Locator("#write-reason")).ToContainTextAsync("Argumentas bus viešas su jūsų vardu ir pasirinkta puse");
        // The side is the two-part switch "Už | Prieš" (owner, 2026-11-13): with no side of one's own, neither half.
        await Assertions.Expect(b.Locator("#reason-side .switch__half")).ToHaveTextAsync(["Už", "Prieš"]);
        await Assertions.Expect(b.Locator("#reason-side-for")).Not.ToBeCheckedAsync();
        await Assertions.Expect(b.Locator("#reason-side-against")).Not.ToBeCheckedAsync();
        (await HalfColourAsync(b, "for")).ShouldBe("rgba(0, 0, 0, 0)", "an unchosen half is not filled: the switch's own background shows");
        var text = $"Papildomos pajamos reikalingos gynybai, o riba paliestų mažai mokėtojų ({width}, {DateTime.UtcNow:HHmmss}).";
        await b.Locator("#reason-text").FillAsync(text);
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
        await PublishReasonAsync(b);
        await ShowAllReasonsAsync(b);
        var mine = b.Locator("#reasons-for .said").Filter(new() { HasText = text });
        await Assertions.Expect(mine).ToBeVisibleAsync();
        await Assertions.Expect(b.Locator("#count-for .figure__num")).ToHaveTextAsync(Number(forBefore), new() { Timeout = 10_000 });

        // 3. ... then takes a side: the consent is asked first, then the side is counted and shown only to them, and the
        // bill they did not follow is followed (design 8.11.2).
        var forButton = b.Locator("#side-for");
        await Assertions.Expect(forButton).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(b.Locator("#follow-toggle-bill")).ToHaveTextAsync("Sekti", new() { Timeout = 60_000 });
        await forButton.ClickAsync();
        await Assertions.Expect(b.Locator("#consent-title")).ToBeVisibleAsync();
        await b.Locator("#consent-check").CheckAsync();
        await b.GetByRole(AriaRole.Button, new() { Name = "Sutinku ir prašau balsuoti už" }).ClickAsync();
        await Assertions.Expect(b.Locator("#my-side")).ToContainTextAsync("Jūs prašote balsuoti už. Tai matote tik jūs.", new() { Timeout = 30_000 });
        await Assertions.Expect(b.Locator("#count-for .figure__num")).ToHaveTextAsync(Number(forBefore + 1));
        await Assertions.Expect(b.Locator("#count-against .figure__num")).ToHaveTextAsync(Number(againstBefore), new() { Timeout = 5_000 });
        await Assertions.Expect(b.Locator("#nobody-yet")).ToHaveCountAsync(0);
        await Assertions.Expect(b.Locator("#follow-toggle-bill")).ToHaveTextAsync("Nebesekti", new() { Timeout = 60_000 });
        await Assertions.Expect(b.Locator("#follow-toggle-bill")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(b.Locator("#share-line")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        (await b.Locator("#share-link").GetAttributeAsync("href"))!.ShouldContain("tikslas=%2Fseimas%2F" + bill, Case.Sensitive, "the share link lands on the bill's page");

        // 4. ... and cannot mark their own argument "Man svarbu" (owner, 2026-11-17); "Atsiimti" is in its "⋯" (design 8.16.1).
        await Assertions.Expect(mine.Locator("button.mark")).ToBeDisabledAsync();
        await Assertions.Expect(mine.Locator("button.mark")).ToHaveAttributeAsync("title", "Savo argumento „Man svarbu“ žymėti negalima.");
        await Assertions.Expect(b.Locator("#not-a-poll")).ToContainTextAsync("Tai nėra reprezentatyvi apklausa");
        await mine.Locator(".act__more").ClickAsync();
        await Assertions.Expect(b.Locator(".menu:popover-open").GetByRole(AriaRole.Button, new() { Name = "Atsiimti", Exact = true })).ToBeVisibleAsync();
        await b.Keyboard.PressAsync("Escape");
        await ActionsOnOneLineAsync(b);
        await E2ESteps.NoSidewaysScrollAsync(b);
        await b.ScreenshotAsync(new() { Path = Shot($"bill-open-{width}.png"), FullPage = true });

        // 5. The first member sees the counts but not the other's side, takes the other side, gets "prieš" preselected
        // and writes an argument for instead; then withdraws it again (the button is beside one's own arguments only).
        await a.ReloadAsync();
        await Assertions.Expect(a.Locator("#count-for .figure__num")).ToHaveTextAsync(Number(forBefore + 1));
        var againstButton = a.Locator("#side-against");
        await Assertions.Expect(againstButton).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await againstButton.ClickAsync();
        await a.Locator("#consent-check").CheckAsync();
        await a.GetByRole(AriaRole.Button, new() { Name = "Sutinku ir prašau balsuoti prieš" }).ClickAsync();
        await Assertions.Expect(a.Locator("#my-side")).ToContainTextAsync("Jūs prašote balsuoti prieš.", new() { Timeout = 30_000 });
        var addMore = a.Locator("#add-reason");
        await Assertions.Expect(addMore).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await addMore.ClickAsync();
        await Assertions.Expect(a.Locator("#reason-side-against")).ToBeCheckedAsync();
        await a.Locator("#reason-side-for").CheckAsync();
        var other = $"Pripažįstu, kad biudžetui papildomų pajamų reikia ({width}, {DateTime.UtcNow:HHmmss}).";
        await a.Locator("#reason-text").FillAsync(other);
        await PublishReasonAsync(a);
        await ShowAllReasonsAsync(a);
        var own = a.Locator("#reasons-for .said").Filter(new() { HasText = other });
        await Assertions.Expect(own).ToBeVisibleAsync();
        await Assertions.Expect(a.Locator("#count-against .figure__num")).ToHaveTextAsync(Number(againstBefore + 1));
        await E2ESteps.NoSidewaysScrollAsync(a);
        await Assertions.Expect(a.Locator("#reasons-for .said").Filter(new() { HasText = text }).GetByRole(AriaRole.Button, new() { Name = "Atsiimti" })).ToHaveCountAsync(0);
        await own.Locator(".act__more").ClickAsync();
        await a.Locator(".menu:popover-open").GetByRole(AriaRole.Button, new() { Name = "Atsiimti", Exact = true }).ClickAsync();
        await a.GetByRole(AriaRole.Button, new() { Name = "Atsiimti argumentą" }).ClickAsync();
        await Assertions.Expect(a.GetByText("Argumentas atsiimtas.")).ToBeVisibleAsync();
        await Assertions.Expect(own).ToHaveCountAsync(0);

        // The demonstration bill, signed in: "Man svarbu", "Aptarimas" and "⋯" on one line under each argument.
        await b.GotoAsync($"{Base}/seimas/XVP-0412");
        await Assertions.Expect(b.Locator("#reasons-against .menu__item").First).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await ActionsOnOneLineAsync(b);
        await b.Locator("#reasons").ScreenshotAsync(new() { Path = Shot($"argument-actions-{width}.png") });

        first.ConsoleErrors.ShouldBeEmpty();
        second.ConsoleErrors.ShouldBeEmpty();
        await TakeBackAsync(a, bill);
        await TakeBackAsync(b, bill);
        var mod = await moderator.PageAsync();
        await mod.GotoAsync($"{Base}/seimas/{bill}");
        await Assertions.Expect(mod.Locator("#count-for .figure__num")).ToHaveTextAsync(Number(forBefore));
        await Assertions.Expect(mod.Locator("#count-against .figure__num")).ToHaveTextAsync(Number(againstBefore));
    }

    /// <summary>
    /// Design 8.12.3 (arguments moderated on the bill's page too, decided by the owner on 2026-11-14): a moderator writes
    /// the description of a bill on its page, publishes a waiting argument from the "Moderavimas" panel and hides another
    /// from "Moderuoti" under it; the author is emailed with the bill's link; a member sees none of the controls. Then the
    /// bill is hidden (taking part closes, with the statement on the page) and restored from the list of hidden items.
    /// </summary>
    [Fact]
    public async Task A_moderator_moderates_on_the_bills_page_and_hides_and_restores_the_bill()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        const string Bill = "XVP-0618";
        await E2EData.RestoreDescriptionAsync(Bill);
        try
        {
            await ModerateOnThePageAsync();
        }
        finally
        {
            // The description written here goes, so the bill is as the demonstration data has it.
            await E2EData.RestoreDescriptionAsync(Bill);
        }
    }

    private async Task ModerateOnThePageAsync()
    {
        const string Bill = "XVP-0618";
        var run = DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var mod = await moderator.PageAsync();

        // An author writes two arguments: one shown at once, one that pre-screening holds for a moderator (a link).
        await using var writer = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var w = writer.Page;
        var email = await PasskeyTests.RegisterAsync(w, "Moderuojama Autorė");
        var shownText = $"Atliekų tvarkymo kainą savivaldybės turėtų nustatyti pačios ({run}).";
        var waitingText = $"Plačiau apie tai: https://pavyzdys.test/atliekos ({run}).";
        await WriteViaApiAsync(w, Bill, "for", shownText).ShouldBeAsync("published");
        await WriteViaApiAsync(w, Bill, "against", waitingText).ShouldBeAsync("pending");
        await w.GotoAsync($"{Base}/seimas/{Bill}");
        await Assertions.Expect(w.Locator("#add-reason")).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(w.Locator("#moderation-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(w.Locator("[id^='moderate-']")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        (await w.ContentAsync()).ShouldNotContain("panel-reason-");

        // The moderator's page: the panel, the waiting argument in it, "Moderuoti" under the shown one.
        await mod.GotoAsync($"{Base}/seimas/{Bill}");
        var panel = mod.Locator("#moderation-panel");
        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // Write (or correct) the description: shown at once with its label.
        var description = $"Projektas keičia atliekų tvarkymo rinkliavos nustatymą ({run}). {Marker}";
        // Design 8.3 (owner, 2026-11-17): "Rašyti aprašymą" is in the panel's "Kiti veiksmai".
        await E2ESteps.ChooseAsync(panel.Locator("button[id^='action-describe-']"));
        await panel.Locator("textarea[id^='describe-']:not([id^='describe-source'])").FillAsync(description);
        await panel.Locator("textarea[id^='statement-']").FillAsync("Projektas darbotvarkėje, aprašymo dar nebuvo.");
        await panel.Locator("button[id^='action-submit-']").ClickAsync();
        await Assertions.Expect(mod.Locator("#about")).ToContainTextAsync(description, new() { Timeout = 30_000 });
        await Assertions.Expect(mod.Locator("#description-source")).ToContainTextAsync("Parašė moderatorius");

        // Publish the waiting argument from the panel.
        var waiting = mod.Locator("article[id^='panel-reason-']").Filter(new() { HasText = waitingText });
        await Assertions.Expect(waiting).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await mod.ScreenshotAsync(new() { Path = Shot("panel-waiting-argument-1280.png"), FullPage = true });
        await ActAsync(waiting, "publish");
        await ShowAllReasonsAsync(mod);
        await Assertions.Expect(mod.Locator("#reasons-against .said").Filter(new() { HasText = waitingText })).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Hide the shown argument from "Moderuoti" under it: the queue's action, told to the author with the bill's link.
        await ShowAllReasonsAsync(mod);
        var shown = mod.Locator("#reasons-for .said").Filter(new() { HasText = shownText });
        // Design 8.16.1: "Moderuoti" is in the argument's "⋯"; its actions open below the row.
        // The item comes with the moderators' answer once the island runs; "⋯" opened before that would be replaced, closed.
        var open = shown.Locator("button.menu__item[id^='moderate-']");
        await Assertions.Expect(open).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await shown.Locator(".act__more").ClickAsync();
        await open.ClickAsync();
        var moderate = shown.Locator(".moderate");
        await Assertions.Expect(moderate).ToBeVisibleAsync(new() { Timeout = 10_000 });
        // Design 8.3 (owner, 2026-11-17): "Palikti" filled, the rest in "Kiti veiksmai", "Paslėpti" last.
        await Assertions.Expect(moderate.Locator("button[id^='action-']")).ToHaveTextAsync(["Palikti", "Riboti autoriaus rašymą", "Paslėpti"], new() { Timeout = 10_000 });
        await Assertions.Expect(moderate.Locator("button.btn[id^='action-keep-']")).ToBeVisibleAsync();
        await moderate.ScreenshotAsync(new() { Path = Shot("moderate-argument-1280.png") });
        await ActAsync(moderate, "hide", "Bandymas: argumentas paslepiamas puslapyje.");
        await Assertions.Expect(mod.Locator("#reasons-for .said").Filter(new() { HasText = shownText })).ToHaveCountAsync(0, new() { Timeout = 30_000 });
        var mail = await Mailpit.MailAsync(email, "jūsų argumentas paslėptas");
        mail.ShouldContain($"/seimas/{Bill}");
        mail.ShouldContain("Bandymas: argumentas paslepiamas puslapyje.");
        await E2ESteps.NoSidewaysScrollAsync(mod);

        // Hiding the bill closes taking part, with the statement; restoring brings everything back.
        // Restored in the finally too, so a failure here never leaves the bill closed for the classes that read it.
        try
        {
            await ActAsync(panel, "hide", "Bandymas: dalyvavimas uždaromas ir atkuriamas.");
            await Assertions.Expect(mod.Locator("#taking-part-closed")).ToContainTextAsync("Bandymas: dalyvavimas uždaromas ir atkuriamas.", new() { Timeout = 30_000 });
            await w.ReloadAsync();
            await Assertions.Expect(w.Locator("#taking-part-closed")).ToBeVisibleAsync();
            await Assertions.Expect(w.Locator("#side-picker")).ToHaveCountAsync(0);
            await w.ScreenshotAsync(new() { Path = Shot("bill-closed-by-moderators-1280.png"), FullPage = true });
            await mod.GotoAsync($"{Base}/moderavimas/paslepti");
            var hidden = mod.Locator("article[id^='hidden-appeal-']").Filter(new() { HasText = Bill });
            await Assertions.Expect(hidden).ToContainTextAsync("Atkūrus vėl bus galima dalyvauti", new() { Timeout = 60_000 });
            var restore = hidden.Locator("button[id^='restore-open-']");
            await Assertions.Expect(restore).ToBeEnabledAsync(new() { Timeout = 60_000 });
            await restore.ClickAsync();
            await hidden.Locator("textarea[id^='restore-statement-']").FillAsync("Bandymas: atkuriama.");
            await hidden.Locator("form button[type=submit]").ClickAsync();
            await Assertions.Expect(hidden.GetByText("Atkurta.")).ToBeVisibleAsync();
            await w.ReloadAsync();
            await Assertions.Expect(w.Locator("#side-picker")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await ShowAllReasonsAsync(w);
            await Assertions.Expect(w.Locator("#reasons-against .said").Filter(new() { HasText = waitingText })).ToBeVisibleAsync();

        }
        finally
        {
            await RestoreIfHiddenAsync(mod, Bill);
        }

        writer.ConsoleErrors.ShouldBeEmpty();
        await TakeBackAsync(w, Bill);
    }

    /// <summary>
    /// Design 8.13 on the demonstration AI summary (migrator --demo-appeal gives XVP-0533 an invented description with the
    /// source <c>ai</c>): a member sees "Sukurta DI · žmonių neperžiūrėta" and reports an error; the report reaches the
    /// moderators' queue, where a moderator confirms the text; the label becomes "Sukurta DI · taisė žmonės". A second report
    /// shows in the "Moderavimas" panel on the bill's page, where the moderator corrects the description with the official
    /// text's address (decided by the owner on 2026-11-14), refused first for a host that is not lrs.lt; the page then links
    /// "Oficialus tekstas, iš kurio parengta". Neither can be taken back through the product, so the test puts the demonstration summary
    /// back itself, before and after (<see cref="ResetDemonstrationSummaryAsync"/>).
    /// </summary>
    [Fact]
    public async Task A_member_reports_the_ai_summary_a_moderator_confirms_it_from_the_queue_and_corrects_it_with_its_address_on_the_page()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        const string Bill = "XVP-0533";
        await E2EData.RestoreDescriptionAsync(Bill);
        try
        {
            await ReportConfirmAndCorrectAsync();
        }
        finally
        {
            // The demonstration summary goes back to "nobody has read it" for the next test and for people using the stack.
            await E2EData.RestoreDescriptionAsync(Bill);
        }
    }

    private async Task ReportConfirmAndCorrectAsync()
    {
        const string Bill = "XVP-0533";
        var mod = await moderator.PageAsync();

        await using var reader = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var r = reader.Page;
        await PasskeyTests.RegisterAsync(r, "Klaidą Radusi Skaitytoja");
        await r.GotoAsync($"{Base}/seimas/{Bill}");
        await Assertions.Expect(r.Locator("#ai-label")).ToHaveTextAsync("Sukurta DI · žmonių neperžiūrėta", new() { Timeout = 10_000 });
        await Assertions.Expect(r.Locator("#description-from")).ToHaveAttributeAsync("rel", "external");
        await Assertions.Expect(r.Locator("#description-made")).ToContainTextAsync("Parengta");
        await Assertions.Expect(r.Locator("#moderation-panel")).ToHaveCountAsync(0);

        // "Pranešti apie klaidą" beside the description; the description's own ground comes first.
        var report = r.Locator("#about #report-appeal");
        await Assertions.Expect(report).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await report.ClickAsync();
        await r.GetByLabel("Aprašymas netikslus ar klaidingas").CheckAsync();
        var run = DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        await r.Locator("#about textarea").FillAsync($"Rinkliavų dydžių skelbimo terminas kitas ({run}).");
        await r.GetByRole(AriaRole.Button, new() { Name = "Pranešti moderatoriams" }).ClickAsync();
        await Assertions.Expect(r.GetByText("Ačiū. Moderatoriai peržiūrės.")).ToBeVisibleAsync();

        // The moderators' queue: the bill with its AI summary, its label and the report; "Patvirtinti aprašymą" first.
        await mod.GotoAsync($"{Base}/moderavimas");
        var item = mod.Locator("article[id^='queue-appeal-']").Filter(new() { HasText = Bill });
        await Assertions.Expect(item).ToContainTextAsync("Sukurta DI · žmonių neperžiūrėta", new() { Timeout = 60_000 });
        await Assertions.Expect(item).ToContainTextAsync("Aprašymas netikslus ar klaidingas");
        await Assertions.Expect(item).ToContainTextAsync(run);
        // Design 8.3 (owner, 2026-11-17): reported, so "Palikti" is the filled button; "Patvirtinti aprašymą" heads "Aprašymas" in "Kiti veiksmai".
        await Assertions.Expect(item.Locator("button.btn[id^='action-keep-']")).ToHaveTextAsync("Palikti", new() { Timeout = 60_000 });
        await Assertions.Expect(item.Locator(".menu__group").First.Locator("button.menu__item").First).ToHaveTextAsync("Patvirtinti aprašymą");
        await E2ESteps.NoSidewaysScrollAsync(mod);
        await ActAsync(item, "confirm");
        await Assertions.Expect(item.GetByText("Atlikta: aprašymas patvirtintas.")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(item.Locator("button[id^='action-confirm-']")).ToHaveCountAsync(0);

        // The page now says people have read it.
        await r.ReloadAsync();
        await Assertions.Expect(r.Locator("#ai-label")).ToHaveTextAsync("Sukurta DI · taisė žmonės");

        // Decided by the owner on 2026-11-14: a second report is shown in the "Moderavimas" panel on the bill's page, as the
        // queue shows it, and the moderator corrects the description there with the official text's address.
        var report2 = r.Locator("#about #report-appeal");
        await Assertions.Expect(report2).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await report2.ClickAsync();
        await r.GetByLabel("Aprašymas netikslus ar klaidingas").CheckAsync();
        var said = $"Aprašyme neminima, nuo kada taikoma ({run}).";
        await r.Locator("#about textarea").FillAsync(said);
        await r.GetByRole(AriaRole.Button, new() { Name = "Pranešti moderatoriams" }).ClickAsync();
        await Assertions.Expect(r.GetByText("Ačiū. Moderatoriai peržiūrės.")).ToBeVisibleAsync();

        await mod.GotoAsync($"{Base}/seimas/{Bill}");
        var panel = mod.Locator("#moderation-panel");
        var reports = panel.Locator("#panel-description-reports");
        await Assertions.Expect(reports).ToContainTextAsync(said, new() { Timeout = 60_000 });
        await Assertions.Expect(reports).ToContainTextAsync("Aprašymas netikslus ar klaidingas");
        await Assertions.Expect(reports.Locator("li span.num").Last).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$"));
        (await mod.ContentAsync()).ShouldNotContain("Klaidą Radusi Skaitytoja", customMessage: "who reported is never shown");
        // The panel is an island: the screenshot waits until it has started (its actions are enabled), or it can catch
        // the server-rendered section just as the island replaces it ("Node is either not visible").
        var describe = panel.Locator("button[id^='action-describe-']");
        await Assertions.Expect(describe).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await panel.ScrollIntoViewIfNeededAsync();
        await panel.ScreenshotAsync(new() { Path = Shot("panel-description-report-1280.png") });

        // The form starts from the address the record has; a refused address is said at the field and nothing is saved.
        await E2ESteps.ChooseAsync(describe);
        var address = panel.Locator("input[id^='describe-url-']");
        await Assertions.Expect(address).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("^https://e-seimas\\.lrs\\.lt/"));
        var corrected = $"Projektas leistų savivaldybėms nustatyti vietinių rinkliavų dydį pagal sąnaudas; taikoma nuo 2027 metų ({run}). {Marker}";
        await panel.Locator("textarea[id^='describe-']:not([id^='describe-source'])").FillAsync(corrected);
        await panel.Locator("textarea[id^='statement-']").FillAsync("Pataisyta pagal narės pranešimą.");
        await address.FillAsync("https://lrs.lt.evil.example/bandymas");
        await panel.Locator("button[id^='action-submit-']").ClickAsync();
        await Assertions.Expect(panel.Locator(".field--error:has(input[id^='describe-url-'])")).ToContainTextAsync("Nurodykite oficialaus teksto adresą lrs.lt svetainėje");
        await panel.Locator("form[id^='action-form-']").ScreenshotAsync(new() { Path = Shot("description-form-refused-address-1280.png") });

        // The same form at 390 px: nothing scrolls sideways.
        await mod.SetViewportSizeAsync(390, 844);
        await E2ESteps.NoSidewaysScrollAsync(mod);
        await panel.Locator("form[id^='action-form-']").ScreenshotAsync(new() { Path = Shot("description-form-390.png") });
        await mod.SetViewportSizeAsync(1280, 800);

        var official = $"https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas-{run}";
        await address.FillAsync(official);
        await panel.Locator("button[id^='action-submit-']").ClickAsync();
        await Assertions.Expect(mod.Locator("#about")).ToContainTextAsync(corrected, new() { Timeout = 30_000 });
        await Assertions.Expect(mod.Locator("#panel-description-reports")).ToHaveCountAsync(0, new() { Timeout = 30_000 });

        // A visitor's view: the corrected text, still "Sukurta DI · taisė žmonės", and the official text it was made from.
        await r.ReloadAsync();
        await Assertions.Expect(r.Locator("#about")).ToContainTextAsync(corrected);
        await Assertions.Expect(r.Locator("#ai-label")).ToHaveTextAsync("Sukurta DI · taisė žmonės");
        var from = r.Locator("#description-from");
        await Assertions.Expect(from).ToHaveTextAsync("Oficialus tekstas, iš kurio parengta");
        await Assertions.Expect(from).ToHaveAttributeAsync("href", official);
        await Assertions.Expect(r.Locator("#moderation-panel")).ToHaveCountAsync(0);
        await r.Locator("#about").ScreenshotAsync(new() { Path = Shot("visitor-after-save-1280.png") });
        reader.ConsoleErrors.ShouldBeEmpty();
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
    public async Task The_public_bill_page_reads_without_javascript()
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

        // The demonstration bill (migrator --demo-appeal) on XVP-0412; the old address redirects to it.
        await page.GotoAsync($"{Base}/seimas/XVP-0412/kreipimasis");
        new Uri(page.Url).AbsolutePath.ShouldBe("/seimas/XVP-0412");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Gyventojų pajamų mokesčio įstatymo pataisa");
        await Assertions.Expect(page.Locator("#count-for .figure__num")).ToHaveTextAsync("6 800");
        await Assertions.Expect(page.Locator("#count-against .figure__num")).ToHaveTextAsync("4 280");
        // The invented accounts age day by day, so the older-account counts grow from the seeded 6 150 and 3 900.
        await Assertions.Expect(page.Locator("#count-for")).ToContainTextAsync(new Regex("\\d\\s\\d{3}\\s–\\spaskyros"));
        await Assertions.Expect(page.Locator("#count-against")).ToContainTextAsync(new Regex("\\d\\s\\d{3}\\s–\\spaskyros"));
        // The description and its label (on a database from before 2026-11-14 the demonstration's initiator wrote it).
        await Assertions.Expect(page.Locator("#description-source")).ToContainTextAsync(new System.Text.RegularExpressions.Regex("Parašė moderatorius|Aprašymą parašė"));
        await Assertions.Expect(page.GetByText("Didžiausias pajamas gaunantys dabar moka mažesnį efektyvųjį tarifą nei samdomi darbuotojai.")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Man svarbu · 1 920")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#not-a-poll")).ToHaveTextAsync("Tai nėra reprezentatyvi apklausa: skaičiai rodo savanoriškai dalyvaujančių Lietuva 2.0 naudotojų nuomonę.");
        // Design 6.9: the sign-in link brings the visitor back to the bill.
        await Assertions.Expect(page.Locator("a.btn--for")).ToHaveAttributeAsync("href", "/prisijungti#tikslas=%2Fseimas%2FXVP-0412");

        // Reporting without JavaScript and without a session: the disclosure opens and says how.
        await page.Locator("#about").GetByText("Pranešti apie klaidą", new() { Exact = true }).ClickAsync();
        // Beside the description (design 8.13); the arguments' own disclosures stay closed.
        await Assertions.Expect(page.Locator("#about").GetByText("Pranešti apie turinį moderatoriams gali prisijungę Lietuva 2.0 naudotojai:")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#about a[href='mailto:moderatoriai@lt2.localhost']")).ToBeVisibleAsync();
        await E2ESteps.NoSidewaysScrollAsync(page);
        await page.ScreenshotAsync(new() { Path = Shot("bill-no-javascript-390.png"), FullPage = true });

        await page.GotoAsync($"{Base}/projektai");
        await Assertions.Expect(page.Locator("#appeals a[href='/seimas/XVP-0412']")).ToBeVisibleAsync();
        await page.GotoAsync($"{Base}/projektai?rikiuoti=data");
        await Assertions.Expect(page.Locator("#sort-data")).ToHaveTextAsync("pagal planuojamą datą");
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_summary_of_the_demonstration_bill_reads_without_javascript_and_names_nobody(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        // A moderator approves one urgent summary for XVP-0412 (a dry run). One a week per bill: a 409 means this week's is
        // already there (made by people using the stack), and it is read instead; one this test made is deleted afterwards.
        var mod = await moderator.PageAsync();
        var (record, recordBody) = await E2ESteps.ApiAsync(mod, "GET", "/api/v1/bills/XVP-0412/record");
        record.ShouldBe(200, recordBody);
        var recordId = JsonDocument.Parse(recordBody).RootElement.GetProperty("id").GetString();
        var (status, body) = await E2ESteps.ApiAsync(mod, "POST", $"/api/v1/moderation/records/{recordId}/urgent", new { statement = "Demonstracinė suvestinė." });
        status.ShouldBeOneOf([201, 409], body);
        var made = status == 201 ? JsonDocument.Parse(body).RootElement.GetProperty("id").GetString() : null;
        try
        {
            await ReadSummaryWithoutJavaScriptAsync(width, height);
        }
        finally
        {
            if (made is not null)
            {
                await E2EData.DeleteSummaryAsync(Guid.Parse(made));
            }
        }
    }

    private static async Task ReadSummaryWithoutJavaScriptAsync(int width, int height)
    {
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
        await Assertions.Expect(page.GetByText("6 800").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#not-a-poll")).ToContainTextAsync("Tai nėra reprezentatyvi apklausa");
        (await page.ContentAsync()).ShouldNotContain("Rūta Mockutė", customMessage: "a summary names nobody");
        await E2ESteps.NoSidewaysScrollAsync(page);
        await page.ScreenshotAsync(new() { Path = Shot($"summary-no-javascript-{width}.png"), FullPage = true });

        // The summaries' links to bills still work, and the bill lists the summary.
        await page.GotoAsync($"{Base}/seimas/XVP-0412");
        await Assertions.Expect(page.Locator("#planned")).ToContainTextAsync("Planuojama");
        await Assertions.Expect(page.Locator("#digests a").First).ToBeVisibleAsync();
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
        await w.GotoAsync($"{Base}/seimas/XVP-0412");
        var add = w.Locator("#add-reason");
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();
        await w.Locator("#reason-side .switch__half--against").ClickAsync();
        await w.Locator("#reason-text").FillAsync(Text);
        await PublishReasonAsync(w);

        // The moderator opens the author's screen from the queue and restricts until lifted.
        await mod.GotoAsync($"{Base}/moderavimas");
        // The item whose own argument this is (another item may list it as a merge candidate).
        var item = mod.Locator("article").Filter(new() { Has = mod.Locator(".said > p.bubble", new() { HasTextString = Text }) });
        await Assertions.Expect(item).ToBeVisibleAsync(new() { Timeout = 60_000 });
        // "Riboti autoriaus rašymą" is one of the item's actions (design 8.3); the length is the two-part switch.
        await E2ESteps.ChooseAsync(item.Locator("button[id^='action-restrict-']"));
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
        await E2ESteps.NoSidewaysScrollAsync(w);
        await w.ScreenshotAsync(new() { Path = Shot("author-restricted-1280.png"), FullPage = true });
        author.ConsoleErrors.ShouldBeEmpty();

        // Clean up: lift the restriction and hide the argument, so the demonstration bill stays as it was.
        var statuses = await mod.EvaluateAsync<string>(
            """
            async (text) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const post = (url, body) => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }, body: JSON.stringify(body) });
              const out = [];
              for (const r of await (await fetch('/api/v1/moderation/restrictions')).json())
                if (r.displayName === 'Ribojama Autorė') out.push((await post('/api/v1/moderation/restrictions/' + r.id + '/lift', { statement: 'Bandymo pabaiga.' })).status);
              const record = await (await fetch('/api/v1/bills/XVP-0412/record')).json();
              for (const r of record.reasons)
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

    /// <summary>Presses "Paskelbti"; an earlier run's argument may be offered as similar first, then publishes anyway.</summary>
    private static async Task PublishReasonAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Paskelbti", Exact = true }).ClickAsync();
        var published = page.GetByText("Argumentas paskelbtas.");
        var anyway = page.GetByRole(AriaRole.Button, new() { Name = "Vis tiek paskelbti savo" });
        await Assertions.Expect(published.Or(anyway)).ToBeVisibleAsync();
        if (await anyway.IsVisibleAsync())
        {
            await anyway.ClickAsync();
        }

        await Assertions.Expect(published).ToBeVisibleAsync();
    }

    /// <summary>
    /// Shows every argument when the list shows only the first ones per side ("Visi argumentai: N"), once the island runs.
    /// </summary>
    private static async Task ShowAllReasonsAsync(IPage page)
    {
        var all = page.Locator("#reasons button.linkish").Filter(new() { HasTextRegex = new Regex("^Visi argumentai") });
        if (await all.CountAsync() > 0)
        {
            await Assertions.Expect(all).ToBeEnabledAsync(new() { Timeout = 60_000 });
            await all.ClickAsync();
            await Assertions.Expect(page.Locator("#reasons button.linkish").Filter(new() { HasText = "Rodyti mažiau" })).ToBeVisibleAsync();
        }
    }

    /// <summary>The number shown in a side's figure, as a number.</summary>
    private static async Task<int> CountAsync(IPage page, string side) =>
        int.Parse(Regex.Replace(await page.Locator($"#count-{side} .figure__num").InnerTextAsync(), @"\s", ""), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A number as the pages write it: thousands separated by a no-break space.</summary>
    private static string Number(int value) =>
        value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ", StringComparison.Ordinal);

    /// <summary>Writes an argument through the API as the signed-in person; returns its status (published or pending).</summary>
    private static Task<string> WriteViaApiAsync(IPage page, string bill, string side, string text) =>
        page.EvaluateAsync<string>(
            """
            async ([bill, side, text]) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const r = await fetch('/api/v1/bills/' + bill + '/record/reasons', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
                body: JSON.stringify({ side, text }) });
              return (await r.json()).status;
            }
            """,
            new[] { bill, side, text });

    /// <summary>Restores the bill's record through the API if it is among the hidden items; used in a <c>finally</c>.</summary>
    private static async Task RestoreIfHiddenAsync(IPage moderatorPage, string bill)
    {
        var statuses = await moderatorPage.EvaluateAsync<int[]>(
            """
            async (bill) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const hidden = await (await fetch('/api/v1/moderation/hidden')).json();
              const out = [];
              for (const a of hidden.records ?? [])
                if (JSON.stringify(a).includes(bill))
                  out.push((await fetch('/api/v1/moderation/records/' + a.id + '/restore', { method: 'POST',
                    headers: { 'X-XSRF-TOKEN': token, 'Content-Type': 'application/json' }, body: JSON.stringify({ statement: 'Bandymo pabaiga.' }) })).status);
              return out;
            }
            """,
            bill);
        statuses.ShouldAllBe(status => status == 204);
    }

    /// <summary>
    /// Takes back, through the API, what the signed-in person did on the bill: their side is cleared and their arguments
    /// withdrawn, so the bill's numbers are as they were. Nobody else's side or argument is touched.
    /// </summary>
    private static async Task TakeBackAsync(IPage page, string bill)
    {
        var json = await page.EvaluateAsync<string>(
            """
            async (bill) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const del = url => fetch(url, { method: 'DELETE', headers: { 'X-XSRF-TOKEN': token } });
              const out = [(await del('/api/v1/bills/' + bill + '/record/position')).status];
              const mine = await (await fetch('/api/v1/bills/' + bill + '/record/mine')).json();
              for (const r of mine.myReasons) if (r.status === 'published' || r.status === 'pending') out.push((await del('/api/v1/reasons/' + r.id)).status);
              return JSON.stringify(out);
            }
            """,
            bill);
        JsonSerializer.Deserialize<int[]>(json)!.ShouldAllBe(status => status == 204);
    }

    private static string Shot(string name)
    {
        Directory.CreateDirectory(Path.Combine("output", "appeals"));
        return Path.Combine("output", "appeals", name);
    }

    /// <summary>
    /// Every description a test writes carries this, so a person reading the local stack can tell the tests' text apart.
    /// </summary>
    internal const string Marker = "(E2E bandymas)";

    /// <summary>An item's action, the filled button or one in "Kiti veiksmai": it opens only its own fields; then its submit button.</summary>
    private static async Task ActAsync(ILocator item, string action, string? statement = null)
    {
        await E2ESteps.ChooseAsync(item.Locator($"button[id^='action-{action}-']").First);
        await Assertions.Expect(item.Locator("p[id^='action-sentence-']").First).ToBeVisibleAsync();
        if (statement is not null)
        {
            await item.Locator("textarea[id^='statement-']").First.FillAsync(statement);
        }

        await item.Locator("button[id^='action-submit-']").First.ClickAsync();
    }

    /// <summary>
    /// Under each argument one action row (design 8.16.1): "Man svarbu", "Aptarimas" and "⋯" on one line, one height of at
    /// least 44 px, inside their column, under the name and on the name's left edge.
    /// </summary>
    internal static async Task ActionsOnOneLineAsync(IPage page)
    {
        var report = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('#reasons .said > .act')].flatMap(row => {
              const items = [...row.children].filter(c => c.matches('.mark, .discuss, .act__more'));
              const boxes = items.map(c => c.getBoundingClientRect());
              const mids = boxes.map(b => Math.round(b.top + b.height / 2));
              const said = row.closest('.said');
              const name = said.querySelector('.said__name')?.getBoundingClientRect();
              const out = [];
              if (items.length !== 3) out.push(row.id + ': ' + items.length + ' items');
              if (mids.length > 1 && Math.max(...mids) - Math.min(...mids) > 3) out.push(row.id + ': the row wraps (' + mids.join(',') + ')');
              if (boxes.some(b => b.height < 43.5)) out.push(row.id + ': an item lower than 44 px');
              if (Math.max(...boxes.map(b => b.right)) > said.getBoundingClientRect().right + 1) out.push(row.id + ': the row overflows its column');
              if (name && boxes[0].top < name.bottom - 1) out.push(row.id + ': the row is not under the name');
              if (name && Math.abs(boxes[0].left - name.left) > 1) out.push(row.id + ': not on the name\'s left edge');
              return out;
            })
            """);
        report.ShouldBeEmpty();
        (await page.Locator("#reasons .said > .act").CountAsync()).ShouldBeGreaterThan(0, "the check is not empty");
    }

    /// <summary>One moderator for the class: registered, given a passkey, granted the role by the migrator, activated.</summary>
    public sealed class ModeratorFixture() : StaffFixture("moderator", "Moderatorius M.");
}

internal static class StatusSteps
{
    /// <summary>Awaits a status and checks it.</summary>
    public static async Task ShouldBeAsync(this Task<string> status, string expected) => (await status).ShouldBe(expected);
}
