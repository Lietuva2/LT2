using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.14.14, stage B, against the running stack: a representative copies the organisation's link for a bill; a new
/// person registers with it and lands on the bill; the representatives' number is 1; a member follows the organisation;
/// the organisation publishes a statement on another bill; the follower's email arrives in Mailpit with a full link and
/// without the statement's text (the worker's follow job runs every 15 minutes, so the run waits for it). Screenshots at
/// 1280 and 390 px go to <c>LT2_E2E_ORGB_SHOTS</c> (or <c>output/organisations-b/shots</c>). Everything the run makes
/// carries <see cref="OrganisationFlowTests.Marker"/>; the local clean-up script removes it.
/// </summary>
public sealed class OrganisationInvitationFlowTests(AdministratorFixture administrator) : IClassFixture<AdministratorFixture>
{
    private static readonly string MailpitUrl = Environment.GetEnvironmentVariable("LT2_E2E_MAILPIT_URL") ?? "http://localhost:8025";

    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    private static string Shots => Environment.GetEnvironmentVariable("LT2_E2E_ORGB_SHOTS") is { Length: > 0 } dir ? dir : Path.Combine("output", "organisations-b", "shots");

    [Fact]
    public async Task A_new_member_comes_by_the_organisations_link_and_a_follower_is_emailed_about_its_new_statement()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        Directory.CreateDirectory(Shots);
        var run = DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var name = $"Bandomasis narių sambūris {run} {OrganisationFlowTests.Marker}";
        const string FirstText = "Prašome balsuoti prieš (E2E bandymas, kvietimo nuoroda).";
        const string SecondText = "Slaptas antros pozicijos tekstas, kurio laiške neturi būti (E2E bandymas).";

        // 1. A member registers an organisation and publishes a statement on XVP-0412, through the API as the island does.
        await using var representative = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var r = representative.Page;
        await PasskeyTests.RegisterAsync(r, "Kodo Atstovė");
        var slug = await r.EvaluateAsync<string>(
            """
            async (name) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              const created = await fetch('/api/v1/organisations', { method: 'POST', headers: { 'X-XSRF-TOKEN': token, 'Content-Type': 'application/json' },
                body: JSON.stringify({ name, description: 'Išgalvota organizacija, kurią sukūrė automatinis bandymas.' }) });
              return (await created.json()).slug;
            }
            """,
            name);
        (await PutStatementAsync(r, "XVP-0412", slug, "against", FirstText)).ShouldBe(200);

