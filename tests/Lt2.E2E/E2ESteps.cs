using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// The local Mailpit the stack sends to: the newest message to an address, waited for. An address that gets other mail while
/// a test runs (an MP's official address gets the summaries) is read by subject, and a message the test has just caused is
/// waited for by count (<see cref="NewAsync"/>, <see cref="NewCodeAsync"/>), never taken as whatever is newest.
/// </summary>
internal static partial class Mailpit
{
    public static readonly string Url = Environment.GetEnvironmentVariable("LT2_E2E_MAILPIT_URL") ?? "http://localhost:8025";

    /// <summary>The text of the newest email to <paramref name="to"/> (whose subject has <paramref name="subject"/>), waiting up to <paramref name="wait"/> (45 s).</summary>
    public static async Task<(string Subject, string Text)> MessageAsync(string to, string? subject = null, TimeSpan? wait = null)
    {
        using var http = new HttpClient { BaseAddress = new Uri(Url) };
        var query = subject is null ? $"to:\"{to}\"" : $"to:\"{to}\" subject:\"{subject}\"";
        var until = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(45));
        while (true)
        {
            var search = await http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString(query)}");
            var messages = search.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var id = messages[0].GetProperty("ID").GetString();
                var message = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}");
                return (messages[0].GetProperty("Subject").GetString()!, message.GetProperty("Text").GetString()!);
            }

            DateTime.UtcNow.ShouldBeLessThan(until, $"an email to {to}{(subject is null ? "" : $" about \"{subject}\"")} arrives in Mailpit");
            await Task.Delay(500);
        }
    }

    /// <summary>The text of the newest email to <paramref name="to"/> whose subject has <paramref name="subject"/>.</summary>
    public static async Task<string> MailAsync(string to, string subject, TimeSpan? wait = null) => (await MessageAsync(to, subject, wait)).Text;

    /// <summary>
    /// The newest email matching a Mailpit <paramref name="query"/> (for example <c>subject:"…"</c>), waited for: its id,
    /// subject, text and HTML parts and its headers (design 8.19.3: <c>List-Unsubscribe</c>).
    /// </summary>
    public static async Task<(string Id, string Subject, string Text, string Html, JsonElement Headers)> FullAsync(string query, TimeSpan? wait = null)
    {
        using var http = new HttpClient { BaseAddress = new Uri(Url) };
        var until = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(60));
        while (true)
        {
            var search = await http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString(query)}");
            var messages = search.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var id = messages[0].GetProperty("ID").GetString()!;
                var message = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}");
                var headers = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}/headers");
                return (id, messages[0].GetProperty("Subject").GetString()!, message.GetProperty("Text").GetString()!, message.GetProperty("HTML").GetString() ?? "", headers);
            }

            DateTime.UtcNow.ShouldBeLessThan(until, $"an email matching {query} arrives in Mailpit");
            await Task.Delay(500);
        }
    }

    /// <summary>
    /// The newest email matching <paramref name="query"/> once more than <paramref name="before"/> match it: the one the test
    /// has just caused, not an earlier one with the same subject. Waits up to <paramref name="wait"/> (60 s).
    /// </summary>
    public static async Task<(string Id, string Subject, string Text, string Html, JsonElement Headers)> NewAsync(string query, int before, TimeSpan? wait = null)
    {
        var until = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(60));
        while (await CountAsync(query) <= before)
        {
            DateTime.UtcNow.ShouldBeLessThan(until, $"a new email matching {query} arrives in Mailpit");
            await Task.Delay(500);
        }

        return await FullAsync(query);
    }

    /// <summary>The Mailpit query for the sign-in code emails to <paramref name="email"/>.</summary>
    public static string CodeQuery(string email) => $"to:\"{email}\" subject:\"prisijungimo kodas\"";

    /// <summary>How many emails match a Mailpit <paramref name="query"/> now.</summary>
    public static async Task<int> CountAsync(string query)
    {
        using var http = new HttpClient { BaseAddress = new Uri(Url) };
        var search = await http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString(query)}");
        return search.GetProperty("messages_count").GetInt32();
    }

    /// <summary>
    /// The code of a sign-in email to <paramref name="email"/> newer than the <paramref name="before"/> such emails there were,
    /// waited for (45 s).
    /// </summary>
    public static async Task<string> NewCodeAsync(string email, int before)
    {
        var query = CodeQuery(email);
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        while (await CountAsync(query) <= before)
        {
            DateTime.UtcNow.ShouldBeLessThan(until, $"a new sign-in code for {email} arrives in Mailpit");
            await Task.Delay(500);
        }

        return await CodeAsync(email);
    }

    /// <summary>Deletes every email to <paramref name="to"/> (a test's own address, after the test).</summary>
    public static async Task DeleteToAsync(string to)
    {
        using var http = new HttpClient { BaseAddress = new Uri(Url) };
        using var response = await http.DeleteAsync($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{to}\"")}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The six-digit code of the newest sign-in email to <paramref name="email"/>, for an address that has had one code only;
    /// after asking for another, use <see cref="NewCodeAsync"/>.
    /// </summary>
    public static async Task<string> CodeAsync(string email)
    {
        var (subject, text) = await MessageAsync(email, "prisijungimo kodas");
        subject.ShouldStartWith("Lietuva 2.0 prisijungimo kodas");
        return SixDigits().Match(text).Value;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}

