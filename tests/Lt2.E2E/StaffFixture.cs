using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// One staff member for a test class: registered, given a passkey, granted <paramref name="role"/> by the migrator and
/// activated (design 6.2). Its page's console goes to a file of its own, and a Playwright trace of the set-up is kept
/// only when the set-up fails. When the class is over, what the staff member did as a moderator is forgotten and the account
/// is deleted from its own staff passkey session (the only session that may delete a staff account, design 6.9).
/// </summary>
public abstract class StaffFixture(string role, string displayName) : IAsyncLifetime
{
    private PasskeyBrowser? _browser;
    private string? _email;

    public async Task<IPage> PageAsync()
    {
        if (_browser is not null)
        {
            return _browser.Page;
        }

        _browser = await PasskeyBrowser.StartAsync("native");
        var page = _browser.Page;
        E2ETest.StaffPage(page);
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
            var email = _email = await TestAccounts.ForFixtureAsync(role, () => PasskeyTests.RegisterAsync(page, displayName));
            await PasskeyTests.AddPasskeyAsync(page);
            await E2ESteps.SignOutOnAccountAsync(page);
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
        GC.SuppressFinalize(this);
        if (_browser is null)
        {
            return;
        }

        try
        {
            if (_email is not null)
            {
                await E2EData.ForgetStaffAsync(_email);
                var (status, body) = await E2ESteps.ApiAsync(_browser.Page, "DELETE", "/api/v1/me");
                status.ShouldBe(204, $"the {role} fixture deletes its account: {body}");
                await Mailpit.DeleteToAsync(_email);
            }
        }
        finally
        {
            await _browser.DisposeAsync();
        }
    }
}

/// <summary>
/// An administrator for the class (settings, invitations). A runtime setting is changed and put back through the settings
/// screen, as a person does it (a fresh passkey confirmation, which the virtual authenticator gives): the application then
/// drops its own cached copy at once (design 5.6), where a change by SQL would wait out the cache.
/// </summary>
public sealed class AdministratorFixture() : StaffFixture("administrator", "Administratorius A.")
{
    /// <summary>
    /// Sets <paramref name="key"/> to <paramref name="typed"/> (as typed into the screen's field) and returns what it was, for
    /// <see cref="RestoreSettingAsync"/> in a <c>finally</c>.
    /// </summary>
    public async Task<SettingBefore> ChangeSettingAsync(string key, string typed)
    {
        var stored = (await E2ESteps.SqlAsync($"select count(*) from config.setting where key = '{key}'")).Trim() != "0";
        var admin = await PageAsync();
        var (status, body) = await E2ESteps.ApiAsync(admin, "GET", "/api/v1/admin/settings");
        status.ShouldBe(200, body);
        var value = System.Text.Json.JsonDocument.Parse(body).RootElement.EnumerateArray()
            .Single(s => s.GetProperty("key").GetString() == key).GetProperty("value");
        var before = new SettingBefore(key, value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString()! : value.GetRawText(), stored);
        await SaveAsync(key, typed);
        return before;
    }

    /// <summary>
    /// Puts a setting back as it was: through the screen, and when it had never been changed, its row (holding the default
    /// by then) goes too, so the table is as before.
    /// </summary>
    public async Task RestoreSettingAsync(SettingBefore before)
    {
        ArgumentNullException.ThrowIfNull(before);
        await SaveAsync(before.Key, before.Typed);
        if (!before.Stored)
        {
            await E2ESteps.SqlAsync($"delete from config.setting where key = '{before.Key}'");
        }
    }

    private async Task SaveAsync(string key, string typed)
    {
        var admin = await PageAsync();
        await admin.GotoAsync(E2ESteps.Base + "/administravimas/nustatymai");
        var id = "setting-" + key.Replace(':', '-');
        var field = admin.Locator("#" + id);
        await Assertions.Expect(field).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
        await field.FillAsync(typed);
        await field.DispatchEventAsync("change");
        await admin.Locator($"#row-{id} button").ClickAsync();
        await Assertions.Expect(admin.GetByText("Išsaugota:", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
    }

    /// <summary>A setting's value before a test changed it, as typed into the screen, and whether it had a row.</summary>
    public sealed record SettingBefore(string Key, string Typed, bool Stored);
}
