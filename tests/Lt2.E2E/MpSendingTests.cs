using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lt2.E2E;

/// <summary>
/// Design 8.19 against the running local stack, whose MPs are the stand-in's invented ones and whose mail Mailpit catches
/// (<c>Email:Caught</c> in deploy/.env): an administrator writes the introduction letter, sends a test copy to their own
/// address and sends it to the MPs; sending is switched on; a moderator is told of a prepared summary on every page, reviews it
/// and approves it (design 8.19.2, owner 2026-11-17), and the box goes; an urgent summary is sent at once and Mailpit holds an
/// MP's email with both sides and working links; through the link the MP changes the frequency, stops by the mail program's
/// one-click, and adds an extra recipient confirmed from the official address. Screenshots at 1280 and 390 px (the MP's page
/// also dark) go to <c>output/mp-sending</c>.
///
/// The scheduled job cannot be made due from here, so the prepared summary is written as the job writes one (a run and its
/// bill); the email comes from an urgent summary, which goes at once. The class removes what it made: rows of MP mail made since
/// it started (runs, deliveries, choices, links, suppression rows, letters, the moderators' notices), and puts
/// <c>Digest:Sending</c> back as it was through the settings screen.
/// </summary>
public sealed partial class MpSendingTests(AppealFlowTests.ModeratorFixture moderator, AdministratorFixture administrator)
    : E2ETest, IClassFixture<AppealFlowTests.ModeratorFixture>, IClassFixture<AdministratorFixture>
{
    private const string Official = "pavyzdys.rasa.ievaite@lrs.lt";
    private const string Name = "Rasa Ievaitė";
    private const string Bill = "XVP-0412";

    private static string Base => E2ESteps.Base;

    private static string Shots => Path.Combine("output", "mp-sending");

    [Fact]
    public async Task The_introduction_goes_first_a_moderator_reviews_a_prepared_summary_and_the_mp_manages_it_from_the_link()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(SignUpTests.BaseUrl), "LT2_E2E_BASE_URL is not set; start the stack and set it to run end-to-end tests.");
        var started = (await SqlAsync("select to_char(clock_timestamp() at time zone 'UTC', 'YYYY-MM-DD HH24:MI:SS.US')")).Trim();

        var lettersBefore = int.Parse((await SqlAsync("select count(*) from notify.introduction_letter where sent_at is not null")).Trim(), CultureInfo.InvariantCulture);
        var marker = DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
        var subject = $"Lietuva 2.0 – kas tai ir ko tikėtis (E2E {marker})";
        var extra = $"padejeja.{Guid.NewGuid():N}@pavyzdys.test";
        var admin = await administrator.PageAsync();
        var mod = await moderator.PageAsync();
        AdministratorFixture.SettingBefore? sendingBefore = null;
        try
        {
            // 1. The letter: written, previewed, a test copy to the administrator's own address, then sent to the MPs.
            await admin.GotoAsync(Base + "/administravimas/pristatymas");
            var save = admin.Locator("#intro-save");
            await Assertions.Expect(save).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await admin.Locator("#intro-subject").FillAsync(subject);
            await admin.Locator("#intro-text").FillAsync("Gerbiami Seimo nariai,\n\nLietuva 2.0 kartą per savaitę siunčia suvestinę: kaip naudotojai prašo balsuoti ir kodėl.\n\nNorite – gaukite rečiau arba atsisakykite.");
            await admin.Locator("#intro-signer").FillAsync("Rūta Mockutė");
            await admin.Locator("#intro-role").FillAsync("VšĮ LT2.0 vadovė");
            await save.ClickAsync();
            await Assertions.Expect(admin.GetByText("Išsaugota.")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            await admin.Locator("#intro-preview summary").ClickAsync();
            await E2ESteps.ShootBothAsync(admin, Shots, "introduction-admin");
            await admin.Locator("#intro-test").ClickAsync();
            await Assertions.Expect(admin.GetByText("Bandomoji kopija išsiųsta jūsų adresu.")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            var test = await Mailpit.FullAsync($"subject:\"[Bandomoji kopija] {subject}\"");
            test.Text.ShouldStartWith("[Bandomoji kopija");
            test.Text.ShouldContain("Rūta Mockutė");

            await admin.Locator("#intro-send").ClickAsync();
            await admin.Locator("#intro-send-confirm").ClickAsync();
            await Assertions.Expect(admin.GetByText(new Regex("Laiškas išsiųstas Seimo nariams: \\d+"))).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            var intro = await Mailpit.FullAsync($"to:\"{Official}\" subject:\"{subject}\"");
            intro.Text.ShouldContain("Keisti suvestinių dažnumą arba jų atsisakyti: " + Base + "/suvestines/nustatymai/");
            await admin.GotoAsync(Base + "/suvestines/pristatymas");
            if (lettersBefore == 0)
            {
                await Assertions.Expect(admin.Locator("#intro-subject")).ToHaveTextAsync(subject);
            }
            await E2ESteps.ShootBothAsync(admin, Shots, "introduction-public");

            // 2. Sending on, on the settings screen (a fresh passkey confirmation, which the virtual authenticator gives).
            sendingBefore = await administrator.ChangeSettingAsync("Digest:Sending", "automatic");
            await Assertions.Expect(admin.GetByText("Išsaugota: Siuntimas Seimo nariams", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            await E2ESteps.ShootBothAsync(admin, Shots, "settings", admin.Locator("#group-summaries"));

            // 3. A before-vote summary prepared for review (owner, 2026-11-17), as the job prepares one a day ahead: the run and its
            // bill; it would go in two hours. The moderator is told on every page, reviews it and presses "Patvirtinti".
            var run = Guid.NewGuid();
            await SqlAsync(
                $"""
                insert into notify.digest_run (id, kind, run_date, sitting_date, created_at, due_at, status, mp_count, sent_count)
                values ('{run}', 'before-vote', (now() at time zone 'Europe/Vilnius')::date, (now() at time zone 'Europe/Vilnius')::date + 1, now(), now() + interval '2 hours', 'prepared', 0, 0);
                insert into notify.digest_item (id, run_id, position, record_id, bill_number, bill_title, stage, planned_date, for_count, for_older_than30days, against_count, against_older_than30days, reasons)
                select gen_random_uuid(), '{run}', 0, r.id, r.bill_number, b.title, 'adoption', (now() at time zone 'Europe/Vilnius')::date + 1, 0, 0, 0, 0, '[]'
                from appeals.bill_record r join parliament.bill b on b.number = r.bill_number
                where r.bill_number = '{Bill}' and r.status = 'open';
                """);
            (await SqlAsync($"select count(*) from notify.digest_item where run_id = '{run}'")).Trim().ShouldBe("1", "the demonstration bill has an open record");

            await mod.GotoAsync(Base + "/seimo-nariai");
            await Assertions.Expect(mod.Locator("#digest-review-notice")).ToContainTextAsync("Suvestinė Seimo nariams išeis", new() { Timeout = E2ESteps.Slow });
            await E2ESteps.ShootBothAsync(mod, Shots, "moderators-notice");
            await mod.Locator("#digest-review-notice-link").ClickAsync();
            var prepared = mod.Locator($"#digmod-run-{run}");
            await Assertions.Expect(prepared).ToContainTextAsync("Išeis", new() { Timeout = E2ESteps.Slow });
            await Assertions.Expect(mod.Locator("#digmod-sending")).ToContainTextAsync("Siuntimas įjungtas");
            await Assertions.Expect(prepared).ToContainTextAsync("Prašo balsuoti už:");
            await Assertions.Expect(mod.Locator($"#digmod-per-mp-{run}")).ToBeVisibleAsync();
            var approve = mod.Locator($"#digmod-approve-{run}");
            await Assertions.Expect(approve).ToBeEnabledAsync(new() { Timeout = E2ESteps.Slow });
            await E2ESteps.ShootBothAsync(mod, Shots, "moderators-prepared");
            await approve.ClickAsync();
            await Assertions.Expect(mod.Locator($"#digmod-status-{run}")).ToContainTextAsync("Patvirtinta", new() { Timeout = E2ESteps.Slow });
            await mod.GotoAsync(Base + "/seimo-nariai");
            await Assertions.Expect(mod.Locator("#digest-review-notice")).ToBeHiddenAsync(new() { Timeout = E2ESteps.Slow });

            // 4. The job cannot be made due from here, so an urgent summary of the same bill (a moderator's decision, which goes at
            // once) carries the email; the prepared one is removed with the class's other rows.
            var record = (await SqlAsync($"select id from appeals.bill_record where bill_number = '{Bill}' and status = 'open'")).Trim();
            var date = (await SqlAsync("select to_char((now() at time zone 'Europe/Vilnius')::date, 'YYYY-MM-DD')")).Trim();
            var digestQuery = $"to:\"{Official}\" subject:\"Lietuva 2.0 suvestinė Seimo nariams, {date}\"";
            var digestsBefore = await Mailpit.CountAsync(digestQuery);
            await moderator.ConfirmFreshAsync();
            var (urgentStatus, urgentBody) = await E2ESteps.ApiAsync(mod, "POST", $"/api/v1/moderation/records/{record}/urgent", new { statement = "Bandymas (E2E): skubi suvestinė." });
            urgentStatus.ShouldBe(201, urgentBody);
            urgentBody.ShouldContain("\"status\":\"sent\"");

            // 5. The MP's mailbox: one email with both sides, the answer link, the closing lines and the one-click header.
            var digest = await Mailpit.FullAsync(digestQuery);
            (await Mailpit.CountAsync(digestQuery)).ShouldBe(digestsBefore + 1, "one email per MP");
            digest.Text.ShouldContain($"{Bill} · Gyventojų pajamų mokesčio įstatymo pataisa");
            digest.Text.ShouldContain("Prašo balsuoti už:");
            digest.Text.ShouldContain("Prašo balsuoti prieš:");
            digest.Text.ShouldContain("paskyros senesnės nei 30 d.");
            digest.Text.ShouldContain($"Atsakyti vieną kartą visiems: {Base}/seimas/{Bill}");
            digest.Text.ShouldContain("Tai nėra reprezentatyvi apklausa");
            digest.Text.ShouldContain($"Ta pati suvestinė viešai: {Base}/suvestines/{date}");
            digest.Text.ShouldContain($"perimti savo Seimo nario profilį ir atsakyti visiems iš karto: {Base}/prisijungti");
            digest.Html.ShouldContain($"<a href=\"{Base}/seimas/{Bill}\">");
            var unsubscribe = Header(digest.Headers, "List-Unsubscribe");
            unsubscribe.ShouldStartWith($"<{Base}/api/v1/mp-mail/");
            Header(digest.Headers, "List-Unsubscribe-Post").ShouldBe("List-Unsubscribe=One-Click");
            var preferences = PreferencesLink().Match(digest.Text).Value;
            preferences.ShouldNotBeNullOrEmpty();

            using (var http = Insecure())
            {
                (await http.GetAsync($"{Base}/seimas/{Bill}")).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, "the bill's link works");
                var copy = System.Net.WebUtility.HtmlDecode(await http.GetStringAsync($"{Base}/suvestines/{date}"));
                copy.ShouldContain("Išsiųsta Seimo nariams:");
                copy.ShouldNotContain(Name, Case.Sensitive, "never which MPs");
            }

            // 6. The MP's page from the link, without an account: the frequency, then the mail program's one-click stop.
            await using (var office = await PasskeyBrowser.StartAsync("native", 1280, 900))
            {
                var page = office.Page;
                await page.GotoAsync(preferences);
                await Assertions.Expect(page.Locator("#mp-mail-who")).ToContainTextAsync($"Seimo narys: {Name}.", new() { Timeout = E2ESteps.Slow });
                await E2ESteps.ShootBothAsync(page, Shots, "preferences");
                await page.Locator("#mp-mail-weekly").CheckAsync();
                await page.Locator("#mp-mail-save").ClickAsync();
                await Assertions.Expect(page.Locator("#mp-mail-info")).ToContainTextAsync("Išsaugota.", new() { Timeout = E2ESteps.Slow });
                await Assertions.Expect(page.Locator("#mp-mail-weekly")).ToBeCheckedAsync();
                var changed = await Mailpit.FullAsync($"to:\"{Official}\" subject:\"Lietuva 2.0: suvestinių nustatymai pakeisti\"");
                changed.Text.ShouldContain("Dabar: Tik kas savaitę.");

                using (var mailProgram = Insecure())
                {
                    using var stop = await mailProgram.PostAsync(
                        unsubscribe.Trim('<', '>'), new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" }));
                    stop.StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent, await stop.Content.ReadAsStringAsync());
                }

                // Opened again (not reloaded: a reload would post the form once more).
                await page.GotoAsync(preferences);
                await Assertions.Expect(page.Locator("#mp-mail-stopped")).ToBeCheckedAsync(new() { Timeout = E2ESteps.Slow });
                (await Mailpit.FullAsync($"to:\"{Official}\" subject:\"Lietuva 2.0: suvestinių nustatymai pakeisti\"")).Text.ShouldContain("Dabar: Nesiųsti.");

                // 7. An extra recipient, confirmed from the official address; copies come only after that.
                await page.Locator("#mp-mail-extra-address").FillAsync(extra);
                await page.Locator("#mp-mail-extra-ask").ClickAsync();
                await Assertions.Expect(page.Locator("#mp-mail-extra-state")).ToHaveTextAsync("laukia patvirtinimo oficialiu adresu", new() { Timeout = E2ESteps.Slow });
                var confirm = await Mailpit.FullAsync($"to:\"{Official}\" subject:\"Lietuva 2.0: patvirtinkite papildomą gavėją\"");
                confirm.Text.ShouldContain(extra);
                var confirmLink = ConfirmLink().Match(confirm.Text).Value;
                confirmLink.ShouldNotBeNullOrEmpty();
                await page.GotoAsync(confirmLink);
                await E2ESteps.ShootBothAsync(page, Shots, "extra-confirm");
                await page.Locator("#mp-mail-confirm").ClickAsync();
                await Assertions.Expect(page.Locator("#mp-mail-confirmed")).ToContainTextAsync(extra, new() { Timeout = E2ESteps.Slow });
                var welcome = await Mailpit.FullAsync($"to:\"{extra}\" subject:\"Lietuva 2.0: gausite Seimo nario suvestinių kopijas\"");
                welcome.Text.ShouldContain($"Seimo narys: {Name}.");
                await page.GotoAsync(preferences);
                await Assertions.Expect(page.Locator("#mp-mail-extra-state")).ToHaveTextAsync("patvirtintas", new() { Timeout = E2ESteps.Slow });
                await E2ESteps.ShootBothAsync(page, Shots, "preferences-extra");
                office.ConsoleErrors.ShouldBeEmpty();
            }

            await using (var dark = await PasskeyBrowser.StartAsync("native", 390, 844))
            {
                await dark.Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
                await dark.Page.GotoAsync(preferences);
                await Assertions.Expect(dark.Page.Locator("#mp-mail-who")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
                await E2ESteps.NoSidewaysScrollAsync(dark.Page);
                Directory.CreateDirectory(Shots);
                await dark.Page.ScreenshotAsync(new() { Path = Path.Combine(Shots, "preferences-390-dark.png"), FullPage = true });
                await dark.Page.SetViewportSizeAsync(1280, 900);
                await dark.Page.ScreenshotAsync(new() { Path = Path.Combine(Shots, "preferences-1280-dark.png"), FullPage = true });
                await mod.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
                await mod.GotoAsync(Base + "/moderavimas/suvestines");
                await Assertions.Expect(mod.Locator($"#digmod-run-{run}")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
                await mod.ScreenshotAsync(new() { Path = Path.Combine(Shots, "moderators-1280-dark.png"), FullPage = true });
                await mod.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
            }

            // The administrator's "Nesiunčiama" screen, as it stands.
            await admin.GotoAsync(Base + "/administravimas/nesiunciama");
            await Assertions.Expect(admin.Locator("#supp-others")).ToBeVisibleAsync(new() { Timeout = E2ESteps.Slow });
            await E2ESteps.ShootBothAsync(admin, Shots, "suppression");
        }
        finally
        {
            // Whatever happened: the setting as it was, through the settings screen (the site drops its cached copy at once),
            // and the rows of MP mail made since the start.
            try
            {
                if (sendingBefore is not null)
                {
                    await administrator.RestoreSettingAsync(sendingBefore);
                }
            }
            finally
            {
                await ForgetMpMailAsync(started, extra);
            }
        }
    }

    /// <summary>The rows of MP mail made since <paramref name="started"/>, and the extra recipient's emails. Never throws.</summary>
    private static async Task ForgetMpMailAsync(string started, string extra)
    {
            try
            {
                await Mailpit.DeleteToAsync(extra);
                await SqlAsync(
                    $"""
                    delete from notify.digest_run where created_at >= '{started}'::timestamp at time zone 'UTC' and (kind = 'introduction' or status <> 'dry-run');
                    delete from notify.mp_mail_token where created_at >= '{started}'::timestamp at time zone 'UTC';
                    delete from notify.mp_email_pref where changed_at >= '{started}'::timestamp at time zone 'UTC';
                    delete from notify.suppression where created_at >= '{started}'::timestamp at time zone 'UTC';
                    delete from notify.introduction_letter where written_at >= '{started}'::timestamp at time zone 'UTC';
                    delete from notify.outbox where (kind like 'email.mp-%' or kind = 'email.digest-review') and created_at >= '{started}'::timestamp at time zone 'UTC';
                    """);
            }
            catch (Exception ex) when (ex is Shouldly.ShouldAssertException or InvalidOperationException or System.ComponentModel.Win32Exception or HttpRequestException)
            {
                Console.Error.WriteLine($"Clean-up of MP mail failed: {ex.Message}");
            }
    }

    private static string Header(System.Text.Json.JsonElement headers, string name) =>
        headers.TryGetProperty(name, out var values) && values.GetArrayLength() > 0 ? values[0].GetString()! : "";

    private static HttpClient Insecure() => new(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });

    /// <summary>A statement on the local database, as the demonstration summary's reset does (the docker CLI is needed).</summary>
    private static Task<string> SqlAsync(string sql) =>
        Compose.RunAsync("exec", "-T", "postgres", "sh", "-c", "psql -U \"$POSTGRES_USER\" -d lt2 -v ON_ERROR_STOP=1 -q -At -c \"$0\"", sql);

    [GeneratedRegex(@"https://\S+/suvestines/nustatymai/[A-Za-z0-9_-]{43}")]
    private static partial Regex PreferencesLink();

    [GeneratedRegex(@"https://\S+/suvestines/nustatymai/patvirtinti/[A-Za-z0-9_-]{43}")]
    private static partial Regex ConfirmLink();
}
