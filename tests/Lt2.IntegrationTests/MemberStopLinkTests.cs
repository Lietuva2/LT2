using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Follows;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.Notifications.MpMail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.24.1 over HTTP: every email of the member's rhythm carries the account's stop link; its one-click POST, sent as a mail
/// program sends it, and the page's button turn every email switch off and leave the follows; opening the page changes nothing;
/// an unknown, a malformed and a valid token get the same answers; the link goes with the account.
/// </summary>
public sealed partial class MemberStopLinkTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Site = "https://lt2.pavyzdys.test";

    private static readonly NoticeSettingsResponse AllOn = new(true, true, true, true, true, true);
    private static readonly NoticeSettingsResponse AllOff = new(false, false, false, false, false, false);

    private static readonly MailKeys Keys = new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [MailKeys.ConfigurationKey] = "test-mail-hash-key" }).Build());

    private async Task RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
    }

    private Task<Guid> UserIdAsync(string email) =>
        Database.ScalarAsync<Guid>("SELECT id FROM identity.\"user\" WHERE email = @e", ("e", email));

    /// <summary>The account's link as the first email of its rhythm would make it: derived, its hash stored.</summary>
    private async Task<string> LinkAsync(string email)
    {
        var token = Keys.MemberStopToken(await UserIdAsync(email), 0);
        await Database.ExecuteAsync(
            "INSERT INTO notify.member_mail_token (user_id, token_hash, generation, created_at) VALUES (@u, @h, 0, now()) ON CONFLICT DO NOTHING",
            ("u", await UserIdAsync(email)), ("h", MailKeys.TokenHash(token)));
        return token;
    }

    /// <summary>A client as a mail program is: no cookie, no Origin header, no antiforgery token.</summary>
    private HttpClient MailProgram() =>
        Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri(Lt2Factory.Origin) });

    private static FormUrlEncodedContent OneClickBody() => new(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" });

    /// <summary>The email the dispatcher would send for an outbox payload, rendered by the worker's own renderers.</summary>
    private static EmailMessage Render(string kind, JsonElement payload)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Site:Address"] = Site }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationsModule(configuration);
        services.AddNotificationsDispatcher(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<Lt2.Notifications.Outbox.IEmailRenderer>().Single(r => r.Kind == kind).Render(payload);
    }

    [Fact]
    public async Task A_followed_bills_email_carries_the_link_whose_one_click_post_turns_every_switch_off_and_the_next_run_tells_nothing()
    {
        await Database.ForgetEventsAsync(AppealSteps.SecondBill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var (control, controlEmail) = await Factory.MemberAsync();
        using var _c = control;
        await member.FollowBillAsync(AppealSteps.SecondBill);
        await control.FollowBillAsync(AppealSteps.SecondBill);
        await Database.AgeFollowsAsync(email, TimeSpan.FromHours(1));
        await Database.AgeFollowsAsync(controlEmail, TimeSpan.FromHours(1));
        var date = ReasonsToComeSteps.VilniusToday().AddDays(5);
        await Database.PlanAsync(AppealSteps.SecondBill, date, "adoption");
        try
        {
            await RunJobAsync();
            using var told = JsonDocument.Parse((await Database.LatestEmailAsync("email.follow-planned", email)).ShouldNotBeNull());
            var token = told.RootElement.GetProperty("stopToken").GetString().ShouldNotBeNull();
            token.ShouldBe(Keys.MemberStopToken(await UserIdAsync(email), 0), "derived from the account, the same in every email");
            (await Database.ScalarAsync<string>("SELECT token_hash FROM notify.member_mail_token WHERE user_id = @u", ("u", await UserIdAsync(email))))
                .ShouldBe(MailKeys.TokenHash(token), "only the hash is kept");

            // The email itself: the one-click stop in its headers, the page in the footer (owner, 2026-11-17).
            var message = Render("email.follow-planned", told.RootElement);
            message.Headers.ShouldNotBeNull();
            message.Headers!["List-Unsubscribe"].ShouldBe($"<{Site}/api/v1/notices/{token}/stop>");
            message.Headers["List-Unsubscribe-Post"].ShouldBe("List-Unsubscribe=One-Click");
            message.TextBody.ShouldContain($"Šį laišką gavote, nes sekate šį projektą Lietuva 2.0.\nKeisti periodiškumą: {Site}/paskyra/sekimas#laisku-daznis · Atsisakyti laiškų: {Site}/laiskai/nebegauti/{token}\n");
            message.HtmlBody.ShouldNotBeNull().ShouldContain($"<a href=\"{Site}/laiskai/nebegauti/{token}\" style=\"color:#64748b;text-decoration:underline\">atsisakyti laiškų</a>");

            (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOn);
            using var mailProgram = MailProgram();
            using (var stop = await mailProgram.PostAsync(NoticeStopRoutes.StopOf(token), OneClickBody()))
            {
                stop.StatusCode.ShouldBe(HttpStatusCode.NoContent, await stop.Content.ReadAsStringAsync());
            }

            (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOff);
            (await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.ShouldContain(b => b.Number == AppealSteps.SecondBill, "the follows stay");
            (await control.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOn, "only the token's account");
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'notices.stopped-by-link' AND target = @t AND actor_user_id IS NULL AND details IS NULL",
                ("t", (await UserIdAsync(email)).ToString()))).ShouldBe(1);

            // A new date: the control member is told, the member who stopped is not.
            await Database.ForgetEventsAsync(AppealSteps.SecondBill);
            await Database.PlanAsync(AppealSteps.SecondBill, date.AddDays(3), "adoption");
            await RunJobAsync();
            (await Database.EmailsAsync("email.follow-planned", controlEmail)).ShouldBe(2);
            (await Database.EmailsAsync("email.follow-planned", email)).ShouldBe(1);

            // Turned on again on the account page, the emails come again; the same link stops them again.
            (await member.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest())).Dispose();
            (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOn);
            (await mailProgram.PostAsync(NoticeStopRoutes.StopOf(token), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOff);
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
            await Database.ForgetEventsAsync(AppealSteps.SecondBill);
        }
    }

    [Fact]
    public async Task A_gathered_email_carries_the_same_link()
    {
        await Database.ForgetEventsAsync(AppealSteps.SecondBill);
        await Database.ForgetEventsAsync(AppealSteps.TaxBill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        await member.FollowBillAsync(AppealSteps.SecondBill);
        await member.FollowBillAsync(AppealSteps.TaxBill);
        await Database.AgeFollowsAsync(email, TimeSpan.FromHours(1));
        var date = ReasonsToComeSteps.VilniusToday().AddDays(5);
        await Database.PlanAsync(AppealSteps.SecondBill, date, "adoption");
        await Database.PlanAsync(AppealSteps.TaxBill, date, "adoption");
        try
        {
            await RunJobAsync();
            var gathered = (await Database.InboxAsync(email)).Where(e => e.Kind.StartsWith("email.follow-", StringComparison.Ordinal) || e.Kind == "email.gathered-notices")
                .ShouldHaveSingleItem("both dates in one email");
            gathered.Kind.ShouldBe("email.gathered-notices");
            var token = gathered.Payload.GetProperty("stopToken").GetString().ShouldNotBeNull();
            token.ShouldBe(Keys.MemberStopToken(await UserIdAsync(email), 0));
            var message = Render("email.gathered-notices", gathered.Payload);
            message.Headers!["List-Unsubscribe"].ShouldBe($"<{Site}/api/v1/notices/{token}/stop>");
            message.TextBody.ShouldContain($"Šį laišką gavote pagal savo laiškų pasirinkimus Lietuva 2.0.\nKeisti periodiškumą: {Site}/paskyra/sekimas#laisku-daznis · Atsisakyti laiškų: {Site}/laiskai/nebegauti/{token}\n");
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
            await Database.UnplanAsync(AppealSteps.TaxBill);
            await Database.ForgetEventsAsync(AppealSteps.SecondBill);
            await Database.ForgetEventsAsync(AppealSteps.TaxBill);
        }
    }

    [Fact]
    public async Task Opening_the_page_changes_nothing_and_its_button_stops_the_emails_without_script()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var token = await LinkAsync(email);
        using var scanner = NewBrowser();
        var path = NoticeStopRoutes.PageOf(token);
        string html;
        using (var page = await scanner.Client.GetAsync(path))
        {
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            page.Headers.GetValues("X-Robots-Tag").ShouldContain("noindex, nofollow");
            page.Headers.CacheControl!.NoStore.ShouldBeTrue();
            html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        }

        html.ShouldContain("Paspaudę mygtuką nebegausite laiškų apie tai, ką sekate.");
        html.ShouldNotContain(email, Case.Insensitive);
        html.ShouldNotContain("Rūta Mockutė");
        (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOn, "a link scanner's GET changes nothing");

        var antiforgery = AntiforgeryField().Match(html).Groups[1].Value;
        antiforgery.ShouldNotBeEmpty("the server-rendered form carries its antiforgery token");
        using var posted = await scanner.Client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "notice-stop",
            ["__RequestVerificationToken"] = antiforgery,
        }));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        var done = WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync());
        done.ShouldContain("Laiškų apie tai, ką sekate, nebesiųsime.");
        done.ShouldContain("href=\"/paskyra/sekimas#sekami\"");
        (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOff);
    }

    [Fact]
    public async Task An_unknown_a_malformed_and_a_valid_token_get_the_same_page_and_the_same_answer()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var token = await LinkAsync(email);
        var unknown = Keys.MemberStopToken(Guid.NewGuid(), 0);
        const string Malformed = "ne-tokenas";
        using var browser = NewBrowser();

        async Task<string> PageAsync(string t)
        {
            using var page = await browser.Client.GetAsync(NoticeStopRoutes.PageOf(t));
            page.StatusCode.ShouldBe(HttpStatusCode.OK, t);
            var text = await page.Content.ReadAsStringAsync();
            // Per request: the form's antiforgery token, also inside the persisted state Blazor appends.
            return PersistedState().Replace(AntiforgeryField().Replace(text.Replace(t, "TOKEN", StringComparison.Ordinal), "ANTIFORGERY"), "STATE");
        }

        var valid = await PageAsync(token);
        valid.ShouldContain("notice-stop-button");
        (await PageAsync(unknown)).ShouldBe(valid);
        (await PageAsync(Malformed)).ShouldBe(valid);

        using var mailProgram = MailProgram();
        foreach (var t in new[] { unknown, Malformed })
        {
            using var answer = await mailProgram.PostAsync(NoticeStopRoutes.StopOf(t), OneClickBody());
            answer.StatusCode.ShouldBe(HttpStatusCode.NoContent, t);
            (await answer.Content.ReadAsStringAsync()).ShouldBeEmpty();
        }

        (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOn, "neither touched the account");
        using var stopped = await mailProgram.PostAsync(NoticeStopRoutes.StopOf(token), OneClickBody());
        stopped.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await stopped.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOff, "the valid one did, with the same answer");
    }

    [Fact]
    public async Task The_terms_gate_does_not_block_it_even_from_a_session_that_has_not_accepted()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        var token = await LinkAsync(email);
        await Database.ExecuteAsync("DELETE FROM identity.consent WHERE kind = 'terms' AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email));
        using (var other = await member.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Planned = false }))
        {
            await other.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "terms.required");
        }

        using var stop = await member.SendAsync(HttpMethod.Post, NoticeStopRoutes.StopOf(token), antiforgery: false);
        stop.StatusCode.ShouldBe(HttpStatusCode.NoContent, await stop.Content.ReadAsStringAsync());
        (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(AllOff);
    }

    [Fact]
    public async Task The_link_goes_with_the_account_and_then_changes_nothing()
    {
        var (member, email) = await Factory.MemberAsync();
        var token = await LinkAsync(email);
        var userId = await UserIdAsync(email);
        using (member)
        {
            (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.member_mail_token WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        using var mailProgram = MailProgram();
        (await mailProgram.PostAsync(NoticeStopRoutes.StopOf(token), OneClickBody())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.notice_setting WHERE user_id = @u", ("u", userId))).ShouldBe(0);
    }

    [Fact]
    public async Task Logs_keep_the_links_paths_without_the_token()
    {
        var sink = new CollectingSink();
        await using var logging = new LoggingFactory(Database, sink);
        var (member, email) = await logging.MemberAsync();
        using var _m = member;
        var token = await LinkAsync(email);
        using var browser = logging.NewBrowser();
        (await browser.Client.GetAsync(NoticeStopRoutes.PageOf(token))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var mailProgram = logging.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri(Lt2Factory.Origin) });
        (await mailProgram.PostAsync(NoticeStopRoutes.StopOf(token), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var lines = sink.Lines;
        lines.ShouldContain(l => l.Contains("/laiskai/nebegauti/***", StringComparison.Ordinal), "the page's request was logged, without its token");
        lines.ShouldContain(l => l.Contains("/api/v1/notices/***/stop", StringComparison.Ordinal), "the one-click request was logged, without its token");
        lines.ShouldNotContain(l => l.Contains(token, StringComparison.Ordinal));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();

    [GeneratedRegex("<!--Blazor-WebAssembly-Component-State:[^>]*-->")]
    private static partial Regex PersistedState();
}
