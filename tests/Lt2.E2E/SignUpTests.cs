using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// End to end against a running stack (docker compose): register with an invitation, receive the code in Mailpit,
/// sign in, complete the registration (name, age and the terms of use, design 6.2, 8.22.2) and see the hello page, at 1280 px and 390 px (design 10).
///
/// Runs only when <c>LT2_E2E_BASE_URL</c> is set, for example:
///   LT2_E2E_BASE_URL=https://lt2.localhost:8443  LT2_E2E_MAILPIT_URL=http://localhost:8025
///   LT2_E2E_BROWSER_CHANNEL=msedge (optional; default: Playwright's Chromium)
///   LT2_E2E_INVITATION (optional): an existing invitation; without it the run makes its own (<see cref="E2ERun"/>)
/// </summary>
public sealed partial class SignUpTests : E2ETest
{
    internal static readonly string? BaseUrl = Environment.GetEnvironmentVariable("LT2_E2E_BASE_URL");
    /// <summary>The run's invitation (<see cref="E2ERun"/>): its own, or <c>LT2_E2E_INVITATION</c>.</summary>
    internal static string Invitation => E2ERun.Invitation;
    internal static readonly string? Channel = Environment.GetEnvironmentVariable("LT2_E2E_BROWSER_CHANNEL");

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task Register_receive_the_code_in_mailpit_sign_in_and_see_the_hello_page(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true, // Caddy's local certificate authority is not trusted by the test browser.
            Locale = "lt-LT",
        });
        var page = await context.NewPageAsync();
        var consoleErrors = new List<string>();
        Directory.CreateDirectory("output");
        var consoleLog = Path.Combine("output", $"console-{width}.txt");
        File.WriteAllText(consoleLog, "");
        page.Console += (_, message) =>
        {
            // Kept for diagnosis: the browser console is the only place a client-side failure shows up.
            File.AppendAllText(consoleLog, $"{message.Type}: {message.Text}{Environment.NewLine}");
            if (message.Type == "error")
            {
                consoleErrors.Add(message.Text);
            }
        };

        var email = E2ESteps.NewEmail();

        await page.GotoAsync(BaseUrl!);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Lietuva");
        // The front page links to registration (design 8.7: the form itself is on /prisijungti).
        await page.GetByRole(AriaRole.Link, new() { Name = "Registruokitės", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/prisijungti"));

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Nauja paskyra" })).ToBeVisibleAsync();
        await WaitForInteractivityAsync(page);
        await ExpectNoNoteAsync(page);
        await page.GetByLabel("El. paštas").FillAsync(email);
        await page.GetByLabel("Kvietimo kodas").FillAsync(Invitation);
        (await page.GetByLabel("Vardas, kurį matys kiti").CountAsync()).ShouldBe(0, "the name is asked only after the code");
        await page.GetByRole(AriaRole.Button, new() { Name = "Registruotis", Exact = true }).ClickAsync();

        var codeField = page.GetByLabel("Šešių skaitmenų kodas");
        await Assertions.Expect(codeField).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Jei gavote kelis laiškus, tinka tik naujausias kodas.")).ToBeVisibleAsync();
        await ExpectNoNoteAsync(page);
        var code = await Mailpit.CodeAsync(email);
        await TestAccounts.RegisteredAsync(email, context);

        // A wrong code first: the refusal is shown in Lithuanian under the field, and no stray note appears.
        await codeField.FillAsync(code == "000000" ? "111111" : "000000");
        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#code-error")).ToHaveTextAsync("Kodas netinka arba nebegalioja. Paprašykite naujo.");
        await ExpectNoNoteAsync(page);

        await codeField.FillAsync(code);
        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();

        // The address is proven; only now are the name and the age declaration asked, with nothing filled in.
        await page.WaitForURLAsync(new Regex("/prisijungti$"), new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dar keli dalykai" })).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
        await WaitForInteractivityAsync(page);
        var name = page.GetByLabel("Vardas, kurį matys kiti");
        await Assertions.Expect(name).ToHaveValueAsync("");
        await Assertions.Expect(page.GetByLabel("Man yra 18 metų arba daugiau")).Not.ToBeCheckedAsync();
        var terms = page.GetByLabel("Sutinku su naudojimo taisyklėmis");
        await Assertions.Expect(terms).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#reg-terms-link")).ToHaveAttributeAsync("href", "/taisykles");
        await Assertions.Expect(page.Locator("#reg-privacy-link")).ToHaveAttributeAsync("href", "/privatumas");
        await name.FillAsync("Rūta M.");
        await page.GetByLabel("Man yra 18 metų arba daugiau").CheckAsync();

        // Design 8.22.2: without the terms box the registration does not finish.
        await page.GetByRole(AriaRole.Button, new() { Name = "Baigti registraciją" }).ClickAsync();
        await Assertions.Expect(page.Locator("#reg-terms-error")).ToHaveTextAsync("Kad galėtumėte užsiregistruoti, sutikite su naudojimo taisyklėmis.");
        await terms.CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Baigti registraciją" }).ClickAsync();

        await page.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });
        await TestAccounts.SessionOfAsync(context);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Sveiki, Rūta M.!");
        await Assertions.Expect(page.GetByText("El. paštas patvirtintas")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Ši sesija")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Atsijungti" })).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await ExpectNoNoteAsync(page);

        // The session cookie: host-only, secure, not readable by scripts.
        var session = (await context.CookiesAsync()).Single(c => c.Name == "__Host-lt2");
        session.Secure.ShouldBeTrue();
        session.HttpOnly.ShouldBeTrue();
        session.SameSite.ShouldBe(SameSiteAttribute.Lax);
        session.Path.ShouldBe("/");
        session.Domain.ShouldNotStartWith(".");

        await E2ESteps.NoSidewaysScrollAsync(page);

        Directory.CreateDirectory("output");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine("output", $"hello-{width}.png"), FullPage = true });

        consoleErrors.Where(e => e.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }

    /// <summary>Nothing has gone wrong, so no general note is shown, and no variable name leaks into the page.</summary>
    private static async Task ExpectNoNoteAsync(IPage page)
    {
        // main .note[role]: the general error and confirmation notes of the form. The <noscript> note has no role and,
        // with JavaScript on, is not part of the page at all. The site-wide notice that the parliament data is invented
        // (local stack) sits above <main> and is not a form note.
        await Assertions.Expect(page.Locator("main .note[role]")).ToHaveCountAsync(0);
        (await page.ContentAsync()).ShouldNotContain("_message");
    }

    /// <summary>
    /// The page's own form fields stay disabled until WebAssembly has taken over the prerendered page. Only the main
    /// content counts (the feedback balloon has a form too) and only a visible field (not the hidden antiforgery one),
    /// so a WebAssembly start that fails shows up here as what it is.
    /// </summary>
    internal static async Task WaitForInteractivityAsync(IPage page) =>
        await page.Locator("main form input:not([disabled]):not([type=hidden])").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
}
