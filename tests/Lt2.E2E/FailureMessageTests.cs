using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Every answer that is not a success ends in a visible Lithuanian message, on every step of signing in and on the
/// hello page. The failing answers are produced at the browser's network layer (Playwright routes), byte for byte as the
/// server and Caddy send them, so the real client parses them and the stack's per-IP limits are not used up.
/// </summary>
public sealed class FailureMessageTests : E2ETest
{
    private const string RateLimited = "Per daug bandymų. Palaukite ir bandykite vėliau.";
    private const string Generic = "Nepavyko. Bandykite dar kartą.";

    /// <summary>The body the rate limiter writes (SecurityServices.OnRejected), as application/problem+json with Retry-After.</summary>
    private static readonly RouteFulfillOptions TooManyRequests = new()
    {
        Status = 429,
        ContentType = "application/problem+json",
        Headers = new Dictionary<string, string> { ["Retry-After"] = "600" },
        Body = """{"type":"https://tools.ietf.org/html/rfc6585#section-4","title":"Per daug užklausų","status":429,"detail":"Per daug bandymų. Palaukite ir bandykite vėliau.","code":"rate-limited"}""",
    };

    /// <summary>What Caddy answers when the web container is down: no problem body at all.</summary>
    private static readonly RouteFulfillOptions BadGateway = new() { Status = 502, ContentType = "text/plain", Body = "" };

    public static TheoryData<string, string> Failures() => new()
    {
        { "429", RateLimited },
        { "502", Generic },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Asking_for_a_code_that_is_refused_says_why(string status, string expected)
    {
        await using var browser = await OpenAsync("/prisijungti");
        await Fail(browser.Page, "**/api/v1/auth/code", status);

        await browser.Page.GetByLabel("El. paštas").FillAsync("ruta.mockute@pavyzdys.test");
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Atsiųsti kodą" }).ClickAsync();

        await ExpectMessageAsync(browser.Page, expected);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_refused_registration_says_why(string status, string expected)
    {
        await using var browser = await OpenAsync("/prisijungti?registruotis=true");
        await Fail(browser.Page, "**/api/v1/auth/register", status);

        await browser.Page.GetByLabel("El. paštas").FillAsync("ruta.mockute@pavyzdys.test");
        await browser.Page.GetByLabel("Kvietimo kodas").FillAsync("BANDYMAS-2026");
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Registruotis", Exact = true }).ClickAsync();

        await ExpectMessageAsync(browser.Page, expected);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_refused_sign_in_with_a_code_says_why(string status, string expected)
    {
        await using var browser = await OpenAsync("/prisijungti");
        await browser.Page.RouteAsync("**/api/v1/auth/code", route => route.FulfillAsync(new() { Status = 202 }));
        await Fail(browser.Page, "**/api/v1/auth/sign-in", status);

        await browser.Page.GetByLabel("El. paštas").FillAsync("ruta.mockute@pavyzdys.test");
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Atsiųsti kodą" }).ClickAsync();
        await browser.Page.GetByLabel("Šešių skaitmenų kodas").FillAsync("123456");
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();

        await ExpectMessageAsync(browser.Page, expected);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_refused_passkey_sign_in_says_why(string status, string expected)
    {
        await using var browser = await OpenAsync("/prisijungti");
        await Fail(browser.Page, "**/api/v1/auth/passkeys/request-options", status);

        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti prieigos raktu" }).ClickAsync();

        await ExpectMessageAsync(browser.Page, expected);
    }

    private static async Task Fail(IPage page, string url, string status) =>
        await page.RouteAsync(url, route => route.FulfillAsync(status == "429" ? TooManyRequests : BadGateway));

    private static async Task ExpectMessageAsync(IPage page, string expected)
    {
        await Assertions.Expect(page.Locator(".note[role=alert]")).ToHaveTextAsync(expected);
        await Assertions.Expect(page.Locator("form button[type=submit]").First).ToBeEnabledAsync();
    }

    private static async Task<OpenPage> OpenAsync(string path)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = SignUpTests.Channel, Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true, Locale = "lt-LT" });
        var page = await context.NewPageAsync();
        await page.GotoAsync(SignUpTests.BaseUrl!.TrimEnd('/') + path);
        await SignUpTests.WaitForInteractivityAsync(page);
        return new OpenPage(playwright, browser, page);
    }

    private sealed record OpenPage(IPlaywright Playwright, IBrowser Browser, IPage Page) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await E2ETest.KeepScreenAsync(Page);
            await Browser.DisposeAsync();
            Playwright.Dispose();
        }
    }
}
