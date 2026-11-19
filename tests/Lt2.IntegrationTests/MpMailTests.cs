using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Digests;
using Lt2.Audit;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.19.4, 8.19.6: the MP's choices without an account (the link's page and routes, the one-click stop, a new link, the
/// extra recipient), the same choice for a linked account, and the suppression of an address a server refused for good.
/// </summary>
public sealed partial class MpMailTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Extra = "padejeja.uosytes@pavyzdys.test";

    /// <summary>The letter sent, so every sitting MP has a link; the outbox emptied of it, so a test reads only its own notices.</summary>
    private async Task<StaffMember> IntroducedAsync()
    {
        await Database.ResetMpMailAsync();
        var admin = await AdministratorAsync();
        (await admin.IntroduceAsync()).ShouldBeGreaterThan(0);
        return admin;
    }

    private async Task<List<OutboxMpMail>> NoticesAsync(string to) =>
        (await Database.MpMailAsync(to, "email.mp-mail-notice")).ToList();

    [Fact]
    public async Task The_links_page_shows_the_mp_and_the_choice_and_every_change_is_confirmed_to_the_official_address()
    {
        await IntroducedAsync();
        try
        {
            var token = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);
            using var office = NewBrowser();
            var choice = await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(token));
            (choice.FirstName, choice.LastName, choice.Frequency, choice.ExtraAddress).ShouldBe(("Laima", "Uosytė", "default", null));

            using (var page = await office.Client.GetAsync(MpMailRoutes.PreferencesPageOf(token)))
            {
                page.StatusCode.ShouldBe(HttpStatusCode.OK);
                page.Headers.GetValues("X-Robots-Tag").ShouldContain("noindex, nofollow");
                page.Headers.CacheControl!.NoStore.ShouldBeTrue();
                page.Headers.GetValues("Referrer-Policy").ShouldContain("no-referrer");
                var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
                html.ShouldContain("Seimo narys: Laima Uosytė.");
                html.ShouldContain("Kas savaitę ir prieš balsavimus");
                html.ShouldContain("<meta name=\"robots\" content=\"noindex, nofollow\"");
            }

            using (var set = await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(token), new MpMailFrequencyRequest { Frequency = "weekly" }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
                (await set.ReadAsync<MpMailChoiceResponse>()).Frequency.ShouldBe("weekly");
            }

            var notice = (await NoticesAsync(MpSteps.Uosyte.Address)).ShouldHaveSingleItem();
            notice.Payload.GetProperty("notice").GetString().ShouldBe("changed");
            notice.Payload.GetProperty("frequency").GetString().ShouldBe("weekly");
            notice.Payload.GetProperty("path").GetString().ShouldBe(MpMailRoutes.PreferencesPageOf(token), "the same link, to undo it");

            // Setting the same choice again is no change and sends nothing.
            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(token), new MpMailFrequencyRequest { Frequency = "weekly" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await NoticesAsync(MpSteps.Uosyte.Address)).Count.ShouldBe(1);
            using var invalid = await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(token), new MpMailFrequencyRequest { Frequency = "daily" });
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            // Nothing public says what an MP chose.
            using var stranger = NewBrowser();
            WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/p/" + await Database.SlugAsync(MpSteps.Uosyte))).ShouldNotContain("Tik kas savaitę");
        }
        finally
        {
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task The_page_works_without_javascript_its_form_posts_back_and_changes_the_choice()
    {
        await IntroducedAsync();
        try
        {
            var token = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);
            using var office = NewBrowser();
            var path = MpMailRoutes.PreferencesPageOf(token);
            var html = await office.Client.GetStringAsync(path);
            var antiforgery = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
            antiforgery.ShouldNotBeEmpty("the server-rendered form carries its antiforgery token");
            using var posted = await office.Client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "mp-mail-frequency",
                ["__RequestVerificationToken"] = antiforgery,
                ["Frequency.Value"] = "before-vote",
            }));
            posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
            WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync()).ShouldContain("Išsaugota. Patvirtinimą išsiuntėme oficialiu adresu.");
            (await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(token))).Frequency.ShouldBe("before-vote");
        }
        finally
        {
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task The_one_click_stop_needs_no_origin_or_antiforgery_token_and_every_other_token_route_still_does()
    {
        await IntroducedAsync();
        try
        {
            var token = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);

            // As a mail program sends it (RFC 8058): a form body, no Origin header, no cookie, no antiforgery token.
            using var mailProgram = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri(Lt2Factory.Origin) });
            using (var stop = await mailProgram.PostAsync(MpMailRoutes.StopOf(token), new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" })))
            {
                stop.StatusCode.ShouldBe(HttpStatusCode.NoContent, await stop.Content.ReadAsStringAsync());
            }

            using var office = NewBrowser();
            (await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(token))).Frequency.ShouldBe("stopped");
            (await NoticesAsync(MpSteps.Uosyte.Address)).ShouldHaveSingleItem().Payload.GetProperty("frequency").GetString().ShouldBe("stopped", "the stop is confirmed once");
            using (var unknown = await mailProgram.PostAsync(MpMailRoutes.StopOf(new string('A', 43)), null))
            {
                await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp-mail.not-found");
            }

            // Every other write about the link goes through the same-origin and antiforgery filter.
            var writes = new (HttpMethod Method, string Uri, object? Body)[]
            {
                (HttpMethod.Put, MpMailRoutes.TokenOf(token), new MpMailFrequencyRequest { Frequency = "default" }),
                (HttpMethod.Post, MpMailRoutes.RenewOf(token), new { }),
                (HttpMethod.Put, MpMailRoutes.ExtraOf(token), new MpMailExtraRequest { Address = Extra }),
                (HttpMethod.Delete, MpMailRoutes.ExtraOf(token), null),
                (HttpMethod.Post, MpMailRoutes.ConfirmExtra, new MpMailTokenRequest { Token = token }),
                (HttpMethod.Post, MpMailRoutes.StopCopies, new MpMailTokenRequest { Token = token }),
            };
            foreach (var (method, uri, body) in writes)
            {
                using var withoutToken = await office.SendAsync(method, uri, body, antiforgery: false);
                await withoutToken.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "antiforgery.invalid");
                using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : System.Net.Http.Json.JsonContent.Create(body, body.GetType()) };
                using var withoutOrigin = await mailProgram.SendAsync(request);
                await withoutOrigin.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "origin.invalid");
            }

            (await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(token))).Frequency.ShouldBe("stopped", "none of the refused writes changed anything");
        }
        finally
        {
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task An_unknown_a_wrong_and_a_replaced_token_get_the_same_answer_and_a_new_link_goes_to_the_official_address()
    {
        await IntroducedAsync();
        try
        {
            var token = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);
            using var office = NewBrowser();
            using (var renewed = await office.PostAsync(MpMailRoutes.RenewOf(token), new { }))
            {
                renewed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await renewed.Content.ReadAsStringAsync());
            }

            var fresh = await Database.NoticeTokenAsync(MpSteps.Uosyte.Address, "new-link");
            fresh.ShouldNotBe(token);
            (await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(fresh))).LastName.ShouldBe("Uosytė");

            // The old link, a token of another kind and an invented one: the same 404, the same body.
            await office.SendAsync(HttpMethod.Put, MpMailRoutes.ExtraOf(fresh), new MpMailExtraRequest { Address = Extra });
            var confirm = await Database.NoticeTokenAsync(MpSteps.Uosyte.Address, "extra-confirm");
            var bodies = new List<string>();
            var pages = new List<string>();
            foreach (var wrong in new[] { token, confirm, new string('x', 43), "short" })
            {
                using var answer = await office.Client.GetAsync(MpMailRoutes.TokenOf(wrong));
                answer.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                bodies.Add(Regex.Replace(await answer.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", ""));
                using var page = await office.Client.GetAsync(MpMailRoutes.PreferencesPageOf(wrong));
                page.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
                html.ShouldNotContain("Uosytė");
                pages.Add(Regex.Replace(Regex.Replace(html, "value=\"[^\"]*\"", ""), "<!--.*?-->", "", RegexOptions.Singleline));
            }

            bodies.Distinct().Count().ShouldBe(1, "an unknown and a wrong token answer the same");
            pages.Distinct().Count().ShouldBe(1, "and the page is the same 'not found'");
        }
        finally
        {
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task An_extra_recipient_is_confirmed_from_the_official_address_gets_copies_with_its_own_stop_and_can_be_removed()
    {
        var admin = await IntroducedAsync();
        var monday = ReasonsToComeSteps.MondayInWeeks(38);
        var (moderator, _) = await StaffAsync();
        try
        {
            var token = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);
            using var office = NewBrowser();
            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.ExtraOf(token), new MpMailExtraRequest { Address = "ne adresas" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using (var asked = await office.SendAsync(HttpMethod.Put, MpMailRoutes.ExtraOf(token), new MpMailExtraRequest { Address = Extra }))
            {
                asked.StatusCode.ShouldBe(HttpStatusCode.OK, await asked.Content.ReadAsStringAsync());
                var choice = await asked.ReadAsync<MpMailChoiceResponse>();
                (choice.ExtraAddress, choice.ExtraConfirmed).ShouldBe((Extra, false));
            }

            var request = (await NoticesAsync(MpSteps.Uosyte.Address)).Single(n => n.Payload.GetProperty("notice").GetString() == "extra-confirm");
            request.Payload.GetProperty("path").GetString()!.ShouldStartWith(MpMailRoutes.ConfirmPage + "/");
            request.Payload.GetProperty("extraAddress").GetString().ShouldBe(Extra);
            request.Payload.GetProperty("validHours").GetInt32().ShouldBe(24);
            (await Database.MpMailAsync(Extra)).ShouldBeEmpty("nothing goes to the extra address before the official one confirms it");

            // Opening the confirmation page confirms nothing; its button does, once.
            var confirm = await Database.NoticeTokenAsync(MpSteps.Uosyte.Address, "extra-confirm");
            (await office.Client.GetAsync(MpMailRoutes.ConfirmPage + "/" + confirm)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(token))).ExtraConfirmed.ShouldBeFalse();
            var confirmed = await office.PostJsonAsync<MpMailChoiceResponse>(MpMailRoutes.ConfirmExtra, new MpMailTokenRequest { Token = confirm });
            confirmed.ExtraConfirmed.ShouldBeTrue();
            using (var reused = await office.PostAsync(MpMailRoutes.ConfirmExtra, new MpMailTokenRequest { Token = confirm }))
            {
                await reused.ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp-mail.not-found");
            }

            (await NoticesAsync(MpSteps.Uosyte.Address)).Select(n => n.Payload.GetProperty("notice").GetString()).ShouldContain("extra-confirmed");
            var welcome = (await NoticesAsync(Extra)).ShouldHaveSingleItem();
            welcome.Payload.GetProperty("notice").GetString().ShouldBe("extra-welcome");
            welcome.Payload.GetProperty("mpName").GetString().ShouldBe("Laima Uosytė");

            // The next summary goes to both; the copy's stop removes the extra address and tells the official one.
            await admin.SetFreshAsync("Digest:ThresholdMinimum", 1);
            await admin.SetFreshAsync("Digest:Sending", "automatic");
            var (member, _) = await Factory.MemberAsync();
            using (member)
            {
                await Database.PrepareBillAsync(member, moderator.Browser, AppealSteps.SecondBill, "for", threshold: 1);
            }

            await Database.PlanAsync(AppealSteps.SecondBill, monday.AddDays(3));
            var weekly = (await Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5))).Single();
            (await Database.DeliveryAsync(weekly.Id, MpSteps.Uosyte.SeimasPersonId, "extra")).ShouldBe("queued");
            var copy = (await Database.MpMailAsync(Extra, "email.mp-digest")).ShouldHaveSingleItem();
            var stopPath = copy.Payload.GetProperty("copyStopPath").GetString()!;
            var stop = stopPath[(stopPath.LastIndexOf('/') + 1)..];
            (await office.Client.GetAsync(stopPath)).StatusCode.ShouldBe(HttpStatusCode.OK);
            using (var stopped = await office.PostAsync(MpMailRoutes.StopCopies, new MpMailTokenRequest { Token = stop }))
            {
                stopped.StatusCode.ShouldBe(HttpStatusCode.NoContent, await stopped.Content.ReadAsStringAsync());
            }

            (await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(token))).ExtraAddress.ShouldBeNull();
            (await NoticesAsync(MpSteps.Uosyte.Address)).Last().Payload.GetProperty("notice").GetString().ShouldBe("extra-removed");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.mp_email_pref WHERE extra_address IS NOT NULL")).ShouldBe(0, "a removed address is deleted");

            // The MP adds one again and removes it on the page.
            await office.SendAsync(HttpMethod.Put, MpMailRoutes.ExtraOf(token), new MpMailExtraRequest { Address = Extra });
            using (var removed = await office.SendAsync(HttpMethod.Delete, MpMailRoutes.ExtraOf(token)))
            {
                removed.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await removed.ReadAsync<MpMailChoiceResponse>()).ExtraAddress.ShouldBeNull();
            }
        }
        finally
        {
            await admin.ResetFreshAsync("Digest:Sending");
            await admin.ResetFreshAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number = 'XVP-0533'");
            await Database.ResetMpMailAsync();
            await Database.FreeBillAsync(AppealSteps.SecondBill);
        }
    }

    [Fact]
    public async Task A_linked_account_sees_and_changes_the_same_choice_only_in_a_passkey_session()
    {
        await IntroducedAsync();
        using var account = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        try
        {
            var mine = await account.Browser.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.Mine);
            (mine.LastName, mine.Frequency, mine.ExtraAddress).ShouldBe(("Kadagytė", "default", null));
            using (var set = await account.Browser.SendAsync(HttpMethod.Put, MpMailRoutes.Mine, new MpMailFrequencyRequest { Frequency = "weekly" }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
            }

            var token = await Database.PreferencesTokenAsync(MpSteps.Kadagyte.Address);
            using var office = NewBrowser();
            (await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(token))).Frequency.ShouldBe("weekly", "one choice, two ways in");
            (await NoticesAsync(MpSteps.Kadagyte.Address)).Last().Payload.GetProperty("frequency").GetString().ShouldBe("weekly");

            // Signed in by a code, the same account reads and writes nothing of it; an account without a link has none.
            using var codeSession = NewBrowser();
            using (var asked = await codeSession.PostAsync(ApiRoutes.Auth.Code, new Lt2.Api.Contracts.Auth.RequestCodeRequest { Email = account.Email }))
            {
                asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            await codeSession.SignInWithCodeAsync(account.Email);
            using (var refused = await codeSession.Client.GetAsync(MpMailRoutes.Mine))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
            }

            var (member, _) = await Factory.MemberAsync();
            using (member)
            {
                using var none = await member.Client.GetAsync(MpMailRoutes.Mine);
                await none.ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp.not-linked");
            }
        }
        finally
        {
            await account.Browser.DeleteAccountAsync();
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task A_permanent_refusal_marks_the_delivery_bounced_and_no_further_mp_mail_goes_to_the_address()
    {
        var admin = await IntroducedAsync();
        try
        {
            var introductions = await Database.MpMailAsync(MpSteps.Uosyte.Address, "email.mp-introduction");
            introductions.ShouldHaveSingleItem();
            var run = introductions[0].Payload.GetProperty("delivery").GetProperty("runId").GetGuid();
            await Database.ExecuteAsync("UPDATE notify.outbox SET due_at = now() + interval '1 day' WHERE kind = ANY(@k) AND lower(payload->>'to') <> @to",
                ("k", MpSendingSteps.MpMailKinds), ("to", MpSteps.Uosyte.Address));

            var transport = new RefusingTransport(MpSteps.Uosyte.Address);
            await using (var worker = WorkerServices(transport))
            {
                var dispatcher = worker.GetRequiredService<INotificationsDispatcher>();
                while (await dispatcher.DispatchDueAsync(CancellationToken.None) > 0)
                {
                }
            }

            transport.Tried.ShouldBe(1, "refused for good: not retried");
            (await Database.DeliveryAsync(run, MpSteps.Uosyte.SeimasPersonId)).ShouldBe("bounced");
            (await Database.MpMailAsync(MpSteps.Uosyte.Address)).ShouldBeEmpty("the row is gone, with its address");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.suppression WHERE reason = 'bounced'")).ShouldBe(1);
            var list = await admin.Browser.GetJsonAsync<SuppressionResponse>(MpSendingAdminRoutes.Suppression);
            list.Mps.ShouldHaveSingleItem().Reason.ShouldBe("bounced");
            list.Mps[0].Slug.ShouldBe(await Database.SlugAsync(MpSteps.Uosyte));

            // No further MP mail goes there: not even a new link, which could reach nobody (the old one keeps working).
            var keys = new Lt2.Notifications.MpMail.MailKeys(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:MailHashKey"] = "test-mail-hash-key" }).Build());
            var link = keys.DerivedToken(MpSteps.Uosyte.SeimasPersonId, "preferences", 0);
            using var office = NewBrowser();
            (await office.GetJsonAsync<MpMailChoiceResponse>(MpMailRoutes.TokenOf(link))).LastName.ShouldBe("Uosytė");
            using (var renew = await office.PostAsync(MpMailRoutes.RenewOf(link), new { }))
            {
                await renew.ShouldBeProblemAsync(HttpStatusCode.Conflict, "mp-mail.no-address");
            }

            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(link), new MpMailFrequencyRequest { Frequency = "weekly" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await Database.MpMailAsync(MpSteps.Uosyte.Address)).ShouldBeEmpty("the change is not confirmed to a suppressed address");
        }
        finally
        {
            await Database.ResetMpMailAsync();
        }
    }

    [Fact]
    public async Task No_address_token_or_choice_reaches_a_log_line_or_an_audit_detail()
    {
        await Database.ResetMpMailAsync();
        var sink = new CollectingSink();
        await using var logging = new LoggingFactory(Database, sink);
        var admin = await StaffMembers.ActiveAsync(logging, Database, "administrator");
        using var _a = admin;
        var since = await Database.ScalarAsync<DateTime>("SELECT clock_timestamp()");
        try
        {
            (await admin.IntroduceAsync()).ShouldBeGreaterThan(0);
            var token = await Database.PreferencesTokenAsync(MpSteps.Uosyte.Address);
            using var office = logging.NewBrowser();
            (await office.Client.GetAsync(MpMailRoutes.PreferencesPageOf(token))).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.TokenOf(token), new MpMailFrequencyRequest { Frequency = "before-vote" })).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await office.SendAsync(HttpMethod.Put, MpMailRoutes.ExtraOf(token), new MpMailExtraRequest { Address = Extra })).StatusCode.ShouldBe(HttpStatusCode.OK);
            var confirm = await Database.NoticeTokenAsync(MpSteps.Uosyte.Address, "extra-confirm");
            (await office.PostAsync(MpMailRoutes.ConfirmExtra, new MpMailTokenRequest { Token = confirm })).StatusCode.ShouldBe(HttpStatusCode.OK);
            using var mailProgram = logging.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri(Lt2Factory.Origin) });
            (await mailProgram.PostAsync(MpMailRoutes.StopOf(token), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await admin.Browser.PostAsync(MpSendingAdminRoutes.Suppression, new SuppressRequest { Address = MpSteps.Berzinyte.Address, Statement = "Bandymas." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var lines = sink.Lines;
            lines.ShouldContain(l => l.Contains("/suvestines/nustatymai/***", StringComparison.Ordinal), "the request was logged, without its token");
            foreach (var line in lines)
            {
                foreach (var secret in new[] { token, confirm, MpSteps.Uosyte.Address, MpSteps.Berzinyte.Address, Extra })
                {
                    line.ShouldNotContain(secret, Case.Insensitive);
                }
            }

            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE occurred_at >= @s AND (details::text LIKE '%@%' OR details::text LIKE '%before-vote%' OR details::text LIKE '%stopped%' OR target LIKE '%@%')",
                ("s", since))).ShouldBe(0);
        }
        finally
        {
            await Database.ResetMpMailAsync();
        }
    }

    private ServiceProvider WorkerServices(IEmailTransport transport)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:MailHashKey"] = "test-mail-hash-key" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(NpgsqlDataSource.Create(Database.WorkerConnectionString));
        services.AddNotificationsModule(configuration);
        services.AddNotificationsDispatcher(configuration);
        services.AddAuditModule();
        services.AddSingleton(transport);
        return services.BuildServiceProvider();
    }

    /// <summary>A mail server that refuses one recipient for good (550) and takes every other message.</summary>
    private sealed class RefusingTransport(string refused) : IEmailTransport
    {
        public int Tried { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (!string.Equals(message.To, refused, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            Tried++;
            throw new MailKit.Net.Smtp.SmtpCommandException(
                MailKit.Net.Smtp.SmtpErrorCode.RecipientNotAccepted, MailKit.Net.Smtp.SmtpStatusCode.MailboxUnavailable,
                new MimeKit.MailboxAddress("", message.To), $"550 5.1.1 <{message.To}>: no such user");
        }
    }
}