        // 2. In the representatives' part of the profile: the link for the bill, copied; the number is 0.
        await r.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new() { Origin = Base });
        await r.GotoAsync($"{Base}/o/{slug}");
        var billLink = r.Locator("#org-code-bill-XVP-0412 a");
        await Assertions.Expect(billLink).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(r.Locator("#org-code-count")).ToContainTextAsync("užsiregistravo: 0");
        var copy = r.Locator("#org-code-copy-XVP-0412");
        await Assertions.Expect(copy).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await copy.ClickAsync();
        await Assertions.Expect(r.Locator("#org-code-bill-XVP-0412").GetByText("Nukopijuota.")).ToBeVisibleAsync();
        var link = await r.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        link.ShouldStartWith(Base + "/prisijungti?kodas=");
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
            var email = $"per.organizacija.{Guid.NewGuid():N}@pavyzdys.test";
            await n.GotoAsync(link);
            await SignUpTests.WaitForInteractivityAsync(n);
            await n.GetByLabel("El. paštas").FillAsync(email);
            await n.GetByRole(AriaRole.Button, new() { Name = "Registruotis", Exact = true }).ClickAsync();
            await n.GetByLabel("Šešių skaitmenų kodas").FillAsync(await SignUpTests.CodeFromMailpitAsync(email));
            await n.GetByRole(AriaRole.Button, new() { Name = "Prisijungti", Exact = true }).ClickAsync();
            await Assertions.Expect(n.GetByRole(AriaRole.Heading, new() { Name = "Dar du dalykai" })).ToBeVisibleAsync();
            await SignUpTests.WaitForInteractivityAsync(n);
            await n.GetByLabel("Vardas, kurį matys kiti").FillAsync("Atėjusi Per Nuorodą");
            await n.GetByLabel("Man yra 18 metų arba daugiau").CheckAsync();
            await n.GetByRole(AriaRole.Button, new() { Name = "Baigti registraciją" }).ClickAsync();
            await n.WaitForURLAsync(new Regex("/seimas/XVP-0412$"), new() { Timeout = 60_000 });
            newcomer.ConsoleErrors.ShouldBeEmpty();
        }

        // 4. The representatives' number is 1.
        await r.ReloadAsync();
        await Assertions.Expect(r.Locator("#org-code-count")).ToContainTextAsync("užsiregistravo: 1", new() { Timeout = 60_000 });
        await r.Locator("#manage").ScrollIntoViewIfNeededAsync();
        await ShootBothAsync(r, "profile-representative-code", r.Locator("#manage"));

        // 5. A member follows the organisation from its profile.
        await using var member = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var m = member.Page;
        var followerEmail = await PasskeyTests.RegisterAsync(m, "Sekanti Narė");
        await m.GotoAsync($"{Base}/o/{slug}");
        var follow = m.Locator("#follow-toggle-organisation");
        await Assertions.Expect(follow).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(follow).ToHaveTextAsync("Sekti");
        await ShootBothAsync(m, "profile-member-follow");
        await follow.ClickAsync();
        await Assertions.Expect(follow).ToHaveTextAsync("Nebesekti");

        // The account page lists it with the three switches.
        await m.GotoAsync($"{Base}/sveiki#sekami");
        await Assertions.Expect(m.Locator("#follows-organisations")).ToContainTextAsync(name, new() { Timeout = 60_000 });
        await Assertions.Expect(m.Locator("#notice-statements")).ToBeCheckedAsync();
        await Assertions.Expect(m.Locator("#notice-planned")).ToBeCheckedAsync();
        await Assertions.Expect(m.Locator("#notice-voted")).ToBeCheckedAsync();
        await m.Locator("#sekami").ScrollIntoViewIfNeededAsync();
        await ShootBothAsync(m, "account-follows", m.Locator("#sekami"));

        // 6. The administrators' invitations page lists the organisation's code by its name.
        var admin = await administrator.PageAsync();
        await admin.SetViewportSizeAsync(1280, 900);
        await admin.GotoAsync($"{Base}/administravimas/kvietimai");
        var find = admin.Locator("#inv-find");
        await Assertions.Expect(find).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await find.FillAsync(Uri.UnescapeDataString(code));
        await admin.GetByRole(AriaRole.Button, new() { Name = "Rasti", Exact = true }).ClickAsync();
        var row = admin.Locator("#inv-codes-list li").Filter(new() { HasText = name });
        await Assertions.Expect(row).ToContainTextAsync("organizacijos kodas", new() { Timeout = 30_000 });
        await Assertions.Expect(row).ToContainTextAsync("užbaigė registraciją: 1");
        (await admin.ContentAsync()).ShouldNotContain("Atėjusi Per Nuorodą");
        await ShootBothAsync(admin, "admin-invitations-organisation");

        // 7. The organisation publishes a statement on another bill; the follower is emailed once the follow job runs.
        (await PutStatementAsync(r, "XVP-0533", slug, "remarks", SecondText)).ShouldBe(200);
        var mail = await MailAsync(followerEmail, "paskelbė poziciją dėl XVP-0533", TimeSpan.FromMinutes(17));
        mail.ShouldContain(name);
        mail.ShouldContain("XVP-0533");
        mail.ShouldContain("Pozicija: pastabos");
        mail.ShouldContain(Base + "/seimas/XVP-0533");
        mail.ShouldNotContain(SecondText);
        mail.ShouldNotContain("Slaptas antros pozicijos");

        representative.ConsoleErrors.ShouldBeEmpty();
        member.ConsoleErrors.ShouldBeEmpty();

        // Clean up through the API: both statements are withdrawn, so the bills show only the invented organisations.
        (await DeleteStatementAsync(r, "XVP-0412", slug)).ShouldBe(204);
        (await DeleteStatementAsync(r, "XVP-0533", slug)).ShouldBe(204);
    }

    private static Task<int> PutStatementAsync(IPage page, string bill, string slug, string stance, string text) =>
        page.EvaluateAsync<int>(
            """
            async ([bill, slug, stance, text]) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              return (await fetch('/api/v1/bills/' + bill + '/organisations/' + slug + '/statement', { method: 'PUT',
                headers: { 'X-XSRF-TOKEN': token, 'Content-Type': 'application/json' }, body: JSON.stringify({ stance, text }) })).status;
            }
            """,
            new[] { bill, slug, stance, text });

    private static Task<int> DeleteStatementAsync(IPage page, string bill, string slug) =>
        page.EvaluateAsync<int>(
            """
            async ([bill, slug]) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              return (await fetch('/api/v1/bills/' + bill + '/organisations/' + slug + '/statement', { method: 'DELETE', headers: { 'X-XSRF-TOKEN': token } })).status;
            }
            """,
            new[] { bill, slug });

    /// <summary>A full-page screenshot (or of one element) at 1280 px and at 390 px, the latter also checked for sideways scrolling.</summary>
    private static async Task ShootBothAsync(IPage page, string name, ILocator? part = null)
    {
        await page.SetViewportSizeAsync(1280, 900);
        await ShootAsync(page, $"{name}-1280.png", part);
        await page.SetViewportSizeAsync(390, 844);
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"))
            .ShouldBeLessThanOrEqualTo(0, $"no horizontal scrolling at 390 px: {page.Url}");
        await ShootAsync(page, $"{name}-390.png", part);
        await page.SetViewportSizeAsync(1280, 900);
    }

    private static async Task ShootAsync(IPage page, string file, ILocator? part)
    {
        if (part is null)
        {
            await page.ScreenshotAsync(new() { Path = Path.Combine(Shots, file), FullPage = true });
            return;
        }

        try
        {
            await part.ScreenshotAsync(new() { Path = Path.Combine(Shots, file) });
        }
        catch (PlaywrightException)
        {
            await page.WaitForTimeoutAsync(1000);
            await part.ScreenshotAsync(new() { Path = Path.Combine(Shots, file) });
        }
    }

    /// <summary>The text of the newest email to <paramref name="to"/> whose subject has <paramref name="subject"/>, waiting up to <paramref name="wait"/>.</summary>
    private static async Task<string> MailAsync(string to, string subject, TimeSpan wait)
    {
        using var http = new HttpClient { BaseAddress = new Uri(MailpitUrl) };
        var until = DateTime.UtcNow + wait;
        while (true)
        {
            var search = JsonDocument.Parse(await http.GetStringAsync($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{to}\" subject:\"{subject}\"")}")).RootElement;
            if (search.GetProperty("messages").GetArrayLength() > 0)
            {
                var id = search.GetProperty("messages")[0].GetProperty("ID").GetString();
                return JsonDocument.Parse(await http.GetStringAsync($"/api/v1/message/{id}")).RootElement.GetProperty("Text").GetString()!;
            }

            DateTime.UtcNow.ShouldBeLessThan(until, $"an email to {to} about \"{subject}\" arrives in Mailpit");
            await Task.Delay(5000);
        }
    }
}
