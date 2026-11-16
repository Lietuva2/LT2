using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Passkeys through a real browser (design 6.2), with Chromium's virtual WebAuthn authenticator over CDP: add a passkey
/// on the hello page, sign out, sign in with it. The same flow also runs with <c>navigator.credentials</c> wrapped the
/// way a password manager's extension does it, where the returned credential's <c>toJSON()</c> leaves out
/// <c>clientExtensionResults</c> or throws: ASP.NET Core Identity requires that property, so the page must not rely on
/// <c>toJSON()</c>.
/// </summary>
public sealed partial class PasskeyTests : E2ETest
{
    /// <summary>How <c>navigator.credentials</c> behaves in the browser.</summary>
    public static TheoryData<string> Browsers() => new("native", "password-manager-without-extension-results", "password-manager-toJSON-throws");

    [Theory]
    [MemberData(nameof(Browsers))]
    public async Task A_person_adds_a_passkey_on_the_hello_page_and_signs_in_with_it(string browserKind)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        await using var session = await PasskeyBrowser.StartAsync(browserKind);
        var page = session.Page;
        var email = await RegisterAsync(page);

        await AddPasskeyAsync(page);

        // A second attempt with the same authenticator: the server lists the existing key in excludeCredentials, so the
        // authenticator refuses instead of making a duplicate.
        await page.GetByRole(AriaRole.Button, new() { Name = "Sukurti prieigos raktą" }).ClickAsync();
        await Assertions.Expect(page.Locator(".note[role=alert]")).ToHaveTextAsync("Prieigos rakto naudojimas atšauktas arba nepavyko.");
        await Assertions.Expect(page.GetByText("Sukurta prieigos raktų: 1")).ToBeVisibleAsync();

        await page.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Atsijungti" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/$"));
        await SignInWithPasskeyAsync(page);

        var current = page.Locator(".rows li").Filter(new() { HasText = "Ši sesija" });
        await Assertions.Expect(current).ToContainTextAsync("prisijungta prieigos raktu");
        (await MeAsync(page)).GetProperty("email").GetString().ShouldBe(email);
        session.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Signing_in_with_a_passkey_this_site_never_stored_says_it_did_not_work()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        // Like a passkey a password manager saved although the server refused it: the browser offers it, the server
        // does not know it.
        await using var session = await PasskeyBrowser.StartAsync("native");
        await session.AddUnknownPasskeyAsync("lt2.localhost");
        var page = session.Page;
        await page.GotoAsync(SignUpTests.BaseUrl!.TrimEnd('/') + "/prisijungti");
        await SignUpTests.WaitForInteractivityAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti prieigos raktu" }).ClickAsync();

        await Assertions.Expect(page.Locator(".note[role=alert]")).ToHaveTextAsync("Prieigos rakto patikrinti nepavyko.");
        page.Url.ShouldEndWith("/prisijungti");
    }

    [Fact]
    public async Task When_the_server_cannot_verify_a_new_passkey_the_page_says_it_was_not_added_and_may_be_tried_again()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        await using var session = await PasskeyBrowser.StartAsync("native");
        var page = session.Page;
        await RegisterAsync(page);

        // The authenticator saves the key, but what reaches the server cannot be verified.
        await page.RouteAsync("**/api/v1/auth/passkeys", async route =>
        {
            using var body = JsonDocument.Parse(route.Request.PostData!);
            var credential = JsonDocument.Parse(body.RootElement.GetProperty("credential").GetString()!).RootElement;
            var broken = credential.ToString().Replace(
                credential.GetProperty("response").GetProperty("clientDataJSON").GetString()!, "e30", StringComparison.Ordinal);
            await route.ContinueAsync(new() { PostData = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { credential = broken })) });
        });
        var add = page.GetByRole(AriaRole.Button, new() { Name = "Sukurti prieigos raktą" });
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();

        await Assertions.Expect(page.Locator(".note[role=alert]")).ToHaveTextAsync(
            "Prieigos rakto patikrinti nepavyko. Prie šios paskyros jis nepridėtas, net jei naršyklė ar slaptažodžių tvarkyklė jį išsaugojo; galite bandyti dar kartą.");
        await Assertions.Expect(page.GetByText("Sukurta prieigos raktų: 0")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_granted_staff_role_is_activated_with_the_code_in_a_passkey_session()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        await using var session = await PasskeyBrowser.StartAsync("native");
        var page = session.Page;
        var email = await RegisterAsync(page);
        await AddPasskeyAsync(page);
        await page.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Atsijungti" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/$"));
        await SignInWithPasskeyAsync(page);
        (await MeAsync(page)).GetProperty("activeStaffRoles").GetArrayLength().ShouldBe(0);

        // The server grants the role and prints the code once; the administrator passes it on in person.
        var output = await Compose.RunAsync("run", "--rm", "--no-deps", "migrator", "--grant-role", "moderator", email);
        var code = ActivationCode().Match(output).Value;
        code.ShouldNotBeNullOrEmpty("the migrator prints the activation code");

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Darbuotojo vaidmens aktyvinimas" })).ToBeVisibleAsync();
        await SignUpTests.WaitForInteractivityAsync(page);
        await page.GetByLabel("Aktyvinimo kodas").FillAsync(code.ToLowerInvariant());
        await page.GetByRole(AriaRole.Button, new() { Name = "Aktyvinti" }).ClickAsync();

        await Assertions.Expect(page.GetByText("Vaidmuo aktyvintas.")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Aktyvinimo kodas")).ToHaveCountAsync(0);
        var me = await MeAsync(page);
        me.GetProperty("activeStaffRoles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["moderator"]);
        me.GetProperty("passkeyCount").GetInt32().ShouldBe(1);
        session.ConsoleErrors.ShouldBeEmpty();
    }

    /// <summary>Registers through the pages: address and invitation, the emailed code, then name and age.</summary>
    internal static async Task<string> RegisterAsync(IPage page, string displayName = "Rūta M.")
    {
        var email = E2ESteps.NewEmail();
        await page.GotoAsync(E2ESteps.Base + "/prisijungti?registruotis=true");
        await E2ESteps.RegisterHereAsync(page, email, displayName, SignUpTests.Invitation);
        await page.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });
        await Assertions.Expect(page.GetByText("prisijungta kodu")).ToBeVisibleAsync();
        return email;
    }

    internal static async Task AddPasskeyAsync(IPage page)
    {
        var add = page.GetByRole(AriaRole.Button, new() { Name = "Sukurti prieigos raktą" });
        await Assertions.Expect(add).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await add.ClickAsync();
        await Assertions.Expect(page.GetByText("Prieigos raktas sukurtas.")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Sukurta prieigos raktų: 1")).ToBeVisibleAsync();
    }

    internal static async Task SignInWithPasskeyAsync(IPage page)
    {
        await page.GotoAsync(SignUpTests.BaseUrl!.TrimEnd('/') + "/prisijungti");
        await SignUpTests.WaitForInteractivityAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti prieigos raktu" }).ClickAsync();
        await page.WaitForURLAsync(new Regex("/sveiki$"));
        await TestAccounts.SessionOfAsync(page.Context);
    }

    internal static async Task<JsonElement> MeAsync(IPage page) =>
        JsonDocument.Parse(await page.EvaluateAsync<string>("async () => await (await fetch('/api/v1/me')).text()")).RootElement;

    [GeneratedRegex("[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}")]
    internal static partial Regex ActivationCode();
}