/// <summary>Steps and checks the end-to-end classes share.</summary>
internal static class E2ESteps
{
    /// <summary>
    /// The longest a step after a full navigation may take: the sign-in, a page rendered on the server, an island starting.
    /// A default of 5 s failed under load (an integration run on the same machine).
    /// </summary>
    public const float Slow = 60_000;

    public static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    /// <summary>
    /// A solved proof of work (design 6.4) for a registration or a code request posted straight to the API, found as the page's
    /// script finds it; null while the check is off (204). The pages' own requests carry theirs without any step.
    /// </summary>
    public static async Task<string?> BotCheckAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/bot-check/challenge");
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var challenge = await response.Content.ReadFromJsonAsync<JsonElement>();
        var salt = challenge.GetProperty("salt").GetString()!;
        var target = challenge.GetProperty("challenge").GetString()!;
        for (var number = 0; number <= challenge.GetProperty("maxnumber").GetInt32(); number++)
        {
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(salt + number.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            if (hash == target)
            {
                var json = JsonSerializer.Serialize(new
                {
                    algorithm = challenge.GetProperty("algorithm").GetString(),
                    challenge = target,
                    number,
                    salt,
                    signature = challenge.GetProperty("signature").GetString(),
                });
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            }
        }

        throw new InvalidOperationException("The challenge has no solution up to its maximum.");
    }

    /// <summary>
    /// A moderator's action (design 8.3, owner 2026-11-17): the filled button is pressed as it is; an action in "Kiti veiksmai"
    /// is chosen after opening that menu (the browser's popover). Waits until the island has started (the action enabled).
    /// </summary>
    public static async Task ChooseAsync(ILocator action)
    {
        await Assertions.Expect(action).ToBeEnabledAsync(new() { Timeout = Slow });
        if (!await action.IsVisibleAsync())
        {
            var menu = await action.EvaluateAsync<string>("e => e.closest('[popover]')?.id ?? ''");
            await action.Page.Locator($"button.menu__trigger[popovertarget='{menu}']").ClickAsync();
        }

        await action.ClickAsync();
    }

    /// <summary>
    /// Signs out with the account's own "Atsijungti" (design 8.21: on the screen "Saugumas ir duomenys", /paskyra/saugumas), opened
    /// first when the browser is elsewhere; waits for the island to start.
    /// </summary>
    public static async Task SignOutOnAccountAsync(IPage page)
    {
        if (!page.Url.Contains("/paskyra/saugumas", StringComparison.Ordinal))
        {
            await page.GotoAsync(Base + "/paskyra/saugumas");
        }

        var signOut = page.Locator("#account-sign-out");
        await Assertions.Expect(signOut).ToBeEnabledAsync(new() { Timeout = Slow });
        await signOut.ClickAsync();
    }

    /// <summary>
    /// The registration form already open on <paramref name="page"/> (any start: the sign-in page, an invitation link, a
    /// share link): the address, the emailed code, then the name, the age and the terms of use (design 6.2, 8.22.2). The invitation code is typed
    /// only when <paramref name="invitation"/> is given (a link fills it in). Leaves the page where the site lands it.
    /// </summary>
    public static async Task RegisterHereAsync(IPage page, string email, string displayName, string? invitation = null)
    {
        await SignUpTests.WaitForInteractivityAsync(page);
        await page.GetByLabel("El. paštas").FillAsync(email);
        if (invitation is not null)
        {
            await page.GetByLabel("Kvietimo kodas").FillAsync(invitation);
        }

        // An MP's official address may hold an earlier code (another test's registration) and other mail: only the new code.
        var codes = await Mailpit.CountAsync(Mailpit.CodeQuery(email));
        await page.GetByRole(AriaRole.Button, new() { Name = "Registruotis", Exact = true }).ClickAsync();
        await page.GetByLabel("Šešių skaitmenų kodas").FillAsync(await Mailpit.NewCodeAsync(email, codes));
        // From here the account exists (pending until it is completed): the test deletes it when it is over (E2ETest).
        await TestAccounts.RegisteredAsync(email, page.Context);
        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dar keli dalykai" })).ToBeVisibleAsync(new() { Timeout = Slow });
        await SignUpTests.WaitForInteractivityAsync(page);
        await page.GetByLabel("Vardas, kurį matys kiti").FillAsync(displayName);
        await page.GetByLabel("Man yra 18 metų arba daugiau").CheckAsync();
        await page.GetByLabel("Sutinku su naudojimo taisyklėmis").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Baigti registraciją" }).ClickAsync();
        await page.WaitForURLAsync(url => !new Uri(url).AbsolutePath.StartsWith("/prisijungti", StringComparison.Ordinal), new() { Timeout = Slow });
        await TestAccounts.SessionOfAsync(page.Context);
    }

