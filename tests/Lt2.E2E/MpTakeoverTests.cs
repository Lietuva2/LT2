using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.5.1 to 8.5.3 against the running stack, whose open data is the stand-in's (invented MPs) and whose email is
/// caught by Mailpit: a person registers with a stand-in MP's official address without an invitation (the local stack has
/// <c>Registration:OfficialAddressesWithoutInvitation</c> on, migrator <c>--official-addresses-without-invitation</c>), sees
/// the offer, takes the profile over with the link read from Mailpit and a passkey made by the virtual authenticator, sees
/// the line on the profile and adds an office member by a one-time link. Screenshots at 1280 and 390 px go to
/// <c>output/mp/shots</c>. The MP's account is deleted at the end, whatever happened (which ends the link); the office
/// member is an ordinary <c>@pavyzdys.test</c> account, deleted when the test is over (<see cref="E2ETest"/>).
/// </summary>
public sealed partial class MpTakeoverTests : E2ETest
{
    /// <summary>An invented MP of the stand-in's open data; this address exists only there and in Mailpit.</summary>
    private const string Official = "pavyzdys.gintare.sermuksnyte@lrs.lt";
    private const string Name = "Gintarė Šermukšnytė";
    private const string Slug = "gintare-sermuksnyte";

    private static string Base => E2ESteps.Base;

    private static string Shots => Path.Combine("output", "mp", "shots");

    [Fact]
    public async Task An_mp_registers_without_an_invitation_takes_the_profile_over_and_adds_an_office_member()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");

