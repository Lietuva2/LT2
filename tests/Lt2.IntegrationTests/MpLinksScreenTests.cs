using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Mps;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.5.2, the administrators' end of an MP's link: the list the screen reads (MPs whose profile is taken over, never the
/// accounts) and the screen's page, "Paskyros", which only an administrator in a staff-passkey session gets.
/// </summary>
public sealed class MpLinksScreenTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Page = "/administravimas/paskyros";

    [Fact]
    public async Task The_list_names_the_mps_whose_profile_is_taken_over_for_administrators_only_and_never_the_account()
    {
        var administrator = await AdministratorAsync();
        var moderator = await ModeratorAsync();
        using var mp = await Factory.MpAccountAsync(MpSteps.Liepaitis);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        try
        {
            using (var listed = await administrator.Browser.Client.GetAsync(MpRoutes.AdminLinks, TestContext.Current.CancellationToken))
            {
                listed.StatusCode.ShouldBe(HttpStatusCode.OK);
                var body = await listed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                using var json = JsonDocument.Parse(body);
                var row = json.RootElement.EnumerateArray().Single(r => r.GetProperty("slug").GetString() == mp.Slug);
                row.EnumerateObject().Select(p => p.Name).ShouldBe(["slug", "firstName", "lastName", "since"], ignoreOrder: true);
                row.GetProperty("firstName").GetString().ShouldBe("Darius");
                body.ShouldNotContain(mp.Email, Case.Insensitive, "never the account's address");
            }

            using (var byModerator = await moderator.Browser.Client.GetAsync(MpRoutes.AdminLinks, TestContext.Current.CancellationToken))
            {
                await byModerator.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
            }

            using (var byMember = await member.Client.GetAsync(MpRoutes.AdminLinks, TestContext.Current.CancellationToken))
            {
                byMember.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            }

            using var visitor = NewBrowser();
            using (var anonymous = await visitor.Client.GetAsync(MpRoutes.AdminLinks, TestContext.Current.CancellationToken))
            {
                anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            // Ended by an administrator, the MP is no longer listed.
            await administrator.ConfirmFreshAsync();
            using (var ended = await administrator.Browser.PostAsync(MpRoutes.EndLinkOf(mp.Slug), new StatementRequest { Statement = "Susieta per klaidą." }))
            {
                ended.StatusCode.ShouldBe(HttpStatusCode.NoContent, await ended.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            (await administrator.Browser.Client.GetStringAsync(MpRoutes.AdminLinks, TestContext.Current.CancellationToken)).ShouldNotContain(mp.Slug);
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task The_page_shows_the_linked_mps_and_the_end_button_to_an_administrator_and_nothing_to_anyone_else()
    {
        var administrator = await AdministratorAsync();
        var moderator = await ModeratorAsync();
        using var mp = await Factory.MpAccountAsync(MpSteps.Liepaitis);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        try
        {
            var seen = WebUtility.HtmlDecode(await administrator.Browser.Client.GetStringAsync(Page, TestContext.Current.CancellationToken));
            seen.ShouldContain("Seimo narių profiliai");
            seen.ShouldContain("Darius Liepaitis");
            seen.ShouldContain($"id=\"acc-mp-end-{mp.Slug}\"");
            seen.ShouldContain("id=\"account-lock\"");
            seen.ShouldNotContain("id=\"accounts-refused\"");
            seen.ShouldNotContain(mp.Email, Case.Insensitive);

            foreach (var (who, client) in new[] { ("a moderator", moderator.Browser), ("a member", member) })
            {
                using var response = await client.Client.GetAsync(Page, TestContext.Current.CancellationToken);
                response.StatusCode.ShouldBe(HttpStatusCode.OK, who);
                response.Headers.CacheControl?.NoStore.ShouldBe(true, "a page for a signed-in session is no-store");
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                html.ShouldContain("id=\"accounts-refused\"", Case.Sensitive, who);
                html.ShouldNotContain("Darius Liepaitis", Case.Sensitive, who);
                html.ShouldNotContain("id=\"account-lock\"", Case.Sensitive, who);
                html.ShouldNotContain("acc-mp-end-", Case.Sensitive, who);
            }

            using var visitor = NewBrowser();
            using var stranger = await visitor.Client.GetAsync(Page, TestContext.Current.CancellationToken);
            stranger.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            stranger.Headers.Location!.ToString().ShouldStartWith("https://lt2.test/prisijungti");
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }
}