    /// <summary>
    /// SQL against the stack's database (the compose project's <c>postgres</c> service), for set-ups the product offers no way to
    /// make: an account older than the terms of use. Returns what psql printed (unaligned, tuples only).
    /// </summary>
    public static Task<string> SqlAsync(string sql) =>
        Compose.RunAsync("exec", "-T", "postgres", "sh", "-c", "psql -U \"$POSTGRES_USER\" -d lt2 -v ON_ERROR_STOP=1 -q -At -c \"$0\"", sql);

    /// <summary>A unique invented address for one run.</summary>
    public static string NewEmail(string name = "ruta.mockute") => $"{name}.{Guid.NewGuid():N}@pavyzdys.test";

    /// <summary>No sideways scrolling at the page's current width.</summary>
    public static async Task NoSidewaysScrollAsync(IPage page, string? where = null) =>
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"))
            .ShouldBeLessThanOrEqualTo(0, $"no horizontal scrolling at {page.ViewportSize?.Width} px: {where ?? page.Url}");

    /// <summary>A full-page screenshot (or of one element) into <paramref name="folder"/> at 1280 px and at 390 px, the latter also checked for sideways scrolling.</summary>
    public static async Task ShootBothAsync(IPage page, string folder, string name, ILocator? part = null)
    {
        Directory.CreateDirectory(folder);
        await page.SetViewportSizeAsync(1280, 900);
        await ShootAsync(page, Path.Combine(folder, $"{name}-1280.png"), part);
        await page.SetViewportSizeAsync(390, 844);
        await NoSidewaysScrollAsync(page);
        await ShootAsync(page, Path.Combine(folder, $"{name}-390.png"), part);
        await page.SetViewportSizeAsync(1280, 900);
    }

    /// <summary>
    /// An API call from the signed-in page, as an island makes it (the antiforgery header from a fresh token); returns the
    /// status and the body's text.
    /// </summary>
    public static async Task<(int Status, string Body)> ApiAsync(IPage page, string method, string path, object? body = null)
    {
        var answer = await page.EvaluateAsync<string[]>(
            """
            async ([method, path, body]) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const headers = { 'X-XSRF-TOKEN': token };
              if (body !== null) headers['Content-Type'] = 'application/json';
              const r = await fetch(path, { method, headers, body: body === null ? undefined : body });
              return [String(r.status), await r.text()];
            }
            """,
            new object?[] { method, path, body is null ? null : JsonSerializer.Serialize(body, JsonSerializerOptions.Web) });
        return (int.Parse(answer[0], System.Globalization.CultureInfo.InvariantCulture), answer[1]);
    }

    /// <summary>
    /// Clean-up in a <c>finally</c>: the representative closes the organisation, which takes its statements off every bill
    /// (design 8.14.14). It never throws, so it cannot hide the failure that brought the test here; what it leaves is
    /// removed when the test is over (<see cref="E2ETest"/>).
    /// </summary>
    public static async Task CloseOrganisationAsync(IPage representative, string slug)
    {
        try
        {
            var (status, body) = await ApiAsync(representative, "POST", $"/api/v1/organisations/{slug}/close", new { });
            if (status != 204)
            {
                Console.Error.WriteLine($"Clean-up: closing {slug} answered {status}: {body}");
            }
        }
        catch (PlaywrightException ex)
        {
            Console.Error.WriteLine($"Clean-up: closing {slug} failed: {ex.Message}");
        }
    }

    /// <summary>An element's screenshot is tried again once: a resized page may re-render the island under it.</summary>
    private static async Task ShootAsync(IPage page, string file, ILocator? part)
    {
        if (part is null)
        {
            await page.ScreenshotAsync(new() { Path = file, FullPage = true });
            return;
        }

        try
        {
            await part.ScreenshotAsync(new() { Path = file });
        }
        catch (PlaywrightException)
        {
            await page.WaitForTimeoutAsync(1000);
            await part.ScreenshotAsync(new() { Path = file });
        }
    }
}
