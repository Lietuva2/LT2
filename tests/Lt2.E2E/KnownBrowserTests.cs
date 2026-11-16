using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.20.2 against the running stack: a member registers in the usual browser, which the account page then lists under
/// "Žinomos naršyklės" as "Ši naršyklė". Signed out, with the address's shared code limit used up for the day, a stranger's
/// browser gets no code (and is told nothing different), while the usual browser still gets one in Mailpit; that code is refused
/// in the stranger's browser and signs the usual one in. "Pamiršti" removes it from the list. Screenshots of the account page's
/// new part at 1280 and 390 px go to <c>output/known-browsers</c>. Whatever happened, the account is deleted (its known browsers
/// and codes go with it).
/// </summary>
public sealed class KnownBrowserTests : E2ETest
{
    private static string Shots => Path.Combine("output", "known-browsers");

    private static Task<string> SqlAsync(string sql) =>
        Compose.RunAsync("exec", "-T", "postgres", "sh", "-c", "psql -U \"$POSTGRES_USER\" -d lt2 -v ON_ERROR_STOP=1 -q -At -c \"$0\"", sql);

    private static async Task AskForCodeAsync(IPage page, string email)
    {
        await page.GotoAsync(E2ESteps.Base + "/prisijungti");
        await SignUpTests.WaitForInteractivityAsync(page);
        await page.GetByLabel("El. paštas").FillAsync(email);
        await page.GetByRole(AriaRole.Button, new() { Name = "Atsiųsti kodą" }).ClickAsync();
        await Assertions.Expect(page.GetByLabel("Šešių skaitmenų kodas")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
    }

    private static async Task TypeCodeAsync(IPage page, string code)
    {
        await page.GetByLabel("Šešių skaitmenų kodas").FillAsync(code);
        await page.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();
    }

    [Fact]
    public async Task The_usual_browser_signs_in_when_strangers_used_up_the_shared_limit_and_the_person_forgets_it()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var usual = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await using var stranger = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = usual.Page;
        string? email = null;
        try
        {
            // 1. Registered here: the account page lists this browser.
            email = await PasskeyTests.RegisterAsync(page, "Rūta Mockutė");
            var part = page.Locator("#zinomos-narsykles");
            await Assertions.Expect(part).ToContainTextAsync("Žinomos naršyklės", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(part.Locator("#known-browsers li")).ToHaveCountAsync(1);
            await Assertions.Expect(part).ToContainTextAsync("Ši naršyklė");
            await E2ESteps.ShootBothAsync(page, Shots, "listed", part);
            await E2ESteps.SignOutOnAccountAsync(page);
            await page.WaitForURLAsync(new Regex("/$"), new() { Timeout = E2ESteps.Slow });

            // 2. The address's shared limit used up for the day, by the highest daily number the settings allow.
            await SqlAsync(
                $"""
                insert into identity.sign_in_code (id, user_id, code_hash, created_at, expires_at, failed_attempts, consumed_at)
                select gen_random_uuid(), id, '\x00'::bytea, now() - interval '3 hours', now() - interval '3 hours', 0, now()
                from identity."user", generate_series(1, 50) where email = '{email}'
                """);
            var before = await Mailpit.CountAsync($"to:\"{email}\"");

            // 3. A stranger asks first: the page answers the same as ever.
            await AskForCodeAsync(stranger.Page, email);

            // 4. Then the usual browser asks: its own allowance sends a code. The outbox sends in order, so once the usual
            //    browser's code has arrived, a code for the stranger would have arrived before it: exactly one came.
            await AskForCodeAsync(page, email);
            var until = DateTime.UtcNow.AddSeconds(45);
            while (await Mailpit.CountAsync($"to:\"{email}\"") == before)
            {
                DateTime.UtcNow.ShouldBeLessThan(until, "the usual browser's code arrives in Mailpit");
                await page.WaitForTimeoutAsync(500);
            }

            (await Mailpit.CountAsync($"to:\"{email}\"")).ShouldBe(before + 1, "the shared limit holds for a stranger's browser");
            var code = await Mailpit.CodeAsync(email);

            // 5. The code is refused in the stranger's browser, and the usual one signs in with it.
            await TypeCodeAsync(stranger.Page, code);
            await Assertions.Expect(stranger.Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Kodas netinka arba nebegalioja. Paprašykite naujo." }))
                .ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            await TypeCodeAsync(page, code);
            await page.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });
            await page.GotoAsync(E2ESteps.Base + "/sveiki/saugumas");
            await Assertions.Expect(part.Locator("#known-browsers li")).ToHaveCountAsync(1, new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(part).ToContainTextAsync("Ši naršyklė");

            // 6. "Pamiršti": this browser is a stranger again.
            var forget = part.GetByRole(AriaRole.Button, new() { Name = "Pamiršti" });
            await Assertions.Expect(forget).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await forget.ClickAsync();
            await Assertions.Expect(part.Locator("#known-browsers-none")).ToHaveTextAsync("Žinomų naršyklių nėra.", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.GetByText("Naršyklė pamiršta.")).ToBeVisibleAsync();
            await E2ESteps.ShootBothAsync(page, Shots, "forgotten", part);
            (await SqlAsync($"select count(*) from identity.known_device d join identity.\"user\" u on u.id = d.user_id where u.email = '{email}'")).Trim().ShouldBe("0");
            usual.ConsoleErrors.ShouldBeEmpty();
        }
        finally
        {
            // Deleting the account takes its known browsers and codes with it; a run that failed signed out leaves the account
            // to E2ETest, which deletes it after a code sign-in. Never throws, so it cannot hide the failure.
            if (email is not null)
            {
                try
                {
                    var (status, body) = await E2ESteps.ApiAsync(page, "DELETE", "/api/v1/me");
                    if (status != 204)
                    {
                        Console.Error.WriteLine($"Clean-up: deleting the account answered {status}: {body}");
                    }
                }
                catch (PlaywrightException ex)
                {
                    Console.Error.WriteLine($"Clean-up: deleting the account failed: {ex.Message}");
                }
            }
        }
    }
}
