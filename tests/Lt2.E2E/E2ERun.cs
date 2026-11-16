using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(Lt2.E2E.E2ERun))]

namespace Lt2.E2E;

/// <summary>
/// What one end-to-end run needs from the stack and gives back at its end, so that no outside script has to prepare or
/// repair the stack (AGENTS.md, "Writing tests"):
/// <list type="bullet">
/// <item>The web host answers and has rendered its first pages before the first test (a host just started takes seconds
/// longer for each first page).</item>
/// <item>An invitation of the run's own, made by the migrator (<c>--seed-invitation</c>), so registrations never use up a
/// code people use by hand; it is deleted at the end, after every account registered with it (the profile refers to it).
/// <c>LT2_E2E_INVITATION</c> names an existing code instead, for a stack whose migrator cannot be run from here.</item>
/// </list>
/// The test classes clean up after themselves (<see cref="E2ETest"/>, <see cref="TestAccounts"/>); this fixture only checks,
/// at the end, that nothing of the run is left, and says so on the console when something is.
/// </summary>
public sealed class E2ERun : IAsyncLifetime
{
    private static string? _invitation;
    private bool _own;

    /// <summary>The invitation code registrations use in this run.</summary>
    public static string Invitation => _invitation
        ?? Environment.GetEnvironmentVariable("LT2_E2E_INVITATION")
        ?? throw new InvalidOperationException("The run's invitation is made when the run starts (E2ERun).");

    public static bool Enabled => !string.IsNullOrEmpty(SignUpTests.BaseUrl);

    public async ValueTask InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        await WarmUpAsync();
        var given = Environment.GetEnvironmentVariable("LT2_E2E_INVITATION");
        if (!string.IsNullOrWhiteSpace(given))
        {
            _invitation = given;
            return;
        }

