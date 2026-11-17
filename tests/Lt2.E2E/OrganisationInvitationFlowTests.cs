using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.14.14, stage B, against the running stack: a representative copies the organisation's link for a bill; a new
/// person registers with it and lands on the bill; the representatives' number is 1; a member follows the organisation;
/// the organisation publishes a statement on another bill; the follower's email arrives in Mailpit with a full link and
/// without the statement's text. The local stack runs the follow job every minute (<c>LT2_FOLLOWS_CRON</c> in
/// <c>deploy/.env</c>), so the email comes within about a minute. Screenshots at 1280 and 390 px go to
/// <c>LT2_E2E_ORGB_SHOTS</c> (or <c>output/organisations-b/shots</c>). Everything the run makes carries
/// <see cref="OrganisationFlowTests.Marker"/>; the organisation is closed at the end, whatever happened.
/// </summary>
public sealed class OrganisationInvitationFlowTests(AdministratorFixture administrator) : E2ETest, IClassFixture<AdministratorFixture>
{
    private static string Base => E2ESteps.Base;

    private static string Shots => Environment.GetEnvironmentVariable("LT2_E2E_ORGB_SHOTS") is { Length: > 0 } dir ? dir : Path.Combine("output", "organisations-b", "shots");

    [Fact]
    public async Task A_new_member_comes_by_the_organisations_link_and_a_follower_is_emailed_about_its_new_statement()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var run = DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var name = $"Bandomasis narių sambūris {run} {OrganisationFlowTests.Marker}";
        const string FirstText = "Prašome balsuoti prieš (E2E bandymas, kvietimo nuoroda).";
        const string SecondText = "Slaptas antros pozicijos tekstas, kurio laiške neturi būti (E2E bandymas).";

        // 1. A member registers an organisation and publishes a statement on XVP-0412, through the API as the island does.
        await using var representative = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var r = representative.Page;
        await PasskeyTests.RegisterAsync(r, "Kodo Atstovė");
        var (created, createdBody) = await E2ESteps.ApiAsync(r, "POST", "/api/v1/organisations", new { name, description = "Išgalvota organizacija, kurią sukūrė automatinis bandymas." });
        created.ShouldBe(201, createdBody);
        var slug = JsonDocument.Parse(createdBody).RootElement.GetProperty("slug").GetString()!;
        try
        {
            (await PutStatementAsync(r, "XVP-0412", slug, "against", FirstText)).ShouldBe(200);

            // 2. In the representatives' part of the profile: the link for the bill, copied; the number is 0.
            await r.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new() { Origin = Base });
            await r.GotoAsync($"{Base}/o/{slug}");
            var billLink = r.Locator("#org-code-bill-XVP-0412 a");
            await Assertions.Expect(billLink).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(r.Locator("#org-code-count")).ToContainTextAsync("užsiregistravo: 0");
            var copy = r.Locator("#org-code-copy-XVP-0412");
            await Assertions.Expect(copy).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await copy.ClickAsync();
            await Assertions.Expect(r.Locator("#org-code-bill-XVP-0412").GetByText("Nukopijuota.")).ToBeVisibleAsync();
            var link = await r.EvaluateAsync<string>("() => navigator.clipboard.readText()");
            link.ShouldStartWith(Base + "/prisijungti#kodas=");
            link.ShouldEndWith("&tikslas=%2Fseimas%2FXVP-0412");
            (await billLink.TextContentAsync()).ShouldBe(link);

