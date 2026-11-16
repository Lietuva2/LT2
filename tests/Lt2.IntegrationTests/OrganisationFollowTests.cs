using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.14.14, stage B: following an organisation. "Sekti" on the profile, the list and a third switch on the account
/// page, one email when a statement first becomes public (none for a change, none for blocked, hidden or old statements,
/// only to those who followed before), and the privacy of 8.11.7 for the new kind: nobody sees who follows an organisation,
/// and there is no follower count for anyone, the organisation included.
/// </summary>
public sealed class OrganisationFollowTests(DatabaseFixture database) : ApiTest(database)
{
    private const string StatementEmail = "email.follow-statement";

    private async Task<FollowRunOutcome> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
    }

    private static async Task FollowAsync(TestBrowser browser, string slug)
    {
        using var followed = await browser.SendAsync(HttpMethod.Put, FollowRoutes.OrganisationOf(slug));
        followed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await followed.Content.ReadAsStringAsync());
    }

    /// <summary>An organisation with a representative, and a member who followed it an hour ago.</summary>
    private async Task<(TestBrowser Rep, OrganisationCreatedResponse Org, TestBrowser Follower, string FollowerEmail)> FollowedOrganisationAsync()
    {
        var (rep, _) = await Factory.MemberAsync("Sekamos Atstovė");
        var created = await rep.RegisterOrganisationAsync();
        var (follower, email) = await Factory.MemberAsync("Sekėja Pavyzdytė");
        await FollowAsync(follower, created.Slug);
        await Database.AgeFollowsAsync(email, TimeSpan.FromHours(1));
        return (rep, created, follower, email);
    }

    [Fact]
    public async Task A_member_follows_an_organisation_sees_it_with_its_label_in_the_follow_answer_and_on_the_account_page()
    {
        using var visitor = NewBrowser();
        var (rep, _) = await Factory.MemberAsync();
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        (await visitor.SendAsync(HttpMethod.Put, FollowRoutes.OrganisationOf(created.Slug))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (member, _) = await Factory.MemberAsync(consent: false);
        using var _m = member;
        await FollowAsync(member, created.Slug);
        await FollowAsync(member, created.Slug);
        var mine = await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine);
        var followed = mine.Organisations.Single();
        (followed.Slug, followed.Status, followed.Label.ConfirmedBy).ShouldBe((created.Slug, "active", (string?)null));

        using (var unknown = await member.SendAsync(HttpMethod.Put, FollowRoutes.OrganisationOf("nera-tokios")))
        {
            await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "organisation.not-found");
        }

        // A blocked organisation cannot be followed; one already followed stays in the list, marked, and can be unfollowed.
        var moderator = (await ModeratorAsync()).Browser;
        await moderator.ActAsync(OrganisationRoutes.Block, created.Id, "Bandymas.");
        var (late, _) = await Factory.MemberAsync();
        using var _l = late;
        using (var refused = await late.SendAsync(HttpMethod.Put, FollowRoutes.OrganisationOf(created.Slug)))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.NotFound, "organisation.not-found");
        }

        (await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Organisations.Single().Status.ShouldBe("blocked");
        (await member.SendAsync(HttpMethod.Delete, FollowRoutes.OrganisationOf(created.Slug))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Organisations.ShouldBeEmpty();
        await moderator.ActAsync(OrganisationRoutes.Unblock, created.Id, "Bandymas.");

        // The third switch is on at first and can be turned off alone.
        var notices = await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices);
        notices.ShouldBe(new NoticeSettingsResponse(true, true, true));
        (await member.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Planned = true, Voted = true, Statements = false })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(new NoticeSettingsResponse(true, true, false));

        // The profile offers "Sekti" (an island); the account page lists the organisation and has the third switch.
        var profile = WebUtility.HtmlDecode(await member.Client.GetStringAsync("/o/" + created.Slug));
        profile.ShouldContain("id=\"follow-organisation\"");
        await FollowAsync(member, created.Slug);
        var account = WebUtility.HtmlDecode(await member.Client.GetStringAsync("/sveiki/sekimas"));
        account.ShouldContain("id=\"follows-organisations\"");
        account.ShouldContain(created.Slug);
        account.ShouldContain("id=\"notice-statements\"");
    }

    [Fact]
    public async Task A_follower_gets_one_email_for_a_new_statement_with_a_full_link_and_none_for_a_change_and_nothing_of_its_text()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var (rep, org, follower, email) = await FollowedOrganisationAsync();
        using var _r = rep;
        using var _f = follower;
        const string Text = "Slaptas pozicijos tekstas, kurio laiške neturi būti.";
        var saved = await rep.PublishStatementAsync(AppealSteps.SecondBill, org.Slug, "against", Text, goal: 50);
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(1);
        var payload = (await Database.LatestEmailAsync(StatementEmail, email))!;
        using (var document = JsonDocument.Parse(payload))
        {
            var root = document.RootElement;
            root.GetProperty("billNumber").GetString().ShouldBe(AppealSteps.SecondBill);
            root.GetProperty("stance").GetString().ShouldBe("against");
            root.GetProperty("billPath").GetString().ShouldBe("/seimas/" + AppealSteps.SecondBill);
            root.GetProperty("accountPath").GetString().ShouldBe("/sveiki/sekimas#sekami");
            root.GetProperty("confirmedBy").ValueKind.ShouldBe(JsonValueKind.Null);

            // Exactly these fields: no text, no goal, no count, no side, nobody else. (A substring check failed by chance:
            // the address and the organisation's name carry random hex digits, which can hold "50".)
            root.EnumerateObject().Select(p => p.Name).ShouldBe(
                ["to", "organisationName", "confirmedBy", "confirmedDomain", "billNumber", "billTitle", "stance", "billPath", "accountPath"], ignoreOrder: true);
            root.GetProperty("to").GetString().ShouldBe(email);
            root.GetProperty("organisationName").GetString().ShouldBe((await rep.ProfileAsync(org.Slug)).Name);
        }

        payload.ShouldNotContain(Text);

        // Recorded once, under its bill, with no person in it.
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(1);
        (await Database.ScalarAsync<string>("SELECT row_to_json(e)::text FROM notify.bill_event e WHERE kind = 'statement' AND key = @k", ("k", saved.Id.ToString())))!
            .ShouldNotContain((await Database.UserIdAsync(email)).ToString());

        // A change sends nothing.
        await rep.PublishStatementAsync(AppealSteps.SecondBill, org.Slug, "remarks", "Pakeista pozicija.");
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(1);

        // Withdrawn and published anew: a new statement, a new email.
        (await rep.SendAsync(HttpMethod.Delete, OrganisationRoutes.StatementOf(AppealSteps.SecondBill, org.Slug))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await rep.PublishStatementAsync(AppealSteps.SecondBill, org.Slug, "for", "Nauja pozicija.");
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(2);
    }

    [Fact]
    public async Task Nobody_is_told_who_followed_later_switched_the_emails_off_or_about_an_old_statement()
    {
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        var (rep, org, follower, email) = await FollowedOrganisationAsync();
        using var _r = rep;
        using var _f = follower;
        var (quiet, quietEmail) = await Factory.MemberAsync();
        using var _q = quiet;
        await FollowAsync(quiet, org.Slug);
        await Database.AgeFollowsAsync(quietEmail, TimeSpan.FromHours(1));
        (await quiet.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Planned = true, Voted = true, Statements = false })).Dispose();

        var saved = await rep.PublishStatementAsync(AppealSteps.ThirdBill, org.Slug, "for", "Pozicija vėlyviems sekėjams.");
        var (late, lateEmail) = await Factory.MemberAsync();
        using var _l = late;
        await FollowAsync(late, org.Slug);
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(1);
        (await Database.EmailsAsync(StatementEmail, lateEmail)).ShouldBe(0, "followed after it became public");
        (await Database.EmailsAsync(StatementEmail, quietEmail)).ShouldBe(0, "the switch is off");

        // A statement public for more than three days when the job first sees it is recorded and never sent.
        await Database.ExecuteAsync("DELETE FROM notify.bill_event WHERE kind = 'statement' AND key = @k", ("k", saved.Id.ToString()));
        await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET first_public_at = now() - interval '4 days' WHERE id = @s", ("s", saved.Id));
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(1);
        (await Database.ScalarAsync<string>("SELECT outcome FROM notify.bill_event WHERE kind = 'statement' AND key = @k", ("k", saved.Id.ToString()))).ShouldBe("skipped");
    }

    [Fact]
    public async Task A_statement_that_waited_is_emailed_when_published_and_none_for_a_blocked_or_hidden_one()
    {
        var moderator = (await ModeratorAsync()).Browser;
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        var (rep, org, follower, email) = await FollowedOrganisationAsync();
        using var _r = rep;
        using var _f = follower;

        // Flagged by pre-screening: it waits, and nobody is told.
        var waiting = await rep.PublishStatementAsync(AppealSteps.TaxBill, org.Slug, "for", "Daugiau informacijos: rasykite@pavyzdys.test.");
        waiting.Status.ShouldBe("pending");
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(0);

        // Hidden before anyone was told: nobody is told.
        await moderator.ActAsync(OrganisationRoutes.HideStatement, waiting.Id, "Įžeidus.");
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(0);

        // A moderator publishes a waiting one: told now.
        await Database.FreeBillAsync(AppealSteps.TaxBill);
        var second = await rep.PublishStatementAsync(AppealSteps.TaxBill, org.Slug, "for", "Daugiau informacijos: rasykite@pavyzdys.test.");
        second.Status.ShouldBe("pending");
        await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET published_at = now() - interval '5 days' WHERE id = @s", ("s", second.Id));
        await moderator.ActAsync(OrganisationRoutes.PublishStatement, second.Id, null);
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(1, "it became public now, though written five days ago");

        // A blocked organisation's statement is never told.
        var (otherRep, otherOrg, otherFollower, otherEmail) = await FollowedOrganisationAsync();
        using var _or = otherRep;
        using var _of = otherFollower;
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await otherRep.PublishStatementAsync(AppealSteps.SecondBill, otherOrg.Slug, "remarks", "Pastabos prieš blokavimą.");
        await moderator.ActAsync(OrganisationRoutes.Block, otherOrg.Id, "Bandymas.");
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, otherEmail)).ShouldBe(0);
    }

    [Fact]
    public async Task A_statement_event_left_waiting_on_a_followed_bill_is_still_sent_by_the_statements_part()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.ForgetEventsAsync(AppealSteps.SecondBill);
        var (rep, org, follower, email) = await FollowedOrganisationAsync();
        using var _r = rep;
        using var _f = follower;

        // The follower also follows the bill, so the bills' part of the job reads the bill's events too.
        await follower.FollowBillAsync(AppealSteps.SecondBill);
        await Database.AgeFollowsAsync(email, TimeSpan.FromHours(1));
        var saved = await rep.PublishStatementAsync(AppealSteps.SecondBill, org.Slug, "for", "Pozicija, kurios įvykis laukia.");
        await Database.ExecuteAsync(
            "INSERT INTO notify.bill_event (bill_number, kind, key, first_seen, outcome) VALUES (@b, 'statement', @k, now(), 'waiting')",
            ("b", AppealSteps.SecondBill), ("k", saved.Id.ToString()));
        await RunJobAsync();
        (await Database.EmailsAsync(StatementEmail, email)).ShouldBe(1);
        (await Database.ScalarAsync<string>("SELECT outcome FROM notify.bill_event WHERE kind = 'statement' AND key = @k", ("k", saved.Id.ToString()))).ShouldBe("sent");
    }

    [Fact]
    public async Task Nobody_sees_who_follows_an_organisation_only_how_many_follow_it()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (rep, repEmail) = await Factory.MemberAsync("Organizacijos Atstovė");
        using var _r = rep;
        var created = await rep.RegisterOrganisationAsync();
        using var visitor = NewBrowser();
        var routes = new[] { OrganisationRoutes.Of(created.Slug), OrganisationRoutes.List(null), OrganisationRoutes.ManagementOf(created.Slug), OrganisationRoutes.InvitationOf(created.Slug) };
        var before = new Dictionary<string, string>();
        foreach (var route in routes.Take(2))
        {
            before[route] = await visitor.Client.GetStringAsync(route);
        }

        var managementBefore = await rep.Client.GetStringAsync(OrganisationRoutes.ManagementOf(created.Slug));
        var invitationBefore = await rep.Client.GetStringAsync(OrganisationRoutes.InvitationOf(created.Slug));

        var (follower, followerEmail) = await Factory.MemberAsync("Slapta Sekėja");
        using var _f = follower;
        await FollowAsync(follower, created.Slug);
        var followerId = (await Database.UserIdAsync(followerEmail)).ToString();

        // Public answers and the organisation's own answers are the same with and without the follower, but the profile's one
        // number of how many follow it (design 8.11.2, owner 2026-11-16).
        foreach (var route in routes.Take(2))
        {
            var after = await visitor.Client.GetStringAsync(route);
            after.Replace("\"followers\":1", "\"followers\":0", StringComparison.Ordinal).ShouldBe(before[route], route);
        }

        (await visitor.GetJsonAsync<OrganisationProfileResponse>(OrganisationRoutes.Of(created.Slug))).Followers.ShouldBe(1);

        (await rep.Client.GetStringAsync(OrganisationRoutes.ManagementOf(created.Slug))).ShouldBe(managementBefore);
        (await rep.Client.GetStringAsync(OrganisationRoutes.InvitationOf(created.Slug))).ShouldBe(invitationBefore);

        // The pages others see, the representative's included, say nothing of the follower.
        foreach (var browser in new[] { visitor, rep })
        {
            var page = WebUtility.HtmlDecode(await browser.Client.GetStringAsync("/o/" + created.Slug));
            page.ShouldNotContain("Slapta Sekėja");
            page.ShouldNotContain(followerId, Case.Insensitive);
            page.ShouldNotContain("Nebesekti");
            page.ShouldContain("Sekėjai: 1", Case.Sensitive, "how many follow is shown, and nothing more");
        }

        // Another member's follow answer is empty; the representative's too.
        (await rep.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Organisations.ShouldBeEmpty();

        // No staff screen names the follower.
        foreach (var route in new[] { OrganisationRoutes.ModerationQueue, OrganisationRoutes.ModerationHidden, AppealRoutes.ModerationQueue })
        {
            var json = await moderator.Client.GetStringAsync(route);
            json.ShouldNotContain(followerEmail, Case.Insensitive, route);
            json.ShouldNotContain(followerId, Case.Insensitive, route);
        }

        // The follow is the person's own: in their export, gone with their account.
        var export = await follower.Client.GetStringAsync(ApiRoutes.Me.Export);
        export.ShouldContain(created.Id.ToString());
        (await follower.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.follow WHERE kind = 'organisation' AND key = @k", ("k", created.Id.ToString()))).ShouldBe(0);
        _ = repEmail;
    }
}
