using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 6.2, "Changing the email address", against the running stack with Mailpit: a member signed in by a code opens "El. pašto
/// adresas" on "Saugumas ir duomenys", proves it is them with a code sent to the address in use, types another address and the
/// code sent to it; the part shows the new address. The old address gets the email with the new one in part and "Ne jūs?
/// Grąžinti ankstesnį adresą"; opened in another browser, the link puts the old address back and ends every session, and the
/// address the account had moved to is told. The account is deleted after the test as any registered one (E2ETest), under the
/// old address it has again.
/// </summary>
public sealed partial class EmailChangeTests : E2ETest
{
    [Fact]
    public async Task A_member_changes_the_address_with_two_codes_and_the_old_address_puts_it_back()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await using var elsewhere = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = member.Page;
        var next = E2ESteps.NewEmail("ruta.kita");
        string? email = null;
        try
        {
            // 1. Registered and signed in by a code; the part shows the address in use, read in the browser.
            email = await PasskeyTests.RegisterAsync(page, "Rūta Mockutė");
            var part = page.Locator("#email-address");
            await Assertions.Expect(part.Locator("#email-current")).ToHaveTextAsync(email, new() { Timeout = E2ESteps.Slow });
            var open = part.GetByRole(AriaRole.Button, new() { Name = "Keisti adresą" });
            await Assertions.Expect(open).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await open.ClickAsync();

            // 2. A code session proves it with a code sent to the address in use.
            await part.Locator("#email-proof-send").ClickAsync();
            await Assertions.Expect(part.Locator("#email-proof-sent")).ToContainTextAsync(email);
            var proof = SixDigits().Match(await Mailpit.MailAsync(email, "adresui keisti")).Value;
            await part.Locator("#email-proof-code").FillAsync(proof);
            await part.GetByLabel("Kitas el. pašto adresas").FillAsync(next);
            await part.Locator("#email-change-submit").ClickAsync();

            // 3. The code sent to the other address moves the account.
            await Assertions.Expect(part.Locator("#email-code-sent")).ToContainTextAsync("Kodą išsiuntėme adresu " + next, new() { Timeout = E2ESteps.Slow });
            var code = SixDigits().Match(await Mailpit.MailAsync(next, "adresui patvirtinti")).Value;
            await part.GetByLabel("Šešių skaitmenų kodas").FillAsync(code);
            await part.Locator("#email-confirm-submit").ClickAsync();
            await Assertions.Expect(part.Locator("#email-current")).ToHaveTextAsync(next, new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(part.GetByText("Adresas pakeistas.")).ToBeVisibleAsync();
            (await PasskeyTests.MeAsync(page)).GetProperty("email").GetString().ShouldBe(next, "this session goes on");

            // 4. The old address is told, with the new one in part and the link back (its code after the "#").
            var told = await Mailpit.MailAsync(email, "adresas pakeistas");
            told.ShouldContain("r***@pavyzdys.test");
            told.ShouldNotContain(next);
            var link = RestoreLink().Match(told);
            link.Success.ShouldBeTrue("the email holds the link back");

            // 5. Opened in another browser, without a session: the old address back, every session ended.
            await elsewhere.Page.GotoAsync(E2ESteps.Base + "/paskyra/el-pastas/grazinti#" + link.Groups["code"].Value);
            var restore = elsewhere.Page.GetByRole(AriaRole.Button, new() { Name = "Grąžinti ankstesnį adresą" });
            await Assertions.Expect(restore).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await restore.ClickAsync();
            await Assertions.Expect(elsewhere.Page.Locator("#email-restored")).ToContainTextAsync("Ankstesnis adresas grąžintas", new() { Timeout = E2ESteps.Slow });
            (await E2ESteps.SqlAsync($"select count(*) from identity.\"user\" where email = '{email}'")).Trim().ShouldBe("1");
            (await E2ESteps.SqlAsync($"select count(*) from identity.session s join identity.\"user\" u on u.id = s.user_id where u.email = '{email}'")).Trim().ShouldBe("0");
            (await Mailpit.MailAsync(next, "ankstesnis paskyros")).ShouldContain("sesijos baigtos");

            // The member's own browser is signed out: the account page sends it to sign in.
            await page.GotoAsync(E2ESteps.Base + "/paskyra/saugumas");
            await page.WaitForURLAsync(new Regex("/prisijungti"), new() { Timeout = E2ESteps.Slow });
            member.ConsoleErrors.ShouldBeEmpty();
            elsewhere.ConsoleErrors.ShouldBeEmpty();
        }
        finally
        {
            // The account is deleted by E2ETest under the address it was registered with; if the test stopped while it had the
            // other address, it is put back by the database first, so that deletion finds it. Never throws.
            try
            {
                if (email is not null)
                {
                    await E2ESteps.SqlAsync(
                        $"""
                        update identity.profile set normalized_email = lower('{email}') where user_id = (select id from identity."user" where email = '{next}');
                        update identity."user" set email = '{email}', normalized_email = upper('{email}') where email = '{next}';
                        """);
                }

                await Mailpit.DeleteToAsync(next);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or System.ComponentModel.Win32Exception or Shouldly.ShouldAssertException)
            {
                Console.Error.WriteLine($"Clean-up after the address change failed: {ex.Message}");
            }
        }
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();

    [GeneratedRegex(@"/paskyra/el-pastas/grazinti#(?<code>[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4})")]
    private static partial Regex RestoreLink();
}