            // Nothing of the code in the page's HTML as the server sent it, the representative's own included.
            var code = Regex.Match(link, "kodas=([^&]+)").Groups[1].Value;
            using (var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }))
            {
                (await http.GetStringAsync($"{Base}/o/{slug}")).ShouldNotContain(Uri.UnescapeDataString(code));
            }

            // 3. A new person registers with the copied link and lands on the bill.
            await using (var newcomer = await PasskeyBrowser.StartAsync("native"))
            {
                var n = newcomer.Page;
                await n.GotoAsync(link);
                await E2ESteps.RegisterHereAsync(n, E2ESteps.NewEmail("per.organizacija"), "Atėjusi Per Nuorodą");
                await n.WaitForURLAsync(new Regex("/seimas/XVP-0412$"), new() { Timeout = E2ESteps.Slow });
                newcomer.ConsoleErrors.ShouldBeEmpty();
            }

            // 4. The representatives' number is 1.
            await r.ReloadAsync();
            await Assertions.Expect(r.Locator("#org-code-count")).ToContainTextAsync("užsiregistravo: 1", new() { Timeout = E2ESteps.Slow });
            await r.Locator("#manage").ScrollIntoViewIfNeededAsync();
            await E2ESteps.ShootBothAsync(r, Shots, "profile-representative-code", r.Locator("#manage"));

            // 5. A member follows the organisation from its profile.
            await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
            var m = member.Page;
            var followerEmail = await PasskeyTests.RegisterAsync(m, "Sekanti Narė");
            await m.GotoAsync($"{Base}/o/{slug}");
            var follow = m.Locator("#follow-toggle-organisation");
            await Assertions.Expect(follow).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(follow).ToHaveTextAsync("Sekti");
            await E2ESteps.ShootBothAsync(m, Shots, "profile-member-follow");
            await follow.ClickAsync();
            await Assertions.Expect(follow).ToHaveTextAsync("Nebesekti");

            // The account page lists it with the three switches.
            await m.GotoAsync($"{Base}/paskyra/sekimas#sekami");
            await Assertions.Expect(m.Locator("#follows-organisations")).ToContainTextAsync(name, new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(m.Locator("#notice-statements")).ToBeCheckedAsync();
            await Assertions.Expect(m.Locator("#notice-planned")).ToBeCheckedAsync();
            await Assertions.Expect(m.Locator("#notice-voted")).ToBeCheckedAsync();

            // The switches are enabled once the island runs: before that its start replaces the prerendered markup, and an
            // element taken for scrolling would no longer be in the page.
            await Assertions.Expect(m.Locator("#notice-statements")).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await m.Locator("#sekami").ScrollIntoViewIfNeededAsync();
            await E2ESteps.ShootBothAsync(m, Shots, "account-follows", m.Locator("#sekami"));

            // 6. The administrators' invitations page lists the organisation's code by its name.
            var admin = await administrator.PageAsync();
            await admin.SetViewportSizeAsync(1280, 900);
            await admin.GotoAsync($"{Base}/administravimas/kvietimai");
            var find = admin.Locator("#inv-find");
            await Assertions.Expect(find).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await find.FillAsync(Uri.UnescapeDataString(code));
            await admin.GetByRole(AriaRole.Button, new() { Name = "Rasti", Exact = true }).ClickAsync();
            var row = admin.Locator("#inv-codes-list li").Filter(new() { HasText = name });
            await Assertions.Expect(row).ToContainTextAsync("organizacijos kodas", new() { Timeout = 30_000 });
            await Assertions.Expect(row).ToContainTextAsync("užbaigė registraciją: 1");
            (await admin.ContentAsync()).ShouldNotContain("Atėjusi Per Nuorodą");
            await E2ESteps.ShootBothAsync(admin, Shots, "admin-invitations-organisation");

            // 7. The organisation publishes a statement on another bill; the follower is emailed once the follow job runs.
            (await PutStatementAsync(r, "XVP-0533", slug, "remarks", SecondText)).ShouldBe(200);
            var mail = await Mailpit.MailAsync(followerEmail, "paskelbė poziciją dėl XVP-0533", TimeSpan.FromMinutes(3));
            mail.ShouldContain(name);
            mail.ShouldContain("XVP-0533");
            mail.ShouldContain("Pozicija: pastabos");
            mail.ShouldContain(Base + "/seimas/XVP-0533");
            mail.ShouldNotContain(SecondText);
            mail.ShouldNotContain("Slaptas antros pozicijos");

            representative.ConsoleErrors.ShouldBeEmpty();
            member.ConsoleErrors.ShouldBeEmpty();
        }
        finally
        {
            // The bills show only the demonstration organisations again, whatever happened above.
            await E2ESteps.CloseOrganisationAsync(r, slug);
        }
    }

    private static async Task<int> PutStatementAsync(IPage page, string bill, string slug, string stance, string text) =>
        (await E2ESteps.ApiAsync(page, "PUT", $"/api/v1/bills/{bill}/organisations/{slug}/statement", new { stance, text })).Status;
}
