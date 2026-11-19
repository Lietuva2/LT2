using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.19.1 to 8.19.3, 8.19.5: summaries really sent. Off: dry runs and nothing for MPs in the outbox; on: a summary prepared
/// the day before goes at its time, approved or not, unless a moderator declines it; the two locks; who receives one; the
/// introduction letter once per MP.
/// </summary>
public sealed class MpSendingTests(DatabaseFixture database) : ApiTest(database)
{
    private async Task<(StaffMember Moderator, StaffMember Admin, PreparedBill Bill, DateOnly Monday)> SummaryWeekAsync(int weeks, string bill = AppealSteps.ThirdBill)
    {
        var (moderator, admin) = await StaffAsync();
        await admin.SetFreshAsync("Digest:ThresholdMinimum", 1);
        var monday = ReasonsToComeSteps.MondayInWeeks(weeks);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var prepared = await Database.PrepareBillAsync(member, moderator.Browser, bill, "against", threshold: 1);
        await Database.PlanAsync(bill, monday.AddDays(3));
        return (moderator, admin, prepared, monday);
    }

    private async Task CleanUpAsync(StaffMember admin, string bill = AppealSteps.ThirdBill)
    {
        await admin.ResetFreshAsync("Digest:Sending");
        await admin.ResetFreshAsync("Digest:ThresholdMinimum");
        await admin.ResetFreshAsync("Digest:MaxPerWeek");
        await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533', 'XVP-0412')");
        await Database.ResetMpMailAsync();
        await Database.FreeBillAsync(bill);
    }

    [Fact]
    public async Task With_sending_off_a_summary_stays_a_dry_run_and_nothing_for_an_mp_reaches_the_outbox()
    {
        await Database.ResetMpMailAsync();
        var (_, admin, _, monday) = await SummaryWeekAsync(22);
        try
        {
            // Even with the letter sent, the setting off keeps it a dry run.
            (await admin.IntroduceAsync()).ShouldBeGreaterThan(0);
            await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE kind = ANY(@k)", ("k", MpSendingSteps.MpMailKinds));
            var weekly = (await Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5))).Single();
            weekly.Status.ShouldBe("dry-run");
            (await Database.MpMailAsync()).ShouldBeEmpty("design 8.19.8: with sending off nothing for MPs is in the outbox");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_delivery WHERE run_id = @r AND status = 'dry-run'", ("r", weekly.Id))).ShouldBe(weekly.MpCount);
            (await Database.ScalarAsync<DateTime?>("SELECT first_summarised_at FROM appeals.bill_record WHERE bill_number = 'XVP-0618'")).ShouldBeNull("a dry run does not count as summarised");

