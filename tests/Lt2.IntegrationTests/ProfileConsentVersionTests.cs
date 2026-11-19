using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.25.1 (owner, 2026-11-19): a public profile switched on under an earlier consent text stays on; <c>GET /me</c> says so
/// to the person alone, "Profilis" asks once, and accepting records the version in force; switching the profile off is as
/// it was; a current consent or a profile that is off has nothing to ask.
/// </summary>
public sealed class ProfileConsentVersionTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Older = "2026-01-01";

    private Task MakeOlderAsync(string email) =>
        Database.ExecuteAsync(
            "UPDATE identity.consent SET text_version = @v WHERE kind = 'public-profile' AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("v", Older), ("e", email));

    private Task<long> ConsentsAsync(string email, bool live, string? version = null) =>
        Database.ScalarAsync<long>(
            $"SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e AND c.kind = 'public-profile' AND c.withdrawn_at IS {(live ? "" : "NOT ")}NULL"
            + (version is null ? "" : " AND c.text_version = @v"),
            version is null ? [("e", email)] : [("e", email), ("v", version)])!;

    private static Task<HttpResponseMessage> AcceptAsync(TestBrowser browser, string version = null!) =>
        browser.SendAsync(HttpMethod.Put, ProfileRoutes.MineConsent, new ProfileConsentRequest { TextVersion = version ?? PublicProfileConsent.TextVersion });

    [Fact]
    public async Task A_profile_on_under_an_older_text_stays_on_says_so_to_the_person_and_accepting_records_the_version_in_force()
    {
        var (member, email, id) = await Factory.PublicMemberAsync("Sutikimo Versija");
        using var _m = member;
        using var visitor = NewBrowser();
        (await member.MeAsync()).PublicProfileConsentOutdated.ShouldBeFalse("switched on under the text in force");

        await MakeOlderAsync(email);

        (await member.MeAsync()).PublicProfileConsentOutdated.ShouldBeTrue();
        (await visitor.Client.GetAsync(ProfileRoutes.MemberOf(id), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK, "the earlier consent covered it: the profile stays on");

        using (var accepted = await AcceptAsync(member))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await accepted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        (await member.MeAsync()).PublicProfileConsentOutdated.ShouldBeFalse("the line goes");
        (await ConsentsAsync(email, live: true)).ShouldBe(1);
        (await ConsentsAsync(email, live: true, PublicProfileConsent.TextVersion)).ShouldBe(1, "the version in force");
        (await ConsentsAsync(email, live: false, Older)).ShouldBe(1, "the earlier one ended");
        (await member.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine)).ProfileId.ShouldBe(id, "the page and its address are as they were");
        (await visitor.Client.GetAsync(ProfileRoutes.MemberOf(id), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'profile.consent-accepted' AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e) AND details IS NULL",
            ("e", email))).ShouldBe(1, "audited, without details");

        using (var again = await AcceptAsync(member))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await ConsentsAsync(email, live: true)).ShouldBe(1, "accepting what is accepted changes nothing");
    }

    [Fact]
    public async Task Only_the_version_in_force_can_be_accepted_and_nothing_changes_otherwise()
    {
        var (member, email, _) = await Factory.PublicMemberAsync("Sena Versija");
        using var _m = member;
        await MakeOlderAsync(email);

        using (var wrong = await AcceptAsync(member, Older))
        {
            await wrong.ShouldBeProblemAsync(HttpStatusCode.Conflict, "profile.consent-outdated");
        }

        (await member.MeAsync()).PublicProfileConsentOutdated.ShouldBeTrue();
        (await ConsentsAsync(email, live: true, Older)).ShouldBe(1);

        using (var empty = await member.SendAsync(HttpMethod.Put, ProfileRoutes.MineConsent, new ProfileConsentRequest { TextVersion = "" }))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var visitor = NewBrowser();
        using (var anonymous = await AcceptAsync(visitor))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Switching_the_profile_off_is_one_step_as_before_and_leaves_nothing_to_ask()
    {
        var (member, email, id) = await Factory.PublicMemberAsync("Išjungia Profilį");
        using var _m = member;
        await MakeOlderAsync(email);
        (await member.MeAsync()).PublicProfileConsentOutdated.ShouldBeTrue();

        using (var off = await member.SendAsync(HttpMethod.Delete, ProfileRoutes.MinePublic))
        {
            off.StatusCode.ShouldBe(HttpStatusCode.OK, await off.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        (await member.MeAsync()).PublicProfileConsentOutdated.ShouldBeFalse();
        (await ConsentsAsync(email, live: true)).ShouldBe(0, "withdrawn with the switch");
        using var visitor = NewBrowser();
        (await visitor.Client.GetAsync(ProfileRoutes.MemberOf(id), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Gone);

        using (var accept = await AcceptAsync(member))
        {
            await accept.ShouldBeProblemAsync(HttpStatusCode.Conflict, "profile.not-public");
        }
    }

    [Fact]
    public async Task A_current_consent_and_a_profile_that_is_off_have_no_line_and_nobody_else_is_told()
    {
        var (current, _, _) = await Factory.PublicMemberAsync("Dabartinis Sutikimas");
        var (off, _) = await Factory.MemberAsync("Be Profilio");
        var (older, olderEmail, _) = await Factory.PublicMemberAsync("Ankstesnis Sutikimas");
        using var _c = current;
        using var _f = off;
        using var _o = older;
        await MakeOlderAsync(olderEmail);

        (await current.MeAsync()).PublicProfileConsentOutdated.ShouldBeFalse();
        (await off.MeAsync()).PublicProfileConsentOutdated.ShouldBeFalse();
        (await older.MeAsync()).PublicProfileConsentOutdated.ShouldBeTrue();
        (await current.MeAsync()).PublicProfileConsentOutdated.ShouldBeFalse("another member's older consent is theirs alone");
    }

    [Fact]
    public async Task The_account_page_shows_the_line_to_the_person_with_an_older_text_and_to_nobody_else()
    {
        var (older, olderEmail, _) = await Factory.PublicMemberAsync("Puslapio Sutikimas");
        var (current, _, _) = await Factory.PublicMemberAsync("Puslapio Dabartinis");
        using var _o = older;
        using var _c = current;
        await MakeOlderAsync(olderEmail);

        var mine = WebUtility.HtmlDecode(await older.Client.GetStringAsync("/paskyra", TestContext.Current.CancellationToken));
        mine.ShouldContain("id=\"profile-consent-outdated\"");
        mine.ShouldContain("Sutikote su ankstesne šio teksto versija");

        var others = WebUtility.HtmlDecode(await current.Client.GetStringAsync("/paskyra", TestContext.Current.CancellationToken));
        others.ShouldNotContain("profile-consent-outdated");

        // No email and no banner elsewhere: the front page and the other account screens say nothing of it.
        foreach (var path in new[] { "/", "/paskyra/sekimas", "/paskyra/saugumas", "/paskyra/dalyvavimas" })
        {
            WebUtility.HtmlDecode(await older.Client.GetStringAsync(path, TestContext.Current.CancellationToken)).ShouldNotContain("ankstesne šio teksto versija", Case.Sensitive, path);
        }

        (await Database.InboxAsync(olderEmail)).ShouldNotContain(m => m.Kind.Contains("consent", StringComparison.Ordinal), "no email about it");
    }
}