/// <summary>A headless browser with a virtual WebAuthn authenticator (user verification on), optionally password-manager-like.</summary>
internal sealed class PasskeyBrowser : IAsyncDisposable
{
    /// <summary>
    /// Wraps <c>navigator.credentials.create</c> and <c>.get</c> the way a password manager's extension does: the returned
    /// credential's <c>toJSON()</c> either drops <c>clientExtensionResults</c> or throws.
    /// </summary>
    private const string PasswordManagerScript = """
        (() => {
          const mode = "__MODE__";
          const wrap = (original) => async (...args) => {
            const credential = await original(...args);
            if (!credential) return credential;
            const toJSON = credential.toJSON ? credential.toJSON.bind(credential) : null;
            Object.defineProperty(credential, "toJSON", {
              configurable: true,
              value: mode === "password-manager-toJSON-throws"
                ? () => { throw new TypeError("toJSON is not supported by this credential"); }
                : () => { const json = toJSON(); delete json.clientExtensionResults; return json; },
            });
            return credential;
          };
          navigator.credentials.create = wrap(navigator.credentials.create.bind(navigator.credentials));
          navigator.credentials.get = wrap(navigator.credentials.get.bind(navigator.credentials));
        })();
        """;

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;

    private PasskeyBrowser(IPlaywright playwright, IBrowser browser, IPage page)
    {
        _playwright = playwright;
        _browser = browser;
        Page = page;
    }

    public IPage Page { get; }

    private ICDPSession? Cdp { get; set; }

    private string AuthenticatorId { get; set; } = "";

    /// <summary>Puts a discoverable passkey for <paramref name="rpId"/> on the virtual authenticator that no server has seen.</summary>
    public async Task AddUnknownPasskeyAsync(string rpId)
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        await Cdp!.SendAsync("WebAuthn.addCredential", new Dictionary<string, object>
        {
            ["authenticatorId"] = AuthenticatorId,
            ["credential"] = new Dictionary<string, object>
            {
                ["credentialId"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                ["isResidentCredential"] = true,
                ["rpId"] = rpId,
                ["privateKey"] = Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
                ["userHandle"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
                ["signCount"] = 0,
            },
        });
    }

    public List<string> ConsoleErrors { get; } = [];

    public static async Task<PasskeyBrowser> StartAsync(string kind, int width = 1280, int height = 900)
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = SignUpTests.Channel, Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true, // Caddy's local certificate authority is not trusted by the test browser.
            Locale = "lt-LT",
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        if (kind != "native")
        {
            await context.AddInitScriptAsync(PasswordManagerScript.Replace("__MODE__", kind, StringComparison.Ordinal));
        }

        var page = await context.NewPageAsync();
        var result = new PasskeyBrowser(playwright, browser, page);
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                result.ConsoleErrors.Add(message.Text);
            }
        };

        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("WebAuthn.enable", new Dictionary<string, object> { ["enableUI"] = false });
        var added = await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
        {
            ["options"] = new Dictionary<string, object>
            {
                ["protocol"] = "ctap2",
                ["transport"] = "internal",
                ["hasResidentKey"] = true,
                ["hasUserVerification"] = true,
                ["isUserVerified"] = true,
                ["automaticPresenceSimulation"] = true,
            },
        });
        result.Cdp = cdp;
        result.AuthenticatorId = added!.Value.GetProperty("authenticatorId").GetString()!;
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        // The session the browser ends with, for deleting its account after the test (TestAccounts), and its last screen,
        // kept if the test failed.
        await TestAccounts.SessionOfAsync(Page.Context);
        await E2ETest.KeepScreenAsync(Page);
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }
}
