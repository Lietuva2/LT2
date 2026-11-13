using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// One staff member for a test class: registered, given a passkey, granted <paramref name="role"/> by the migrator and
/// activated (design 6.2). Its page's console goes to a file of its own, and a Playwright trace of the set-up is kept
/// only when the set-up fails.
/// </summary>
public abstract class StaffFixture(string role, string displayName) : IAsyncLifetime
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
        // One file per fixture: several test classes use such a fixture at the same time.
        var log = Path.Combine("output", "appeals", $"{role}-console-{Guid.NewGuid():N}.txt");
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

        // A Playwright trace of the set-up, kept only when it fails: a click on "Aktyvinti" once waited 30 s for a
        // navigation nothing in the page starts (2026-11-13), and the next occurrence should be readable.
        await page.Context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = false, Title = $"{role} fixture" });
        try
        {
            var email = await PasskeyTests.RegisterAsync(page, displayName);
            await PasskeyTests.AddPasskeyAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Atsijungti" }).ClickAsync();
            await page.WaitForURLAsync(new Regex("/$"));
            await PasskeyTests.SignInWithPasskeyAsync(page);
            var output = await Compose.RunAsync("run", "--rm", "--no-deps", "migrator", "--grant-role", role, email);
            var code = PasskeyTests.ActivationCode().Match(output).Value;
            code.ShouldNotBeNullOrEmpty("the migrator prints the activation code");
            await page.ReloadAsync();
            await SignUpTests.WaitForInteractivityAsync(page);
            await page.GetByLabel("Aktyvinimo kodas").FillAsync(code);
            await page.GetByRole(AriaRole.Button, new() { Name = "Aktyvinti" }).ClickAsync();
            await Assertions.Expect(page.GetByText("Vaidmuo aktyvintas.")).ToBeVisibleAsync();
        }
        catch
        {
            var trace = Path.Combine("output", "appeals", $"{role}-trace-{Guid.NewGuid():N}.zip");
            await page.Context.Tracing.StopAsync(new() { Path = trace });
            File.AppendAllText(log, $"trace: {Path.GetFullPath(trace)}{Environment.NewLine}");
            throw;
        }

        await page.Context.Tracing.StopAsync();
        return page;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>An administrator for the class (settings, invitations).</summary>
public sealed class AdministratorFixture() : StaffFixture("administrator", "Administratorius A.");
