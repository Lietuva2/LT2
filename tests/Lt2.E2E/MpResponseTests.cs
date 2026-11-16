using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.5.5 to 8.5.7 against the running stack: an MP takes the profile over (step one's flow, with the link from
/// Mailpit and a passkey from the virtual authenticator), reaches "Projektai, dėl kurių į jus kreipiamasi" from the account
/// page, publishes a response on a bill's page in the passkey session, sees it on the bill's page and on the profile as a
/// visitor without JavaScript, changes it and sees the earlier version, and withdraws it; at 1280 and 390 px, the part also in
/// the dark theme. A member who followed the bill gets the email "Seimo narių atsakymai" (design 8.20.1) in Mailpit, with the MP's
/// name and stance and without the explanation: the worker's job runs every minute on the local stack, and under "Iš karto" (design
/// 8.24) the answer is told at its next run. Screenshots go to
/// <c>output/mp/responses</c>. Whatever happened, the MP's and the follower's accounts are deleted (which ends the link), the
/// response it made is removed from the database (no request removes a response: its history is public), and the bill's "response"
/// events are removed.
/// </summary>
public sealed partial class MpResponseTests : E2ETest
{
    /// <summary>An invented MP of the stand-in's open data; this address exists only there and in Mailpit.</summary>
    private const string Official = "pavyzdys.aurelija.kadagyte@lrs.lt";
    private const string Name = "Aurelija Kadagytė";
    private const string Slug = "aurelija-kadagyte";
    private const long Person = 99000013;
    private const string Bill = "XVP-0618";
    private const string First = "Balsuosiu už: pataisa aiškiai apibrėžia savivaldybių teises. (E2E)";
    private const string Second = "Balsuosiu prieš: pakeitimui trūksta lėšų įvertinimo. (E2E)";

    private static string Base => E2ESteps.Base;

    private static string Shots => Path.Combine("output", "mp", "responses");

    private static Task<string> SqlAsync(string sql) =>
        Compose.RunAsync("exec", "-T", "postgres", "sh", "-c", "psql -U \"$POSTGRES_USER\" -d lt2 -v ON_ERROR_STOP=1 -q -At -c \"$0\"", sql);

    [Fact]
    public async Task An_mp_publishes_changes_and_withdraws_a_response_seen_by_a_visitor_without_javascript()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        await RemoveAsync();

        await using var mp = await PasskeyBrowser.StartAsync("native", 1280, 900);
        await using var follower = await PasskeyBrowser.StartAsync("native", 1280, 900);
        var page = mp.Page;
        string? followerEmail = null;
        try
        {
            // 0. A member follows the bill (design 8.20.1: the followers hear about MPs' answers), as if two days ago.
            followerEmail = await PasskeyTests.RegisterAsync(follower.Page, "Rūta Mockutė");
            var (followed, followBody) = await E2ESteps.ApiAsync(follower.Page, "PUT", $"/api/v1/bills/{Bill}/follow");
            followed.ShouldBe(204, followBody);
            await SqlAsync($"update notify.follow set created_at = created_at - interval '2 days' where user_id = (select id from identity.\"user\" where email = '{followerEmail}')");

            // 1. Step one's take-over: registration with the official address, the link from the mailbox, the passkey.
            await page.GotoAsync(Base + "/prisijungti?registruotis=true");
            await E2ESteps.RegisterHereAsync(page, Official, Name);
            await page.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });
            var claim = page.Locator("#mp-claim");
            await Assertions.Expect(claim).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await claim.ClickAsync();
            await Assertions.Expect(page.Locator("#mp-link-sent")).ToBeVisibleAsync();
            var mail = await Mailpit.MailAsync(Official, "Seimo nario profilio perėmimas");
            await page.GotoAsync(TakeoverLink().Match(mail).Value);
            var submit = page.Locator("#takeover-submit");
            await Assertions.Expect(submit).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await submit.ClickAsync();
            await page.WaitForURLAsync(new Regex("/sveiki$"), new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.Locator("#mp-link")).ToContainTextAsync($"Tvarkote Seimo nario profilį: {Name}.", new() { Timeout = E2ESteps.Slow });

            // 2. The page of design 8.5.7, reached from the account page.
            var bills = page.Locator("#mp-bills-link");
            await Assertions.Expect(bills).ToBeVisibleAsync();
            await bills.ClickAsync();
            await page.WaitForURLAsync(new Regex("/seimo-narys/projektai$"), new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.Locator("#mp-bills-for")).ToContainTextAsync($"Seimo narys: {Name}.", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(page.Locator("#mp-bills")).ToBeVisibleAsync();
            await E2ESteps.ShootBothAsync(page, Shots, "bills");

            // 3. The form on the bill's page, in the passkey session: published at once.
            await page.GotoAsync($"{Base}/seimas/{Bill}");
            var form = page.Locator("#mano-atsakymas");
            await Assertions.Expect(form).ToContainTextAsync($"Rašote Seimo nario vardu: {Name}.", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(form).ToContainTextAsync("Seimo narys arba jo biuras");
            await E2ESteps.ShootBothAsync(page, Shots, "form", form);
            await page.Locator("#my-response-stance-for").CheckAsync();
            await page.Locator("#my-response-text").FillAsync(First);
            await page.Locator("#my-response-submit").ClickAsync();
            await Assertions.Expect(page.Locator("#my-response-state")).ToContainTextAsync("Atsakymas paskelbtas", new() { Timeout = E2ESteps.Slow });
            var part = page.Locator("#atsakymai");
            await Assertions.Expect(part.Locator("#responses-list")).ToContainTextAsync(First, new() { Timeout = E2ESteps.Slow });
            await E2ESteps.ShootBothAsync(page, Shots, "published", part);

            // 3b. The morning after (design 8.20.1): the follower's email, with the MP's name and stance and no explanation.
            // Design 8.24: under "Iš karto" (the follower's default) the answer comes at the job's next run, without waiting for
            // the morning, so nothing is moved and no setting changed.
            var (subject, text) = await Mailpit.MessageAsync(followerEmail, $"Seimo narių atsakymai dėl {Bill}", TimeSpan.FromMinutes(3));
            subject.ShouldBe($"Lietuva 2.0: Seimo narių atsakymai dėl {Bill}");
            text.ShouldContain($"{Name}");
            text.ShouldContain("balsuos už");
            text.ShouldContain($"/seimas/{Bill}#atsakymai");
            text.ShouldNotContain(First, Case.Sensitive, "never the explanation's text");

            // 4. A visitor without JavaScript sees it on the bill's page and on the profile, with the mark.
            await using (var visitor = await VisitorAsync())
            {
                await visitor.Page.GotoAsync($"{Base}/seimas/{Bill}");
                var shown = visitor.Page.Locator("#atsakymai");
                await Assertions.Expect(shown).ToContainTextAsync(First);
                await Assertions.Expect(shown).ToContainTextAsync(Name);
                await Assertions.Expect(shown).ToContainTextAsync("Seimo narys arba jo biuras");
                await Assertions.Expect(shown).ToContainTextAsync("Balsuos už");
                await E2ESteps.ShootBothAsync(visitor.Page, Shots, "visitor-bill", shown);
                await visitor.Page.GotoAsync($"{Base}/p/{Slug}");
                var answers = visitor.Page.Locator("#atsakymai");
                await Assertions.Expect(answers).ToContainTextAsync("Atsakymai");
                await Assertions.Expect(answers).ToContainTextAsync(Bill);
                await Assertions.Expect(answers).ToContainTextAsync(First);
                await E2ESteps.ShootBothAsync(visitor.Page, Shots, "visitor-profile", answers);
            }

            // 5. Changed: the earlier version stays, behind "Ankstesnės versijos".
            await Assertions.Expect(page.Locator("#my-response-change")).ToBeEnabledAsync();
            await page.Locator("#my-response-change").ClickAsync();
            await page.Locator("#my-response-stance-against").CheckAsync();
            await page.Locator("#my-response-text").FillAsync(Second);
            await page.Locator("#my-response-submit").ClickAsync();
            await Assertions.Expect(part.Locator("#responses-list")).ToContainTextAsync(Second, new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(part).ToContainTextAsync("Keista");
            var earlier = part.Locator("details").Filter(new() { HasText = "Ankstesnės versijos (1)" });
            await earlier.Locator("summary").ClickAsync();
            await Assertions.Expect(earlier).ToContainTextAsync(First);
            await E2ESteps.ShootBothAsync(page, Shots, "changed", part);
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
            await E2ESteps.ShootBothAsync(page, Shots, "changed-dark", part);
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });

            // 6. Withdrawn: it stays as "Atsakymas atsiimtas", with its versions.
            await page.Locator("#my-response-withdraw").ClickAsync();
            await page.Locator("#my-response-withdraw-confirm").ClickAsync();
            await Assertions.Expect(part.Locator("#responses-list")).ToContainTextAsync("Atsakymas atsiimtas", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(part).ToContainTextAsync("Ankstesnės versijos (2)");
            await Assertions.Expect(page.Locator("#my-response-state")).ToContainTextAsync("Atsakymas atsiimtas");
            await E2ESteps.ShootBothAsync(page, Shots, "withdrawn", part);
            await using (var visitor = await VisitorAsync())
            {
                await visitor.Page.GotoAsync($"{Base}/seimas/{Bill}");
                await Assertions.Expect(visitor.Page.Locator("#atsakymai")).ToContainTextAsync("Atsakymas atsiimtas");
            }

            mp.ConsoleErrors.ShouldBeEmpty();
        }
        finally
        {
            // Deleting the MP's account ends the link (design 8.5.2); the response stays by design, so it is removed here.
            // Neither step throws, so neither can hide the failure.
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

            try
            {
                await RemoveAsync();
                await SqlAsync($"delete from notify.bill_event where bill_number = '{Bill}' and kind = 'response';");
            }
            catch (ShouldAssertException ex)
            {
                Console.Error.WriteLine($"Clean-up: removing the response, the setting or the events failed: {ex.Message}");
            }

            if (followerEmail is not null)
            {
                try
                {
                    var (status, body) = await E2ESteps.ApiAsync(follower.Page, "DELETE", "/api/v1/me");
                    if (status != 204)
                    {
                        Console.Error.WriteLine($"Clean-up: deleting the follower's account answered {status}: {body}");
                    }
                }
                catch (PlaywrightException ex)
                {
                    Console.Error.WriteLine($"Clean-up: deleting the follower's account failed: {ex.Message}");
                }
            }
        }
    }

    /// <summary>A visitor's browser with JavaScript off, at 1280 px.</summary>
    private static async Task<Visitor> VisitorAsync()
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new() { Channel = SignUpTests.Channel, Headless = true });
        var context = await browser.NewContextAsync(new()
        {
            JavaScriptEnabled = false,
            IgnoreHTTPSErrors = true,
            Locale = "lt-LT",
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        });
        return new Visitor(playwright, browser, await context.NewPageAsync());
    }

    /// <summary>
    /// The MP's response and its traces (versions, reports, moderators' actions), and an account left on the official address by
    /// a failed run (its link goes with it). Local stack only: it needs the docker CLI, as the demonstration summary's reset does.
    /// </summary>
    private static async Task RemoveAsync() =>
        await Compose.RunAsync(
            "exec", "-T", "postgres", "sh", "-c", "psql -U \"$POSTGRES_USER\" -d lt2 -v ON_ERROR_STOP=1 -q -c \"$0\"",
            $"""
            begin;
            delete from appeals.report where target_kind = 'response' and target_id in (select id from appeals.mp_response where seimas_person_id = {Person});
            delete from appeals.moderation_action where target_kind = 'response' and target_id in (select id from appeals.mp_response where seimas_person_id = {Person});
            delete from appeals.revision where entity_kind = 'response' and entity_id in (select id from appeals.mp_response where seimas_person_id = {Person});
            delete from appeals.mp_response where seimas_person_id = {Person};
            delete from identity."user" where lower(email) = '{Official}';
            commit;
            """);

    [GeneratedRegex(@"https://\S+/seimo-narys/perimti#[A-Za-z0-9_-]+")]
    private static partial Regex TakeoverLink();

    private sealed record Visitor(IPlaywright Playwright, IBrowser Browser, IPage Page) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await E2ETest.KeepScreenAsync(Page);
            await Browser.DisposeAsync();
            Playwright.Dispose();
        }
    }
}
