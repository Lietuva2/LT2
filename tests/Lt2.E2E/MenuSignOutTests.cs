using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// "Atsijungti" in the menu (owner, 2026-11-13), since design 8.21 inside the account item "Paskyra" with "Mano paskyra", for a
/// signed-in person at every width: the item opens and signing out works with JavaScript switched off.
/// </summary>
public sealed class MenuSignOutTests
{
    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_member_signs_out_from_the_menu_without_javascript(int width, int height)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        // Signing in needs a passkey or a code (JavaScript); the session is then carried into a browser without it.
        await using var member = await PasskeyBrowser.StartAsync("native", width, height);
        await PasskeyTests.RegisterAsync(member.Page, "Atsijungianti Narė");
        var state = await member.Page.Context.StorageStateAsync();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            JavaScriptEnabled = false,
            IgnoreHTTPSErrors = true,
            Locale = "lt-LT",
            StorageState = state,
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{Base}/seimas/XVP-0412");

        var nav = page.Locator("nav.top__nav");
        var item = page.Locator("#account-menu");
        var signOut = nav.GetByRole(AriaRole.Button, new() { Name = "Atsijungti" });
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Prisijungti" })).ToHaveCountAsync(0);
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Mano paskyra" })).ToBeHiddenAsync();
        await Assertions.Expect(signOut).ToBeHiddenAsync();

        // The browser's own disclosure: no script needed to open it.
        await page.Locator("#account-toggle").ClickAsync();
        await Assertions.Expect(item).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Mano paskyra" })).ToBeVisibleAsync();
        await Assertions.Expect(signOut).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-account-name]")).ToBeHiddenAsync();
        var account = (await nav.GetByRole(AriaRole.Link, new() { Name = "Mano paskyra" }).BoundingBoxAsync())!;
        var button = (await signOut.BoundingBoxAsync())!;
        (button.X + button.Width).ShouldBeLessThanOrEqualTo(width, "inside the window");
        account.X.ShouldBeGreaterThanOrEqualTo(0, "inside the window");
        button.Y.ShouldBeGreaterThan(account.Y, "under the account link");
        await page.ScreenshotAsync(new() { Path = Shot($"menu-sign-out-{width}.png") });

        await signOut.ClickAsync();
        await page.WaitForURLAsync(new Regex("/$"));
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Prisijungti" })).ToBeVisibleAsync();
        await Assertions.Expect(nav.GetByRole(AriaRole.Button, new() { Name = "Atsijungti" })).ToHaveCountAsync(0);

        // The session is gone on the server too: the account page asks to sign in.
        await page.GotoAsync($"{Base}/sveiki");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/prisijungti"));
    }

    private static string Shot(string name)
    {
        Directory.CreateDirectory(Path.Combine("output", "appeals"));
        return Path.Combine("output", "appeals", name);
    }
}
