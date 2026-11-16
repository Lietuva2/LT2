using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 6.2 (decided 2026-11-13): a member's invitation link opens the registration form with the code filled in;
/// once the invited person completes registration, the member's count rises. The account page shows the code without
/// JavaScript.
/// </summary>
public sealed class InvitationFlowTests : E2ETest
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Fact]
    public async Task A_members_link_opens_registration_with_the_code_and_the_count_rises_when_the_invited_person_completes()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var a = member.Page;
        await PasskeyTests.RegisterAsync(a, "Kviečianti Narė");
        await a.GotoAsync($"{Base}/sveiki/dalyvavimas");
        var link = a.Locator("#invite-link");
        await Assertions.Expect(link).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(a.Locator("#invite-count")).ToHaveTextAsync("Su jūsų kodu užsiregistravo: 0.");
        var url = await link.InnerTextAsync();
        var code = await a.Locator("#invite-code").InnerTextAsync();
        url.ShouldEndWith("/prisijungti#kodas=" + code, Case.Sensitive, "design 6.8: the code after the #, never sent by the browser");
        await a.ScreenshotAsync(new() { Path = Shot("invitation-account-1280.png"), FullPage = true });

        // The invited person follows the link: registration, with the code filled in and the plain notice.
        await using var invited = await PasskeyBrowser.StartAsync("native", 390, 844);
        var b = invited.Page;
        var email = E2ESteps.NewEmail("pakviesta");
        await b.GotoAsync(url);
        await SignUpTests.WaitForInteractivityAsync(b);
        await Assertions.Expect(b.GetByLabel("Kvietimo kodas")).ToHaveValueAsync(code);
        await Assertions.Expect(b.GetByText("Kodą davęs žmogus matys tik skaičių, ne jus.")).ToBeVisibleAsync();
        await E2ESteps.RegisterHereAsync(b, email, "Pakviesta Asmenybė");
        await b.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });

        // The inviter's count rises; nothing names the invited person.
        await a.ReloadAsync();
        await Assertions.Expect(a.Locator("#invite-count")).ToHaveTextAsync("Su jūsų kodu užsiregistravo: 1.", new() { Timeout = 30_000 });
        (await a.ContentAsync()).ShouldNotContain("Pakviesta Asmenybė");
        member.ConsoleErrors.ShouldBeEmpty();
        invited.ConsoleErrors.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_account_page_shows_the_code_and_count_without_javascript()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 800);
        var a = member.Page;
        await PasskeyTests.RegisterAsync(a, "Be Scenarijų Narė");
        var cookies = await a.Context.CookiesAsync();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new() { JavaScriptEnabled = false, IgnoreHTTPSErrors = true, Locale = "lt-LT" });
        await context.AddCookiesAsync(cookies.Select(c => new Cookie { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, Secure = c.Secure, HttpOnly = c.HttpOnly, SameSite = c.SameSite, Expires = c.Expires }));
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{Base}/sveiki/dalyvavimas");
        await Assertions.Expect(page.Locator("#invite-code")).ToHaveTextAsync(new Regex("^[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$"));
        await Assertions.Expect(page.Locator("#invite-count")).ToHaveTextAsync("Su jūsų kodu užsiregistravo: 0.");
    }

    private static string Shot(string name)
    {
        Directory.CreateDirectory(Path.Combine("output", "invitations"));
        return Path.Combine("output", "invitations", name);
    }
}
