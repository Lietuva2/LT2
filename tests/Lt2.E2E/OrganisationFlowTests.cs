using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.14.14, stage A, against the running stack: a member registers an organisation, confirms it by a code read from
/// Mailpit, publishes a statement with a goal on XVP-0412; a visitor sees it in "Organizacijų pozicijos" with the label; a
/// member reports it; a moderator sees the new organisation and the reported statement in the queue and blocks the
/// organisation; the statement disappears and the representative is emailed; the block is lifted and the statement is
/// back. Screenshots at 1280 and 390 px go to <c>LT2_E2E_ORG_SHOTS</c> (or <c>output/organisations/shots</c>). Everything
/// the run makes carries <see cref="Marker"/>; the local clean-up script removes it.
/// </summary>
public sealed class OrganisationFlowTests(AppealFlowTests.ModeratorFixture moderator) : IClassFixture<AppealFlowTests.ModeratorFixture>
{
    /// <summary>In every organisation name an end-to-end run makes, so the clean-up script finds them.</summary>
    internal const string Marker = "(E2E)";

    private static readonly string MailpitUrl = Environment.GetEnvironmentVariable("LT2_E2E_MAILPIT_URL") ?? "http://localhost:8025";

    private static string Base => SignUpTests.BaseUrl!.TrimEnd('/');

    private static string Shots => Environment.GetEnvironmentVariable("LT2_E2E_ORG_SHOTS") is { Length: > 0 } dir ? dir : Path.Combine("output", "organisations", "shots");

    [Fact]
    public async Task An_organisation_is_registered_confirmed_by_a_code_states_a_position_is_blocked_and_comes_back()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        Directory.CreateDirectory(Shots);
        var mod = await moderator.PageAsync();
        var run = DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var name = $"Bandomoji pavyzdžių bendrija {run} {Marker}";
        var host = $"e2e-{run}-{Guid.NewGuid().ToString("N")[..6]}.pavyzdys.test";
        const string Text = "Prašome balsuoti už: pataisa sumažintų mokesčių skirtumą tarp pajamų rūšių (E2E bandymas).";