            using var stranger = NewBrowser();
            var html = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            html.ShouldContain("Bandomoji suvestinė: niekam nesiųsta");
            html.ShouldContain("Bandymas: suvestinės sudaromos, bet Seimo nariams nesiunčiamos.");
        }
        finally
        {
            await CleanUpAsync(admin);
        }
    }

    [Fact]
    public async Task A_summary_prepared_the_day_before_goes_at_its_time_whether_approved_or_not_and_each_mp_gets_one_email_with_both_sides()
    {
        await Database.ResetMpMailAsync();
        var (moderator, admin, bill, monday) = await SummaryWeekAsync(24);
        try
        {
            var introduced = await admin.IntroduceAsync();
            introduced.ShouldBeGreaterThan(0);
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            var since = await Database.ScalarAsync<DateTime>("SELECT clock_timestamp()");

            // Owner, 2026-11-17: prepared on Sunday at the same time, 24 hours ahead; nothing goes to MPs yet and nothing is public.
            (await Factory.RunDueAsync(MpSendingSteps.At(monday.AddDays(-1), 6, 55))).ShouldBeEmpty("before Sunday 07:00");
            var prepared = (await Factory.RunDueAsync(MpSendingSteps.At(monday.AddDays(-1), 7, 5))).Single();
            (prepared.Status, prepared.Kind, prepared.RunDate).ShouldBe(("prepared", "weekly", monday));
            prepared.DueAt.ShouldBe(MpSendingSteps.At(monday, 7).ToUniversalTime());
            (await Database.MpMailAsync(kind: "email.mp-digest")).ShouldBeEmpty("a prepared summary queues nothing for MPs");
            using var stranger = NewBrowser();
            var date = monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            (await stranger.Client.GetAsync("/suvestines/" + date)).StatusCode.ShouldBe(HttpStatusCode.NotFound, "the public copy is made when it goes");

            // The moderators' screen lists it first, with the sending state and what it would hold, the MPs only counted.
            var screen = await moderator.Browser.GetJsonAsync<DigestModerationResponse>(DigestRoutes.Moderation);
            (screen.Sending, screen.SendingSetting, screen.Lock).ShouldBe(("automatic", "automatic", null));
            screen.Runs[0].Id.ShouldBe(prepared.Id);
            screen.Runs[0].MpCount.ShouldBe(introduced);
            screen.Runs[0].Items.ShouldContain(i => i.BillNumber == AppealSteps.ThirdBill);
            JsonSerializer.Serialize(screen).ShouldNotContain("Uosytė", customMessage: "no MP is named on the screen");
            (await stranger.Client.GetAsync(DigestRoutes.Moderation)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await stranger.Client.PostAsync(DigestRoutes.ApproveOf(prepared.Id), null)).StatusCode.ShouldNotBe(HttpStatusCode.OK);

            // "Patvirtinti" needs no fresh passkey confirmation (it sends nothing) and is pressed once.
            await Database.ExecuteAsync(
                "UPDATE identity.session SET passkey_confirmed_at = now() - interval '11 minutes' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", moderator.Email));
            using (var approved = await moderator.ApproveDigestAsync(prepared.Id))
            {
                approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
                var marked = await approved.ReadAsync<DigestRunResponse>();
                marked.Status.ShouldBe("prepared", "it still goes at its own time");
            }

            using (var again = await admin.ApproveDigestAsync(prepared.Id))
            {
                await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "digest.not-prepared");
            }

            (await moderator.Browser.GetJsonAsync<DigestModerationResponse>(DigestRoutes.Moderation)).Runs[0].ApprovedAt.ShouldNotBeNull();
            (await Factory.RunDueAsync(MpSendingSteps.At(monday, 6, 55))).ShouldBeEmpty("approved, it still waits for its time");
            (await Database.MpMailAsync(kind: "email.mp-digest")).ShouldBeEmpty();

            var run = (await Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5))).Single();
            run.Status.ShouldBe("sent");
            run.SentCount.ShouldBe(introduced);
            var weekly = run;

            // One email per MP, to the official address, with both sides and the links.
            var emails = await Database.MpMailAsync(kind: "email.mp-digest");
            emails.Count.ShouldBe(introduced);
            emails.Select(e => e.To).Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(introduced, "one email per MP");
            emails.ShouldAllBe(e => e.To.EndsWith("@lrs.lt", StringComparison.OrdinalIgnoreCase));
            var uosyte = emails.Single(e => e.To == MpSteps.Uosyte.Address);
            uosyte.Payload.GetProperty("recipient").GetString().ShouldBe("official");
            uosyte.Payload.GetProperty("bills")[0].GetProperty("item").GetProperty("against").GetInt32().ShouldBe(1);
            var stored = await Database.ScalarAsync<int?>("SELECT for_reason_count FROM notify.digest_item WHERE run_id = @r AND bill_number = @b", ("r", weekly.Id), ("b", uosyte.Payload.GetProperty("bills")[0].GetProperty("item").GetProperty("billNumber").GetString()!));
            stored.ShouldNotBeNull();
            uosyte.Payload.GetProperty("bills")[0].GetProperty("item").GetProperty("forReasonCount").GetInt32().ShouldBe(stored.Value, "the email carries the stored number of arguments");
            uosyte.Payload.GetProperty("preferencesPath").GetString()!.ShouldStartWith(MpMailRoutes.PreferencesPage + "/");
            uosyte.Payload.GetProperty("unsubscribePath").GetString()!.ShouldEndWith("/stop");
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Uosyte.SeimasPersonId)).ShouldBe("queued");
            (await Database.ScalarAsync<DateTime?>("SELECT first_summarised_at FROM appeals.bill_record WHERE id = @r", ("r", bill.Id)))
                .ShouldNotBeNull("design 8.12.2: a summary really sent counts as summarised");

            // Sent once: a later run of the job sends nothing again.
            (await Factory.RunDueAsync(MpSendingSteps.At(monday, 9))).ShouldBeEmpty();
            (await Database.MpMailAsync(kind: "email.mp-digest")).Count.ShouldBe(introduced);

            // The public copy says how many and when, never which MPs; the moderator is audited and named nowhere public.
            var page = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + date));
            page.ShouldContain($"Išsiųsta Seimo nariams: {introduced}");
            page.ShouldNotContain("Bandymas: suvestinės sudaromos");
            page.ShouldNotContain("Uosytė");
            var api = await stranger.GetJsonAsync<List<DigestRunResponse>>(DigestRoutes.DigestOn(monday));
            JsonSerializer.Serialize(api).ShouldNotContain("lrs.lt");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'digest.sent' AND target = @t AND actor_user_id IS NULL", ("t", "digest:" + weekly.Id))).ShouldBe(1, "the system let it go");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'digest.approved' AND target = @t AND actor_role = 'moderator'", ("t", "digest:" + weekly.Id))).ShouldBe(1);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE details::text LIKE '%@%' AND occurred_at >= @s", ("s", since))).ShouldBe(0, "no address in an audit detail");
        }
        finally
        {
            await CleanUpAsync(admin);
        }
    }

    [Fact]
    public async Task A_declined_summary_sends_nothing_says_why_publicly_and_frees_its_place_under_the_ceiling()
    {
        await Database.ResetMpMailAsync();
        var (moderator, admin, _, monday) = await SummaryWeekAsync(26);
        try
        {
            var introduced = await admin.IntroduceAsync();
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            await admin.SetFreshAsync("Digest:MaxPerWeek", 1);
            var weekly = (await Factory.RunDueAsync(MpSendingSteps.At(monday.AddDays(-1), 7, 5))).Single();
            weekly.Status.ShouldBe("prepared");

            using (var tooShort = await moderator.Browser.PostAsync(DigestRoutes.CancelOf(weekly.Id), new CancelDigestRequest { Statement = "Per trumpa." }))
            {
                tooShort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            const string Statement = "Darbotvarkė pasikeitė, projektas nesvarstomas šią savaitę.";
            using (var cancelled = await moderator.Browser.PostAsync(DigestRoutes.CancelOf(weekly.Id), new CancelDigestRequest { Statement = Statement }))
            {
                cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
                (await cancelled.ReadAsync<DigestRunResponse>()).Status.ShouldBe("cancelled");
            }

            (await Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5))).ShouldBeEmpty("a declined summary does not go, nor is the week's made again");
            (await Database.MpMailAsync(kind: "email.mp-digest")).ShouldBeEmpty();
            using var stranger = NewBrowser();
            var html = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            html.ShouldContain("Nesiųsta");
            html.ShouldContain(Statement);
            html.ShouldContain(AppealSteps.ThirdBill, customMessage: "the copy shows what it held when it was declined");

            // The bill's page says it too, in the row of the summary that held it: not read as sent.
            var bill = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + AppealSteps.ThirdBill));
            System.Text.RegularExpressions.Regex.IsMatch(bill, $"id=\"digest-status-{weekly.Id}\"[^>]*>nesiųsta<").ShouldBeTrue("the row's status");
            using (var approve = await moderator.ApproveDigestAsync(weekly.Id))
            {
                await approve.ShouldBeProblemAsync(HttpStatusCode.Conflict, "digest.not-prepared");
            }

            // The ceiling of one a week: the declined summary holds no place, so Wednesday's before-vote one reaches every MP.
            var beforeVote = (await Factory.RunDueAsync(MpSendingSteps.At(monday.AddDays(2), 12, 5))).Single();
            beforeVote.Kind.ShouldBe("before-vote");
            beforeVote.Status.ShouldBe("sent");
            beforeVote.MpCount.ShouldBe(introduced, "a cancelled summary frees its place");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_delivery WHERE run_id = @r AND status = 'skipped-ceiling'", ("r", beforeVote.Id))).ShouldBe(0);
        }
        finally
        {
            await CleanUpAsync(admin);
        }
    }

    [Fact]
    public async Task No_summary_goes_before_the_introduction_letter()
    {
        await Database.ResetMpMailAsync();
        var (moderator, admin, _, monday) = await SummaryWeekAsync(30);
        try
        {
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            var screen = await moderator.Browser.GetJsonAsync<DigestModerationResponse>(DigestRoutes.Moderation);
            (screen.Sending, screen.SendingSetting, screen.Lock).ShouldBe(("off", "automatic", "no-introduction"));
            (await Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5))).Single().Status.ShouldBe("dry-run");
            (await Database.MpMailAsync()).ShouldBeEmpty();
            using var stranger = NewBrowser();
            (await stranger.GetJsonAsync<DigestRulesResponse>(DigestRoutes.Rules)).Sending.ShouldBe("off");
        }
        finally
        {
            await CleanUpAsync(admin);
        }
    }

    /// <summary>
    /// Design 8.19.1: the data speaks for itself. The test database's parliament data is the stand-in's (synthetic), so with
    /// <c>Demo:Allowed</c> false and the mail not declared caught, no MP mail of any kind leaves: no letter, no test copy, no
    /// summary, no preference email.
    /// </summary>
    [Fact]
    public async Task Synthetic_parliament_data_locks_mp_mail_even_when_demo_allowed_is_off()
    {
        (await Database.ScalarAsync<long>("SELECT count(*) FROM parliament.raw_document WHERE synthetic")).ShouldBeGreaterThan(0, "the seeded parliament is the stand-in's");
        await Database.ResetMpMailAsync();
        await using var forgotten = new InventedDataFactory(Database, demo: false, caught: false);
        var admin = await StaffMembers.ActiveAsync(forgotten, Database, "administrator");
        using var _a = admin;
        try
        {
            using (var saved = await admin.Browser.SendAsync(HttpMethod.Put, MpSendingAdminRoutes.Introduction, new IntroductionRequest
            {
                Subject = "Tema", Text = MpSendingSteps.Letter, SignerName = "Rūta Mockutė", SignerRole = "Vadovė",
            }))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            (await admin.Browser.GetJsonAsync<IntroductionResponse>(MpSendingAdminRoutes.Introduction)).MailMayLeave.ShouldBeFalse();
            using (var test = await admin.Browser.PostAsync(MpSendingAdminRoutes.IntroductionTest, new { }))
            {
                await test.ShouldBeProblemAsync(HttpStatusCode.Conflict, "mp-mail.not-caught");
            }

            using (var confirmed = await admin.Browser.ConfirmPasskeyAsync(admin.Authenticator))
            {
                confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            using (var send = await admin.Browser.PostAsync(MpSendingAdminRoutes.IntroductionSend, new { }))
            {
                await send.ShouldBeProblemAsync(HttpStatusCode.Conflict, "mp-mail.not-caught");
            }

            // A letter sent elsewhere and sending on: still locked, and the link's emails stay unsent too.
            await Database.ExecuteAsync("UPDATE notify.introduction_letter SET sent_at = now()");
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            await using (var scope = forgotten.Services.CreateAsyncScope())
            {
                var sending = await scope.ServiceProvider.GetRequiredService<IDigests>().SendingAsync(CancellationToken.None);
                (sending.Mode, sending.Lock).ShouldBe(("off", "not-caught"));
            }

            var keys = new Lt2.Notifications.MpMail.MailKeys(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:MailHashKey"] = "test-mail-hash-key" }).Build());
            var token = keys.DerivedToken(MpSteps.Uosyte.SeimasPersonId, "preferences", 0);
            await Database.ExecuteAsync(
                "INSERT INTO notify.mp_mail_token (id, seimas_person_id, kind, token_hash, created_at) VALUES (gen_random_uuid(), @p, 'preferences', @h, now())",
                ("p", MpSteps.Uosyte.SeimasPersonId), ("h", Lt2.Notifications.MpMail.MailKeys.TokenHash(token)));
            using var office = forgotten.NewBrowser();
            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(token), new MpMailFrequencyRequest { Frequency = "weekly" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            using (var renew = await office.PostAsync(MpMailRoutes.RenewOf(token), new { }))
            {
                await renew.ShouldBeProblemAsync(HttpStatusCode.Conflict, "mp-mail.not-caught");
            }

            (await Database.MpMailAsync()).ShouldBeEmpty("no MP mail of any kind reached the outbox");
        }
        finally
        {
            await admin.ResetFreshAsync("Digest:Sending");
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task Where_the_data_is_invented_no_mp_mail_leaves_unless_the_mail_is_declared_caught()
    {
        await Database.ResetMpMailAsync();
        await using var invented = new InventedDataFactory(Database, demo: true, caught: false);
        var admin = await StaffMembers.ActiveAsync(invented, Database, "administrator");
        using var _a = admin;
        try
        {
            using (var saved = await admin.Browser.SendAsync(HttpMethod.Put, MpSendingAdminRoutes.Introduction, new IntroductionRequest
            {
                Subject = "Tema", Text = MpSendingSteps.Letter, SignerName = "Rūta Mockutė", SignerRole = "Vadovė",
            }))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            var state = await admin.Browser.GetJsonAsync<IntroductionResponse>(MpSendingAdminRoutes.Introduction);
            state.MailMayLeave.ShouldBeFalse();
            using (var send = await admin.Browser.PostAsync(MpSendingAdminRoutes.IntroductionSend, new { }))
            {
                await send.ShouldBeProblemAsync(HttpStatusCode.Conflict, "mp-mail.not-caught");
            }

            (await Database.MpMailAsync()).ShouldBeEmpty();

            // Even with a letter sent and sending on, the lock holds where the mail is not caught.
            await Database.ExecuteAsync("UPDATE notify.introduction_letter SET sent_at = now()");
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            await using var scope = invented.Services.CreateAsyncScope();
            var sending = await scope.ServiceProvider.GetRequiredService<IDigests>().SendingAsync(CancellationToken.None);
            (sending.Mode, sending.Lock).ShouldBe(("off", "not-caught"));
        }
        finally
        {
            await admin.ResetFreshAsync("Digest:Sending");
            await Database.ResetMpMailAsync();
        }

        await using var caught = new InventedDataFactory(Database, demo: true, caught: true);
        await using var caughtScope = caught.Services.CreateAsyncScope();
        await Database.ExecuteAsync("INSERT INTO notify.introduction_letter (version, subject, text, signer_name, signer_role, written_by, written_at, sent_at) VALUES (1, 's', 't', 'n', 'r', gen_random_uuid(), now(), now())");
        try
        {
            (await caughtScope.ServiceProvider.GetRequiredService<IMpMailAdministration>().IntroductionAsync(CancellationToken.None)).MailMayLeave.ShouldBeTrue();
        }
        finally
        {
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task The_letter_goes_once_per_mp_a_newcomer_gets_it_in_place_of_a_summary_and_a_new_version_only_to_those_who_had_none()
    {
        await Database.ResetMpMailAsync();
        var (moderator, admin, _, monday) = await SummaryWeekAsync(32);
        try
        {
            var first = await admin.IntroduceAsync();
            (await Database.MpMailAsync(MpSteps.Uosyte.Address, "email.mp-introduction")).Count.ShouldBe(1);
            (await admin.IntroduceAsync()).ShouldBe(0, "once per MP: nobody has had none");

            // Laima Uosytė as if she joined the Seimas after the letter went: her next summary is the letter instead.
            await Database.ExecuteAsync(
                "DELETE FROM notify.digest_delivery WHERE seimas_person_id = @p AND run_id IN (SELECT id FROM notify.digest_run WHERE kind = 'introduction')",
                ("p", MpSteps.Uosyte.SeimasPersonId));
            await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE kind = ANY(@k)", ("k", MpSendingSteps.MpMailKinds));
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            var weekly = (await Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5))).Single();
            weekly.Status.ShouldBe("sent");
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Uosyte.SeimasPersonId)).ShouldBe("introduced");
            (await Database.MpMailAsync(MpSteps.Uosyte.Address)).Select(m => m.Kind).ShouldBe(["email.mp-introduction"]);
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Berzinyte.SeimasPersonId)).ShouldBe("queued");
            weekly.SentCount.ShouldBe(first - 1);

            // A changed text is a new version, sent to MPs who have had none: nobody now.
            (await admin.IntroduceAsync("Lietuva 2.0 – antra versija")).ShouldBe(0);
            var state = await admin.Browser.GetJsonAsync<IntroductionResponse>(MpSendingAdminRoutes.Introduction);
            state.Current!.Version.ShouldBe(2);
            state.Sent.Select(v => v.Version).ShouldBe([2, 1]);
            using var stranger = NewBrowser();
            var page = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync(DigestRoutes.IntroductionPage));
            page.ShouldContain("Lietuva 2.0 – antra versija");
            page.ShouldContain("Rūta Mockutė");
            page.ShouldContain("Ankstesnės versijos");
            // Owner, 2026-11-18: the letter in force opens the versions with what its text changed; here only the subject did.
            page.ShouldContain("Dabartinė versija, išsiųsta");
            page.ShouldNotContain("<ins>");
            (await stranger.GetJsonAsync<List<IntroductionLetterResponse>>(DigestRoutes.Introduction)).Count.ShouldBe(2);
            moderator.ShouldNotBeNull();
        }
        finally
        {
            await CleanUpAsync(admin);
        }
    }

    [Fact]
    public async Task Preferences_and_the_suppression_list_keep_mps_out_and_a_confirmed_extra_recipient_gets_a_copy()
    {
        await Database.ResetMpMailAsync();
        var (moderator, admin, _, monday) = await SummaryWeekAsync(34);
        try
        {
            await admin.IntroduceAsync();
            var uosyte = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);
            var liepaitis = await Database.PreferencesTokenAsync(MpSteps.Liepaitis.Address);
            var gluosnis = await Database.PreferencesTokenAsync(MpSteps.Gluosnis.Address);
            using var office = NewBrowser();
            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(uosyte), new MpMailFrequencyRequest { Frequency = "stopped" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(liepaitis), new MpMailFrequencyRequest { Frequency = "before-vote" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.ExtraOf(gluosnis), new MpMailExtraRequest { Address = "padejejas.gluosnio@pavyzdys.test" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            var confirm = await Database.NoticeTokenAsync(MpSteps.Gluosnis.Address, "extra-confirm");
            (await office.PostAsync(MpMailRoutes.ConfirmExtra, new MpMailTokenRequest { Token = confirm })).StatusCode.ShouldBe(HttpStatusCode.OK);
            using (var suppressed = await admin.Browser.PostAsync(MpSendingAdminRoutes.Suppression, new SuppressRequest { Address = MpSteps.Berzinyte.Address, Statement = "Biuras paprašė nerašyti." }))
            {
                suppressed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await suppressed.Content.ReadAsStringAsync());
            }

            await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE kind = ANY(@k)", ("k", MpSendingSteps.MpMailKinds));
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            var weekly = (await Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5))).Single();
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Uosyte.SeimasPersonId)).ShouldBe("skipped-preference");
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Liepaitis.SeimasPersonId)).ShouldBe("skipped-preference", "before votes only");
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Berzinyte.SeimasPersonId)).ShouldBe("suppressed");
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Gluosnis.SeimasPersonId)).ShouldBe("queued");
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Gluosnis.SeimasPersonId, "extra")).ShouldBe("queued");
            foreach (var mp in new[] { MpSteps.Uosyte, MpSteps.Liepaitis, MpSteps.Berzinyte })
            {
                (await Database.MpMailAsync(mp.Address)).ShouldBeEmpty(mp.Name);
            }

            var copy = (await Database.MpMailAsync("padejejas.gluosnio@pavyzdys.test", "email.mp-digest")).ShouldHaveSingleItem();
            copy.Payload.GetProperty("recipient").GetString().ShouldBe("extra");
            copy.Payload.GetProperty("mpName").GetString().ShouldBe("Kęstutis Gluosnis");
            copy.Payload.GetProperty("preferencesPath").ValueKind.ShouldBe(JsonValueKind.Null, "a copy never carries the MP's link");
            copy.Payload.GetProperty("copyStopPath").GetString()!.ShouldStartWith(MpMailRoutes.CopiesPage + "/");

            // The screen "Nesiunčiama" lists the MP, never the address; the other entries are only counted.
            var list = await admin.Browser.GetJsonAsync<SuppressionResponse>(MpSendingAdminRoutes.Suppression);
            list.Mps.ShouldHaveSingleItem().Slug.ShouldBe(await Database.SlugAsync(MpSteps.Berzinyte));
            list.Mps[0].Reason.ShouldBe("staff");
            JsonSerializer.Serialize(list).ShouldNotContain("@");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.suppression WHERE address_hash LIKE '%berzinyte%'")).ShouldBe(0);
            using (var removed = await admin.Browser.SendAsync(HttpMethod.Delete, MpSendingAdminRoutes.Suppression, new UnsuppressRequest { EntryId = list.Mps[0].Id, Statement = "Biuras persigalvojo." }))
            {
                removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            (await admin.Browser.GetJsonAsync<SuppressionResponse>(MpSendingAdminRoutes.Suppression)).Mps.ShouldBeEmpty();
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action IN ('suppression.added', 'suppression.removed') AND details::text NOT LIKE '%@%'")).ShouldBeGreaterThanOrEqualTo(2);
            using var member = NewBrowser();
            (await member.Client.GetAsync(MpSendingAdminRoutes.Suppression)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            moderator.ShouldNotBeNull();
        }
        finally
        {
            await CleanUpAsync(admin);
        }
    }

    [Fact]
    public async Task Two_moderators_approving_one_summary_at_the_same_moment_approve_it_once_and_two_runs_of_the_job_send_it_once()
    {
        await Database.ResetMpMailAsync();
        var (moderator, admin, _, monday) = await SummaryWeekAsync(36);
        try
        {
            await admin.IntroduceAsync();
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            var weekly = (await Factory.RunDueAsync(MpSendingSteps.At(monday.AddDays(-1), 7, 5))).Single();
            var answers = await Task.WhenAll(moderator.ApproveDigestAsync(weekly.Id), admin.ApproveDigestAsync(weekly.Id));
            answers.Select(a => a.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
            foreach (var answer in answers)
            {
                answer.Dispose();
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'digest.approved' AND target = @t", ("t", "digest:" + weekly.Id))).ShouldBe(1);
            var runs = await Task.WhenAll(Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5)), Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 6)));
            var sent = runs.SelectMany(r => r).First(r => r.Id == weekly.Id);
            sent.Status.ShouldBe("sent");
            (await Database.MpMailAsync(kind: "email.mp-digest")).Count.ShouldBe(sent.SentCount, "one email per MP, not two");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'digest.sent' AND target = @t", ("t", "digest:" + weekly.Id))).ShouldBe(1);
        }
        finally
        {
            await CleanUpAsync(admin);
        }
    }

    /// <summary>
    /// Design 8.19.8: a simulated month of agenda changes with review, preferences, the suppression list, the ceiling and
    /// the thresholds in play. Week 1 sends Monday's summary and the before-vote one is covered by it; week 2 declines Monday's
    /// on Sunday, so a bill moved from Tuesday to Thursday gets two before-vote summaries within the ceiling; in week 3 the only
    /// bill is held during review, so nothing goes; week 4 sends Monday's and Tuesday's and the third of the week meets the ceiling.
    /// </summary>
    [Fact]
    public async Task A_simulated_month_of_agenda_changes_keeps_review_preferences_suppression_the_ceiling_and_the_thresholds()
    {
        await Database.ResetMpMailAsync();
        var (moderator, admin) = await StaffAsync();
        await admin.SetFreshAsync("Digest:ThresholdMinimum", 1);
        var weeks = Enumerable.Range(40, 4).Select(ReasonsToComeSteps.MondayInWeeks).ToArray();
        try
        {
            var (member, _) = await Factory.MemberAsync();
            PreparedBill third;
            using (member)
            {
                third = await Database.PrepareBillAsync(member, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);
                await Database.PrepareBillAsync(member, moderator.Browser, AppealSteps.SecondBill, "for", threshold: 50);
            }

            await admin.IntroduceAsync();
            using var office = NewBrowser();
            async Task ChooseAsync(StandInMp mp, string frequency) =>
                (await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(await Database.PreferencesTokenAsync(mp.Address)), new MpMailFrequencyRequest { Frequency = frequency }))
                    .StatusCode.ShouldBe(HttpStatusCode.OK);
            await ChooseAsync(MpSteps.Uosyte, "weekly");
            await ChooseAsync(MpSteps.Gluosnis, "before-vote");
            await ChooseAsync(MpSteps.Liepaitis, "stopped");
            (await admin.Browser.PostAsync(MpSendingAdminRoutes.Suppression, new SuppressRequest { Address = MpSteps.Berzinyte.Address, Statement = "Bandymas." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            await admin.SetFreshAsync("Digest:MaxPerWeek", 2);

            async Task<IReadOnlyList<DigestRunInfo>> DueAsync(DateOnly day, int hour) => await Factory.RunDueAsync(MpSendingSteps.At(day, hour, 5));
            async Task StatementAsync(string route, Guid id, string statement) =>
                (await moderator.Browser.PostAsync(AppealRoutes.With(route, id), new StatementRequest { Statement = statement })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // Week 1: Thursday; Monday's summary sent; Wednesday's would be the same sitting, covered by Monday's.
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[0].AddDays(3), "adoption");
            await Database.PlanAsync(AppealSteps.SecondBill, weeks[0].AddDays(3), "adoption");
            var w1 = (await DueAsync(weeks[0], 7)).Of("weekly");
            w1.Status.ShouldBe("sent");
            (await DueAsync(weeks[0].AddDays(2), 12)).ShouldBeEmpty("covered by Monday's summary");

            // Week 2: Monday's declined on Sunday; the bill planned for Tuesday, then moved to Thursday.
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[1].AddDays(1), "adoption");
            var w2 = (await DueAsync(weeks[1].AddDays(-1), 7)).Of("weekly");
            w2.Status.ShouldBe("prepared");
            (await moderator.Browser.PostAsync(DigestRoutes.CancelOf(w2.Id), new CancelDigestRequest { Statement = "Bandymas: suvestinė atšaukiama mėnesio bandyme." }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
            // Monday morning: nothing weekly goes; Tuesday's before-vote summary is prepared (sent at noon).
            (await DueAsync(weeks[1], 7)).ShouldAllBe(r => r.Kind == "before-vote" && r.Status == "prepared", "declined on Sunday, nothing goes on Monday");
            var w2Tuesday = (await DueAsync(weeks[1], 12)).Of("before-vote");
            w2Tuesday.Status.ShouldBe("sent");
            w2Tuesday.Items.Select(i => i.BillNumber).ShouldBe([AppealSteps.ThirdBill], "a declined Monday covers nothing");
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[1].AddDays(3), "adoption");
            (await DueAsync(weeks[1].AddDays(2), 12)).Of("before-vote").Status.ShouldBe("sent");

            // Week 3: the only bill is held during review; nothing is left to go at Monday's time.
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[2].AddDays(3), "adoption");
            var w3 = (await DueAsync(weeks[2].AddDays(-1), 7)).Of("weekly");
            w3.Items.Select(i => i.BillNumber).ShouldBe([AppealSteps.ThirdBill]);
            await StatementAsync(AppealRoutes.HoldRecord, third.Id, "Bandymas: sulaikoma peržiūros metu.");
            (await DueAsync(weeks[2], 7)).Of("weekly").Status.ShouldBe("dropped");
            await StatementAsync(AppealRoutes.ReleaseRecord, third.Id, "Bandymas: siuntimas grąžinamas.");

            // Week 4: Monday's sent; a bill that reached its threshold after it is added for Tuesday (sent); the first bill moves
            // to Friday, and that third summary of the week meets the ceiling of two for every MP who had both.
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[3].AddDays(1), "adoption");
            await Database.PlanAsync(AppealSteps.SecondBill, weeks[3].AddDays(3), "adoption");
            (await DueAsync(weeks[3], 7)).Of("weekly").Status.ShouldBe("sent");
            var (late, _) = await Factory.MemberAsync();
            using (late)
            {
                await Database.PrepareBillAsync(late, moderator.Browser, AppealSteps.TaxBill, "for", threshold: 1);
            }

            await Database.PlanAsync(AppealSteps.TaxBill, weeks[3].AddDays(1), "adoption");
            var w4Tuesday = (await DueAsync(weeks[3], 12)).Of("before-vote");
            w4Tuesday.Items.Select(i => i.BillNumber).ShouldBe([AppealSteps.TaxBill], "the first bill was covered by Monday's summary for the same sitting");
            await Database.PlanAsync(AppealSteps.ThirdBill, weeks[3].AddDays(4), "adoption");
            var w4Friday = (await DueAsync(weeks[3].AddDays(3), 12)).Of("before-vote");
            w4Friday.Status.ShouldBe("sent");

            // Over the month: the thresholds, the choices, the suppression list and the ceiling held for every MP.
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_item i JOIN notify.digest_run r ON r.id = i.run_id WHERE i.bill_number = @b AND r.run_date >= @f",
                ("b", AppealSteps.SecondBill), ("f", weeks[0]))).ShouldBe(0, "a bill under its threshold is in no summary");
            var perWeek = await Database.ScalarAsync<long>(
                """
                SELECT coalesce(max(n), 0) FROM (
                    SELECT count(*) AS n FROM notify.digest_delivery d JOIN notify.digest_run r ON r.id = d.run_id
                    WHERE d.recipient = 'official' AND d.status = 'queued' AND r.run_date >= @f AND r.kind <> 'introduction'
                    GROUP BY d.seimas_person_id, date_trunc('week', r.run_date)) AS weekly
                """,
                ("f", weeks[0]));
            perWeek.ShouldBeLessThanOrEqualTo(2, "never over the ceiling");
            async Task<List<(string Kind, string Status)>> OfAsync(StandInMp mp)
            {
                var rows = new List<(string, string)>();
                await using var connection = new Npgsql.NpgsqlConnection(Database.AdminConnectionString);
                await connection.OpenAsync();
                await using var command = new Npgsql.NpgsqlCommand(
                    "SELECT r.kind, d.status FROM notify.digest_delivery d JOIN notify.digest_run r ON r.id = d.run_id WHERE d.seimas_person_id = @p AND d.recipient = 'official' AND r.status = 'sent' AND r.kind <> 'introduction' AND r.run_date >= @f",
                    connection);
                command.Parameters.AddWithValue("p", mp.SeimasPersonId);
                command.Parameters.AddWithValue("f", weeks[0]);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add((reader.GetString(0), reader.GetString(1)));
                }

                return rows;
            }

            var uosyte = await OfAsync(MpSteps.Uosyte);
            uosyte.Where(r => r.Status == "queued").Select(r => r.Kind).Distinct().ShouldBe(["weekly"]);
            uosyte.Where(r => r.Kind == "before-vote").ShouldAllBe(r => r.Status == "skipped-preference");
            var gluosnis = await OfAsync(MpSteps.Gluosnis);
            gluosnis.Where(r => r.Status == "queued").Select(r => r.Kind).Distinct().ShouldBe(["before-vote"]);
            (await OfAsync(MpSteps.Liepaitis)).ShouldAllBe(r => r.Status == "skipped-preference");
            (await OfAsync(MpSteps.Berzinyte)).ShouldAllBe(r => r.Status == "suppressed");
            (await Database.MpMailAsync(MpSteps.Berzinyte.Address, "email.mp-digest")).ShouldBeEmpty();
            (await Database.MpMailAsync(MpSteps.Liepaitis.Address, "email.mp-digest")).ShouldBeEmpty();
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_delivery d JOIN notify.digest_run r ON r.id = d.run_id WHERE r.status IN ('cancelled', 'dropped') AND d.status = 'queued'"))
                .ShouldBe(0, "a declined or dropped summary sent nothing");
            var friday = await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_delivery WHERE run_id = @r AND status = 'skipped-ceiling'", ("r", w4Friday.Id));
            friday.ShouldBeGreaterThan(0, "the third summary of the week meets the ceiling for MPs who had two");
            (await Database.DeliveryAsync(w4Friday.Id, MpSteps.Gluosnis.SeimasPersonId)).ShouldBe("queued", "he had only one before-vote summary that week");
        }
        finally
        {
            await CleanUpAsync(admin);
            await Database.FreeBillAsync(AppealSteps.SecondBill);
            await Database.FreeBillAsync(AppealSteps.TaxBill);
        }
    }

    /// <summary>The web application where the data is invented (<c>Demo:Allowed</c>), with its mail caught or not.</summary>
    private sealed class InventedDataFactory(DatabaseFixture database, bool demo, bool caught) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Demo:Allowed", demo ? "true" : "false");
            builder.UseSetting("Email:Caught", caught ? "true" : "false");
        }
    }
}