        // Upper-case letters and digits: what the site keeps of a typed code (case and dashes do not matter).
        _invitation = "E2E-RUN-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        _own = true;
        await Compose.RunAsync("run", "--rm", "--no-deps", "migrator", "--seed-invitation", $"{_invitation}:2000");
    }

    public async ValueTask DisposeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        var left = await TestAccounts.LeftAsync();
        if (left.Count > 0)
        {
            Console.Error.WriteLine($"E2E run: {left.Count} account(s) of this run were not deleted: {string.Join(", ", left)}");
        }

        if (_own)
        {
            try
            {
                await E2ESteps.SqlAsync($"delete from identity.invitation where code = '{_invitation}'");
            }
            catch (Exception ex) when (ex is ShouldAssertException or InvalidOperationException)
            {
                // An account registered with it is still there (identity.profile refers to the code).
                Console.Error.WriteLine($"E2E run: the run's invitation {_invitation} could not be deleted: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Waits until the web host is ready and asks for the pages most tests open first, so the first test does not meet a
    /// host that is still compiling its pages (up to two minutes after a restart).
    /// </summary>
    private static async Task WarmUpAsync()
    {
        using var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        {
            BaseAddress = new Uri(E2ESteps.Base),
            Timeout = TimeSpan.FromSeconds(30),
        };
        var until = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        while (true)
        {
            try
            {
                if ((await http.GetAsync("/health/ready")).StatusCode == HttpStatusCode.OK)
                {
                    break;
                }
            }
            catch (HttpRequestException)
            {
                // Not up yet.
            }
            catch (TaskCanceledException)
            {
                // Not up yet.
            }

            DateTime.UtcNow.ShouldBeLessThan(until, "the web host answers /health/ready");
            await Task.Delay(500);
        }

        foreach (var path in new[] { "/", "/darbotvarke", "/seimas/XVP-0412", "/seimo-nariai", "/prisijungti" })
        {
            (await http.GetAsync(path)).EnsureSuccessStatusCode();
        }
    }
}

/// <summary>
/// The accounts a test made, and their deletion through the product (<c>DELETE /api/v1/me</c>, design 6.5) when the test is
/// over, whether it passed or not. A registration through the shared steps is recorded with the session it ended in; a later
/// sign-in in the same browser adds its session. The deletion uses the newest session that still works and otherwise signs
/// in with an emailed code from a client of its own; a staff account is deleted by its fixture from its staff passkey session
/// (the only session that may delete it, design 6.9).
/// </summary>
internal static class TestAccounts
{
    private const string SessionCookie = "__Host-lt2";
    private static readonly Lock Gate = new();
    private static readonly List<Account> Made = [];
    private static readonly AsyncLocal<string?> FixtureScope = new();

    /// <summary>Registrations made inside <paramref name="action"/> belong to a class fixture, which deletes them itself.</summary>
    public static async Task<T> ForFixtureAsync<T>(string fixture, Func<Task<T>> action)
    {
        FixtureScope.Value = fixture;
        try
        {
            return await action();
        }
        finally
        {
            FixtureScope.Value = null;
        }
    }

    /// <summary>
    /// A registration has started in <paramref name="context"/> (the account exists from the first step): the account is
    /// recorded; its sessions are added as the browser signs in (<see cref="SessionOfAsync"/>).
    /// </summary>
    public static Task RegisteredAsync(string email, IBrowserContext context)
    {
        var account = new Account(email.ToLowerInvariant(), FixtureScope.Value ?? E2ETest.CurrentTest, context);
        lock (Gate)
        {
            Made.Add(account);
        }

        return Task.CompletedTask;
    }

    /// <summary>The newest session of every account registered in <paramref name="context"/> (after a sign-in, before the browser closes).</summary>
    public static async Task SessionOfAsync(IBrowserContext context)
    {
        List<Account> mine;
        lock (Gate)
        {
            mine = Made.Where(a => ReferenceEquals(a.Context, context)).ToList();
        }

        if (mine.Count == 0)
        {
            return;
        }

        string? value;
        try
        {
            value = (await context.CookiesAsync([E2ESteps.Base])).FirstOrDefault(c => c.Name == SessionCookie)?.Value;
        }
        catch (PlaywrightException)
        {
            return;
        }

        if (value is null)
        {
            return;
        }

        // Only the account the browser was last registered or signed in as can hold this session; the newest is tried first.
        lock (Gate)
        {
            if (!mine[^1].Sessions.Contains(value))
            {
                mine[^1].Sessions.Add(value);
            }
        }
    }

    /// <summary>Deletes the accounts <paramref name="owner"/> made; returns what could not be done, to report.</summary>
    public static async Task<List<string>> DeleteAsync(string owner)
    {
        List<Account> mine;
        lock (Gate)
        {
            // Newest first: an account registered with another one's personal code refers to that code until it is gone.
            mine = Made.Where(a => a.Owner == owner).Reverse().ToList();
        }

        var problems = new List<string>();
        foreach (var account in mine)
        {
            try
            {
                await DeleteAsync(account);
                lock (Gate)
                {
                    Made.Remove(account);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or ShouldAssertException or InvalidOperationException or TaskCanceledException)
            {
                problems.Add($"{account.Email}: {ex.Message}");
            }
        }

        return problems;
    }

    /// <summary>The accounts of the whole run still recorded as not deleted, as they stand in the database.</summary>
    public static async Task<List<string>> LeftAsync()
    {
        List<string> emails;
        lock (Gate)
        {
            emails = Made.Select(a => a.Email).ToList();
        }

        if (emails.Count == 0)
        {
            return [];
        }

        var list = string.Join(",", emails.Select(e => $"'{e}'"));
        var found = await E2ESteps.SqlAsync($"select email from identity.\"user\" where lower(email) in ({list})");
        return found.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>The ids of accounts <paramref name="owner"/> made that still exist, quoted for SQL (none: <c>null</c>).</summary>
    public static string? EmailsOf(string owner)
    {
        lock (Gate)
        {
            var mine = Made.Where(a => a.Owner == owner).Select(a => $"'{a.Email}'").ToList();
            return mine.Count == 0 ? null : string.Join(",", mine);
        }
    }

    private static async Task DeleteAsync(Account account)
    {
        foreach (var session in Enumerable.Reverse(account.Sessions).ToList())
        {
            using var client = Client(session);

            // Only a session that is still this account's: a browser may have signed in as someone else since.
            using var me = await client.GetAsync("/api/v1/me");
            if (me.StatusCode != HttpStatusCode.OK
                || !string.Equals((await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString(), account.Email, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await DeleteMeAsync(client) == HttpStatusCode.NoContent)
            {
                await Mailpit.DeleteToAsync(account.Email);
                return;
            }
        }

        // Signed out, or the sessions ended: a code sign-in of its own.
        if (!await ExistsAsync(account.Email))
        {
            await Mailpit.DeleteToAsync(account.Email);
            return;
        }

        using (var client = Client(null))
        {
            var before = await Mailpit.CountAsync($"to:\"{account.Email}\" subject:\"prisijungimo kodas\"");
            var asked = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/code", new { email = account.Email });
            asked.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Accepted], $"a sign-in code for {account.Email} to delete it");
            var code = await Mailpit.NewCodeAsync(account.Email, before);
            var signed = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/sign-in", new { email = account.Email, code });
            signed.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.NoContent], $"signing in as {account.Email} to delete it");
            (await DeleteMeAsync(client)).ShouldBe(HttpStatusCode.NoContent, $"deleting {account.Email}");
        }

        await Mailpit.DeleteToAsync(account.Email);
    }

    private static async Task<bool> ExistsAsync(string email) =>
        (await E2ESteps.SqlAsync($"select count(*) from identity.\"user\" where lower(email) = '{email}'")).Trim() != "0";

    private static async Task<HttpStatusCode> DeleteMeAsync(HttpClient client) =>
        await SendAsync(client, HttpMethod.Delete, "/api/v1/me", null);

    /// <summary>A state-changing call as the site's own page makes it: a fresh antiforgery token and the site's origin.</summary>
    private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/antiforgery")).GetProperty("token").GetString();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Origin", E2ESteps.Base);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonSerializerOptions.Web);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static HttpClient Client(string? session)
    {
        var cookies = new CookieContainer();
        if (session is not null)
        {
            cookies.Add(new Uri(E2ESteps.Base), new System.Net.Cookie(SessionCookie, session, "/") { Secure = true, HttpOnly = true });
        }

        return new HttpClient(new HttpClientHandler
        {
            CookieContainer = cookies,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        })
        {
            BaseAddress = new Uri(E2ESteps.Base),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    private sealed record Account(string Email, string Owner, IBrowserContext Context)
    {
        public List<string> Sessions { get; } = [];
    }
}

/// <summary>
/// The base of every end-to-end test class: after each test, passed or failed, the accounts it registered are deleted
/// through the product, with first what the product keeps on purpose after an account is gone and a test must not leave
/// behind (organisations it registered, moderators' actions and reports on its arguments: <see cref="E2EData.ForgetAsync"/>).
/// A failure to clean up fails the test with what was left. When the test failed, the last screen of every browser it used
/// (<see cref="KeepScreenAsync"/>) and of the class's staff pages is kept in <c>output/failures/</c>.
/// </summary>
public abstract class E2ETest : IAsyncLifetime
{
    private static readonly Lock Gate = new();
    private static readonly List<IPage> StaffPages = [];
    private static string? _current;

    private readonly string _id = Guid.NewGuid().ToString("N");

    /// <summary>The test running now (the tests of this project run one at a time, xunit.runner.json).</summary>
    internal static string CurrentTest => _current ?? "outside a test";

    /// <summary>Where the screens of the running test go until it is known whether it failed.</summary>
    private static string Screens(string id) => Path.Combine("output", "last", id);

    /// <summary>
    /// The page's screen as it is now, kept if the running test fails (called as a browser closes). Never throws.
    /// </summary>
    internal static async Task KeepScreenAsync(IPage page)
    {
        if (_current is not { } id)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Screens(id));
            var file = Path.Combine(Screens(id), $"{DateTime.UtcNow:HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}.png");
            File.WriteAllText(Path.ChangeExtension(file, ".txt"), page.Url);
            await page.ScreenshotAsync(new() { Path = file, FullPage = true, Timeout = 10_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or TimeoutException)
        {
            // A closed or crashed page has no screen; the test's own failure says what happened.
        }
    }

    /// <summary>A class fixture's page (a staff member's), whose screen is kept when a test of the class fails.</summary>
    internal static void StaffPage(IPage page)
    {
        lock (Gate)
        {
            StaffPages.Add(page);
        }
    }

    public virtual ValueTask InitializeAsync()
    {
        _current = _id;
        return ValueTask.CompletedTask;
    }

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (!E2ERun.Enabled)
        {
            return;
        }

        await KeepScreensIfFailedAsync();
        _current = null;
        var problems = new List<string>();
        IReadOnlyList<Guid> organisations = [];
        if (TestAccounts.EmailsOf(_id) is { } emails)
        {
            try
            {
                organisations = await E2EData.ForgetAsync(emails);
            }
            catch (Exception ex) when (ex is ShouldAssertException or InvalidOperationException)
            {
                problems.Add($"forgetting what the test's accounts left: {ex.Message}");
            }
        }

        problems.AddRange(await TestAccounts.DeleteAsync(_id));
        if (organisations.Count > 0)
        {
            try
            {
                await E2EData.DeleteOrganisationsAsync(organisations);
            }
            catch (Exception ex) when (ex is ShouldAssertException or InvalidOperationException)
            {
                problems.Add($"deleting the test's organisations: {ex.Message}");
            }
        }

        problems.ShouldBeEmpty("what the test made is deleted after it");
    }

    private async Task KeepScreensIfFailedAsync()
    {
        var folder = Screens(_id);
        if (TestContext.Current.TestState?.Result is not TestResult.Failed)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return;
        }

        List<IPage> staff;
        lock (Gate)
        {
            staff = StaffPages.Where(p => !p.IsClosed).ToList();
        }

        foreach (var page in staff)
        {
            await KeepScreenAsync(page);
        }

        var test = TestContext.Current.Test?.TestDisplayName ?? _id;
        var name = new string(test.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_').ToArray());
        var target = Path.Combine("output", "failures", $"{DateTime.Now:yyyyMMdd-HHmmss}-{name[..Math.Min(name.Length, 150)]}");
        if (Directory.Exists(folder))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(folder, target);
            Console.Error.WriteLine($"Screens of the failed test: {Path.GetFullPath(target)}");
        }
    }
}