        await using var mp = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = mp.Page;
        try
        {
            // 1. Registration without an invitation: the official address is the proof.
            await page.GotoAsync(Base + "/prisijungti?registruotis=true");
            await E2ESteps.RegisterHereAsync(page, Official, Name);
            await page.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });

            // 2. Told so at once, with one button.
            var offer = page.Locator("#mp-offer");
            await Assertions.Expect(offer).ToContainTextAsync($"Prisijungėte oficialiu Seimo nario adresu. Seimo narys: {Name}.", new() { Timeout = E2ESteps.Slow });
            await E2ESteps.ShootBothAsync(page, Shots, "offer", offer);
            var claim = page.Locator("#mp-claim");
            await Assertions.Expect(claim).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await claim.ClickAsync();
            await Assertions.Expect(page.Locator("#mp-link-sent")).ToBeVisibleAsync();

            // Nothing is public before the take-over is finished.
            using (var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }))
            {
                var html = System.Net.WebUtility.HtmlDecode(await http.GetStringAsync($"{Base}/p/{Slug}"));
                html.ShouldContain("Šermukšnytė", customMessage: "the swept page is the profile");
                html.ShouldNotContain("Profilį tvarko");
            }

            // 3. The link from the official mailbox, opened in this browser: what taking over means, then the passkey.
            var mail = await Mailpit.MailAsync(Official, "Seimo nario profilio perėmimas");
            var link = TakeoverLink().Match(mail).Value;
            link.ShouldStartWith(Base + "/seimo-narys/perimti#", Case.Sensitive, "design 6.8: the token after the #");
            await page.GotoAsync(link);
            var takeover = page.Locator("#takeover");
            await Assertions.Expect(takeover).ToContainTextAsync("pozicijos dabar bus atsiimtos", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(takeover).ToContainTextAsync("vienintelis šios paskyros raktas");
            await E2ESteps.ShootBothAsync(page, Shots, "takeover", takeover);
            var submit = page.Locator("#takeover-submit");
            await Assertions.Expect(submit).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await submit.ClickAsync();
            await page.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });

            // 4. Linked, in a passkey session, with one passkey.
            var linked = page.Locator("#mp-link");
            await Assertions.Expect(linked).ToContainTextAsync($"Tvarkote Seimo nario profilį: {Name}.", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.Locator("#mp-passkey-needed")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator(".rows li").Filter(new() { HasText = "Ši sesija" })).ToContainTextAsync("prisijungta prieigos raktu");
            await Assertions.Expect(page.GetByText("Sukurta prieigos raktų: 1")).ToBeVisibleAsync();

            // Design 8.21: the account item of an MP's account offers its projects, read from GET /me in the browser.
            await page.Locator("#account-toggle").ClickAsync();
            var mpBills = page.Locator("[data-account-link=mp]");
            await Assertions.Expect(mpBills).ToBeVisibleAsync();
            await Assertions.Expect(mpBills.GetByRole(AriaRole.Link)).ToHaveTextAsync("Projektai, dėl kurių į jus kreipiamasi");
            await Assertions.Expect(page.Locator("[data-account-link=moderation]")).ToBeHiddenAsync();
            await page.Keyboard.PressAsync("Escape");

            // 5. The line on the profile, for everyone.
            using (var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }))
            {
                System.Net.WebUtility.HtmlDecode(await http.GetStringAsync($"{Base}/p/{Slug}")).ShouldContain("Profilį tvarko Seimo narys arba jo biuras");
            }

            await page.GotoAsync($"{Base}/p/{Slug}");
            await Assertions.Expect(page.Locator("#managed-by")).ToContainTextAsync("Profilį tvarko Seimo narys arba jo biuras");
            await E2ESteps.ShootBothAsync(page, Shots, "profile");

            // 6. An office member: a one-time link, opened by a member in a passkey session.
            await page.GotoAsync(Base + "/sveiki");
            var invite = page.Locator("#mp-office-invite");
            await Assertions.Expect(invite).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await invite.ClickAsync();
            var officeLink = await page.Locator("#mp-office-link").GetAttributeAsync("href");
            officeLink.ShouldNotBeNull();
            officeLink.ShouldStartWith("/seimo-narys/biuras#", Case.Sensitive, "design 6.8: the token after the #");
            await E2ESteps.ShootBothAsync(page, Shots, "office-invitation", page.Locator("#mp-link"));

            await using (var member = await PasskeyBrowser.StartAsync("native", 1280, 900))
            {
                var m = member.Page;
                await PasskeyTests.RegisterAsync(m, "Biuro Narė (E2E)");
                await PasskeyTests.AddPasskeyAsync(m);
                await m.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Atsijungti" }).ClickAsync();
                await m.WaitForURLAsync(new Regex("/$"));
                await PasskeyTests.SignInWithPasskeyAsync(m);
                await m.GotoAsync(Base + officeLink);
                var accept = m.Locator("#office-accept");
                await Assertions.Expect(accept).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
                await E2ESteps.ShootBothAsync(m, Shots, "office-acceptance");
                await accept.ClickAsync();
                await Assertions.Expect(m.Locator("#office-joined")).ToContainTextAsync($"Esate Seimo nario biuro narys. Seimo narys: {Name}.");
                member.ConsoleErrors.ShouldBeEmpty();
            }

            // 7. The MP's account sees its office by name.
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator("#mp-office-members")).ToContainTextAsync("Biuro Narė (E2E)", new() { Timeout = E2ESteps.Slow });
            await E2ESteps.ShootBothAsync(page, Shots, "office", page.Locator("#mp-link"));
            mp.ConsoleErrors.ShouldBeEmpty();
        }
        finally
        {
            // Deleting the MP's account ends the link (design 8.5.2); never throws, so it cannot hide the failure.
            try
            {
                var (status, body) = await E2ESteps.ApiAsync(page, "DELETE", "/api/v1/me");
                if (status != 204)
                {
                    Console.Error.WriteLine($"Clean-up: deleting the MP's account answered {status}: {body}");
                }
            }
            catch (PlaywrightException ex)
            {
                Console.Error.WriteLine($"Clean-up: deleting the MP's account failed: {ex.Message}");
            }
        }
    }

    [GeneratedRegex(@"https://\S+/seimo-narys/perimti#[A-Za-z0-9_-]+")]
    private static partial Regex TakeoverLink();
}
