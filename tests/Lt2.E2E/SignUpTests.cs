using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// End to end against a running stack (docker compose): register with an invitation, receive the code in Mailpit,
/// sign in and see the hello page, at 1280 px and 390 px (design 10).
///
/// Runs only when <c>LT2_E2E_BASE_URL</c> is set, for example:
///   LT2_E2E_BASE_URL=https://lt2.localhost:8443  LT2_E2E_MAILPIT_URL=http://localhost:8025
///   LT2_E2E_INVITATION=BANDYMAS-2026  LT2_E2E_BROWSER_CHANNEL=msedge (optional; default: Playwright's Chromium)
/// </summary>
public sealed partial class SignUpTests
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("LT2_E2E_BASE_URL");
    private static readonly string MailpitUrl = Environment.GetEnvironmentVariable("LT2_E2E_MAILPIT_URL") ?? "http://localhost:8025";
    private static readonly string Invitation = Environment.GetEnvironmentVariable("LT2_E2E_INVITATION") ?? "BANDYMAS-2026";
    private static readonly string? Channel = Environment.GetEnvironmentVariable("LT2_E2E_BROWSER_CHANNEL");

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
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                consoleErrors.Add(message.Text);
            }
        };

        var email = $"ruta.mockute.{Guid.NewGuid():N}@pavyzdys.test";

        await page.GotoAsync(BaseUrl!);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Lietuva");
        await page.GetByRole(AriaRole.Link, new() { Name = "Registruotis" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Registracija" })).ToBeVisibleAsync();
        await WaitForInteractivityAsync(page);
        await page.GetByLabel("El. pašto adresas").FillAsync(email);
        await page.GetByLabel("Kvietimo kodas").FillAsync(Invitation);
        await page.GetByLabel("Patvirtinu, kad man yra 18 metų ar daugiau.").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Registruotis", Exact = true }).ClickAsync();

        var codeField = page.GetByLabel("Kodas iš laiško");
        await Assertions.Expect(codeField).ToBeVisibleAsync();
        var code = await CodeFromMailpitAsync(email);
        await codeField.FillAsync(code);
        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();

        await page.WaitForURLAsync(new Regex("/sveiki$"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync($"Sveiki, {email}!");
        await Assertions.Expect(page.GetByText("El. paštas patvirtintas")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Ši sesija")).ToBeVisibleAsync();

        var scroll = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        scroll.ShouldBeLessThanOrEqualTo(0, "no horizontal scrolling");

        Directory.CreateDirectory("output");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine("output", $"hello-{width}.png"), FullPage = true });

        consoleErrors.Where(e => e.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }

    /// <summary>The form works only once WebAssembly has started; Blazor then marks the page interactive.</summary>
    private static async Task WaitForInteractivityAsync(IPage page) =>
        await page.WaitForFunctionAsync("() => window.Blazor !== undefined && document.querySelector('form') !== null && !!window.DotNet", null, new() { Timeout = 60_000 });

    private static async Task<string> CodeFromMailpitAsync(string email)
    {
        using var http = new HttpClient { BaseAddress = new Uri(MailpitUrl) };
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var search = await http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{email}\"")}");
            var messages = search.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var id = messages[0].GetProperty("ID").GetString();
                var message = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}");
                messages[0].GetProperty("Subject").GetString().ShouldStartWith("Lietuva 2.0 prisijungimo kodas");
                return SixDigits().Match(message.GetProperty("Text").GetString()!).Value;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"No email for {email} arrived in Mailpit.");
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}