        // The visitor's look at the section before anything of this run, and the list.
        using var playwright = await Playwright.CreateAsync();
        await using var visitorBrowser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        await using var visitorContext = await visitorBrowser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "lt-LT", ViewportSize = new() { Width = 1280, Height = 900 } });
        var visitor = await visitorContext.NewPageAsync();

        // 1. A member registers the organisation.
        await using var representative = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var r = representative.Page;
        var representativeEmail = await PasskeyTests.RegisterAsync(r, "Atstovė Pavyzdytė");
        await r.GotoAsync($"{Base}/organizacijos/nauja");
        var nameField = r.Locator("#org-name");
        await Assertions.Expect(nameField).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await ShootBothAsync(r, "registration-form");
        await nameField.FillAsync(name);
        await r.Locator("#org-description").FillAsync("Išgalvota organizacija, kurią sukūrė automatinis bandymas.");
        await r.Locator("#org-website").FillAsync("https://www." + host);
        await r.Locator("#org-submit").ClickAsync();
        await r.WaitForURLAsync(new Regex("/o/bandomoji-pavyzdziu-bendrija-"), new() { Timeout = 60_000 });
        var profilePath = new Uri(r.Url).AbsolutePath;
        await Assertions.Expect(r.Locator("#organisation-tags")).ToContainTextAsync("Nepatvirtinta");

        // 2. Confirms it by a code sent to an address on the website's domain, read from Mailpit.
        var address = Assertions.Expect(r.Locator("#confirm-address"));
        await address.ToBeEnabledAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(r.Locator("#manage")).ToContainTextAsync("Atstovė Pavyzdytė");
        await ShootBothAsync(r, "profile-representative-confirmation-step");
        await r.Locator("#confirm-address").FillAsync("info@" + host);
        await r.Locator("#confirm-send").ClickAsync();
        await Assertions.Expect(r.Locator("#confirm-code")).ToBeVisibleAsync();
        var code = Regex.Match(await MailAsync("info@" + host, "organizacijos patvirtinimo kodas"), @"(?m)^\s*(\d{6})\s*$").Groups[1].Value;
        code.ShouldNotBeNullOrEmpty();
        await r.Locator("#confirm-code").FillAsync(code);
        await r.Locator("#confirm-submit").ClickAsync();
        await Assertions.Expect(r.Locator("#organisation-tags")).ToContainTextAsync("Patvirtinta per el. pašto adresą @" + host, new() { Timeout = 60_000 });
        await Assertions.Expect(r.Locator("#manage")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await ShootBothAsync(r, "profile-representative-confirmed");

        // 3. Publishes a statement with a goal on the demonstration bill, from its section on the bill's page.
        await r.GotoAsync($"{Base}/seimas/XVP-0412");
        var slug = profilePath["/o/".Length..];
        var open = r.Locator($"#statement-open-{slug}");
        await Assertions.Expect(open).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await open.ClickAsync();
        await r.Locator($"#stance-for-{slug}").CheckAsync();
        await r.Locator($"#statement-text-{slug}").FillAsync(Text);
        await r.Locator($"#statement-link-{slug}").FillAsync($"https://www.{host}/pozicija");
        await r.Locator($"#statement-goal-{slug}").FillAsync("9 000");
        await r.Locator("#organisations").ScrollIntoViewIfNeededAsync();
        await ShootBothAsync(r, "bill-organisations-representative-form", r.Locator("#organisations"));
        await r.Locator($"#statement-submit-{slug}").ClickAsync();
        await Assertions.Expect(r.GetByText("Pozicija paskelbta.")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(r.Locator($"#my-statement-status-{slug}")).ToHaveTextAsync("Paskelbta");

        // 4. A visitor sees it in "Organizacijų pozicijos" with the label and the goal; the section says it is not counted.
        await visitor.GotoAsync($"{Base}/seimas/XVP-0412");
        var section = visitor.Locator("#organisations");
        var mine = section.Locator("article.statement").Filter(new() { HasText = name });
        await Assertions.Expect(mine).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(mine).ToContainTextAsync("Patvirtinta per el. pašto adresą @" + host);
        await Assertions.Expect(mine).ToContainTextAsync("Siekiame 9 000 dalyvių · dabar");
        await Assertions.Expect(mine.Locator("a[rel='nofollow noopener ugc']")).ToHaveTextAsync(host);
        await Assertions.Expect(section.Locator("#organisations-not-counted")).ToContainTextAsync("neskaičiuojamos");
        await Assertions.Expect(section.Locator("#organisations-for")).ToContainTextAsync("Pavyzdinė mokesčių aiškumo asociacija");
        await Assertions.Expect(section.Locator("#organisations-against")).ToContainTextAsync("Pavyzdinis šeimos verslų sambūris");
        await Assertions.Expect(section.Locator("#organisations-remarks")).ToContainTextAsync("Pavyzdinė viešųjų finansų stebėsena");
        (await visitor.ContentAsync()).ShouldNotContain("Atstovė Pavyzdytė");
        await NoSidewaysScrollAsync(visitor);
        await ShootBothAsync(visitor, "bill-organisations-visitor", section);
        await visitor.GotoAsync($"{Base}{profilePath}");
        await Assertions.Expect(visitor.Locator("#organisation-tags")).ToContainTextAsync("Patvirtinta per el. pašto adresą @" + host);
        (await visitor.ContentAsync()).ShouldNotContain("Atstovė Pavyzdytė");
        await ShootBothAsync(visitor, "profile-confirmed-visitor");
        await visitor.GotoAsync($"{Base}/organizacijos");
        await Assertions.Expect(visitor.Locator("#organisations")).ToContainTextAsync("Pavyzdinė mokesčių aiškumo asociacija");
        await ShootBothAsync(visitor, "organisations-list");

        // 5. A member reports the statement as impersonation.
        await using var reporter = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var rp = reporter.Page;
        await PasskeyTests.RegisterAsync(rp, "Pranešanti Narė");
        await rp.GotoAsync($"{Base}/seimas/XVP-0412");
        var reported = rp.Locator("#organisations article.statement").Filter(new() { HasText = name });
        var report = reported.GetByRole(AriaRole.Button, new() { Name = "Pranešti" });
        await Assertions.Expect(report).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await report.ClickAsync();
        await rp.Locator("#organisations").GetByLabel("Apsimeta kita organizacija").CheckAsync();
        await rp.Locator("#organisations").GetByRole(AriaRole.Button, new() { Name = "Pranešti moderatoriams" }).ClickAsync();
        await Assertions.Expect(rp.GetByText("Ačiū. Moderatoriai peržiūrės.")).ToBeVisibleAsync();

        // 6. The moderator's queue: the new organisation and the reported statement; blocks the organisation.
        await mod.SetViewportSizeAsync(1280, 900);
        await mod.GotoAsync($"{Base}/moderavimas");
        var queued = mod.Locator("#queue-organisations > article").Filter(new() { HasText = name });
        await Assertions.Expect(queued).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(queued).ToContainTextAsync("Nauja");
        var queuedStatement = mod.Locator("#queue-statements > article").Filter(new() { HasText = name });
        await Assertions.Expect(queuedStatement).ToContainTextAsync("Apsimeta kita organizacija");
        await queued.ScrollIntoViewIfNeededAsync();
        await ShootBothAsync(mod, "moderation-queue-organisations", mod.Locator("#queue-organisations"));
        var block = queued.Locator("button[id^='org-action-block-']");
        await Assertions.Expect(block).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await block.ClickAsync();
        await queued.Locator("textarea[id^='org-statement-']").FillAsync("Bandymas: organizacija blokuojama (E2E bandymas).");
        await ShootBothAsync(mod, "moderation-block-open", queued);
        await queued.Locator("button[id^='org-action-submit-']").ClickAsync();
        await Assertions.Expect(queued.GetByText("Organizacija užblokuota.")).ToBeVisibleAsync();

        // 7. The statement is gone from the bill; the profile says blocked; the representative is emailed.
        await visitor.GotoAsync($"{Base}/seimas/XVP-0412");
        await Assertions.Expect(visitor.Locator("#organisations")).ToBeVisibleAsync();
        await Assertions.Expect(visitor.Locator("#organisations article.statement").Filter(new() { HasText = name })).ToHaveCountAsync(0);
        await visitor.GotoAsync($"{Base}{profilePath}");
        await Assertions.Expect(visitor.Locator("#blocked")).ToContainTextAsync("Bandymas: organizacija blokuojama");
        await Assertions.Expect(visitor.Locator("#statements")).ToHaveCountAsync(0);
        await ShootBothAsync(visitor, "profile-blocked-visitor");
        (await MailAsync(representativeEmail, "užblokuota")).ShouldContain("Bandymas: organizacija blokuojama");

        // 8. The block is lifted from the list of hidden items: the statement is back, as it was.
        await mod.GotoAsync($"{Base}/moderavimas/paslepti");
        var hidden = mod.Locator("#hidden-organisations > article").Filter(new() { HasText = name });
        await Assertions.Expect(hidden).ToBeVisibleAsync(new() { Timeout = 60_000 });
        var restore = hidden.Locator("button[id^='org-restore-open-']");
        await Assertions.Expect(restore).ToBeEnabledAsync(new() { Timeout = 60_000 });
        await restore.ClickAsync();
        await hidden.Locator("textarea[id^='org-restore-statement-']").FillAsync("Bandymas baigtas (E2E bandymas).");
        await hidden.Locator("button[id^='org-restore-submit-']").ClickAsync();
        await Assertions.Expect(hidden.GetByText("Atkurta.")).ToBeVisibleAsync();
        await visitor.GotoAsync($"{Base}/seimas/XVP-0412");
        await Assertions.Expect(visitor.Locator("#organisations article.statement").Filter(new() { HasText = name })).ToContainTextAsync(Text, new() { Timeout = 30_000 });

        representative.ConsoleErrors.ShouldBeEmpty();
        reporter.ConsoleErrors.ShouldBeEmpty();

        // Clean up through the API: the statement is withdrawn, so the demonstration bill shows only the invented ones.
        var withdrawn = await r.EvaluateAsync<int>(
            """
            async (slug) => {
              const token = (await (await fetch('/api/v1/auth/antiforgery')).json()).token;
              return (await fetch('/api/v1/bills/XVP-0412/organisations/' + slug + '/statement', { method: 'DELETE', headers: { 'X-XSRF-TOKEN': token } })).status;
            }
            """,
            slug);
        withdrawn.ShouldBe(204);
    }

    /// <summary>A full-page screenshot (or of one element) at 1280 px and at 390 px, the latter also checked for sideways scrolling.</summary>
    private static async Task ShootBothAsync(IPage page, string name, ILocator? part = null)
    {
        await page.SetViewportSizeAsync(1280, 900);
        await ShootAsync(page, $"{name}-1280.png", part);
        await page.SetViewportSizeAsync(390, 844);
        await NoSidewaysScrollAsync(page);
        await ShootAsync(page, $"{name}-390.png", part);
        await page.SetViewportSizeAsync(1280, 900);
    }

    /// <summary>An element's screenshot is tried again once: a resized page may re-render the island under it.</summary>
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

    private static async Task NoSidewaysScrollAsync(IPage page) =>
        (await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth"))
            .ShouldBeLessThanOrEqualTo(0, $"no horizontal scrolling at {page.ViewportSize?.Width} px: {page.Url}");

    /// <summary>The text of the newest email to <paramref name="to"/> whose subject has <paramref name="subject"/>.</summary>
    private static async Task<string> MailAsync(string to, string subject)
    {
        using var http = new HttpClient { BaseAddress = new Uri(MailpitUrl) };
        for (var attempt = 0; ; attempt++)
        {
            var search = JsonDocument.Parse(await http.GetStringAsync($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{to}\" subject:\"{subject}\"")}")).RootElement;
            if (search.GetProperty("messages").GetArrayLength() > 0)
            {
                var id = search.GetProperty("messages")[0].GetProperty("ID").GetString();
                return JsonDocument.Parse(await http.GetStringAsync($"/api/v1/message/{id}")).RootElement.GetProperty("Text").GetString()!;
            }

            attempt.ShouldBeLessThan(90, $"an email to {to} about \"{subject}\" arrives in Mailpit");
            await Task.Delay(500);
        }
    }
}
