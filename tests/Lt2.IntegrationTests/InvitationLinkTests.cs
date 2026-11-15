using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.8, "Codes and tokens are not sent in addresses": the links the site makes carry the invitation code and the
/// representative's token after the <c>#</c>, which a browser never sends; the page reads it and the API gets it in a
/// request body. A link in the old form (<c>?kodas=…</c>) still registers. Neither secret reaches a request path, a query
/// the server reads into a page, or a log line of the host.
/// </summary>
public sealed class InvitationLinkTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task The_new_and_the_old_registration_link_both_register_and_neither_code_reaches_a_page_or_a_log_line()
    {
        var sink = new CollectingSink();
        await using var logged = new LoggingFactory(Database, sink);
        var (inviter, _) = await logged.MemberAsync("Kviečianti Narė");
        using var _i = inviter;
        var mine = await inviter.GetJsonAsync<MyInvitationResponse>(ApiRoutes.Me.Invitation);
        var code = mine.Code!;
        mine.Link.ShouldBe("/prisijungti#kodas=" + code);

        // The new link: what the browser sends is the page alone; the page reads the code from after the "#".
        using (var visitor = logged.NewBrowser())
        {
            var page = new Uri(new Uri(Lt2Factory.Origin), mine.Link);
            using var answer = await visitor.Client.GetAsync(page);
            answer.StatusCode.ShouldBe(HttpStatusCode.OK);
            answer.RequestMessage!.RequestUri!.PathAndQuery.ShouldBe("/prisijungti");
            (await answer.Content.ReadAsStringAsync()).ShouldNotContain(code, Case.Insensitive, "the page rendered on the server does not know the code");

            var fromFragment = InvitationLink.FromFragment(page.ToString(), InvitationLink.CodeParameter).ShouldNotBeNull();
            var email = TestData.Email();
            await visitor.SignInWithCodeAsync(email, await visitor.RegisterAsync(email, fromFragment));
            await visitor.CompleteRegistrationAsync("Nauja Narė");
        }

        // The old link: still opens the form, and the page rendered on the server does not repeat the code from the query.
        using (var visitor = logged.NewBrowser())
        {
            using var answer = await visitor.Client.GetAsync("/prisijungti?kodas=" + Uri.EscapeDataString(code));
            answer.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await answer.Content.ReadAsStringAsync()).ShouldNotContain(code, Case.Insensitive);
            var email = TestData.Email();
            await visitor.SignInWithCodeAsync(email, await visitor.RegisterAsync(email, code));
            await visitor.CompleteRegistrationAsync("Sena Nuoroda");
        }

        (await inviter.GetJsonAsync<MyInvitationResponse>(ApiRoutes.Me.Invitation)).Completed.ShouldBe(2);
        var lines = sink.Lines;
        lines.ShouldContain(l => l.Contains("/prisijungti", StringComparison.Ordinal), "the requests were logged");
        lines.ShouldAllBe(l => !l.Contains(code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_representatives_link_carries_the_token_after_the_hash_and_the_api_takes_it_only_in_the_body()
    {
        var sink = new CollectingSink();
        await using var logged = new LoggingFactory(Database, sink);
        var (first, _) = await logged.MemberAsync("Pirmoji Atstovė");
        using var _f = first;
        var created = await first.RegisterOrganisationAsync();
        InviteCreatedResponse invite;
        using (var made = await first.PostAsync(OrganisationRoutes.InvitesOf(created.Slug), new { }))
        {
            made.StatusCode.ShouldBe(HttpStatusCode.OK, await made.Content.ReadAsStringAsync());
            invite = (await made.Content.ReadFromJsonAsync<InviteCreatedResponse>(System.Text.Json.JsonSerializerOptions.Web))!;
        }

        invite.Path.ShouldBe("/organizacijos/kvietimas#" + Uri.EscapeDataString(invite.Token));

        var (second, _) = await logged.MemberAsync("Antroji Atstovė");
        using var _s = second;
        using (var page = await second.Client.GetAsync(new Uri(new Uri(Lt2Factory.Origin), invite.Path)))
        {
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            page.RequestMessage!.RequestUri!.PathAndQuery.ShouldBe("/organizacijos/kvietimas");
            (await page.Content.ReadAsStringAsync()).ShouldNotContain(invite.Token);
        }

        (await second.PostJsonAsync<InviteResponse>(OrganisationRoutes.Invite, new InviteTokenRequest { Token = invite.Token })).Usable.ShouldBeTrue();
        using (var accepted = await second.PostAsync(OrganisationRoutes.AcceptInvite, new InviteTokenRequest { Token = invite.Token }))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        }

        var lines = sink.Lines;
        lines.ShouldContain(l => l.Contains("/organisation-invites/accept", StringComparison.Ordinal), "the requests were logged");
        lines.ShouldAllBe(l => !l.Contains(invite.Token, StringComparison.Ordinal));

        // The old API address with the token in the path is gone.
        (await second.Client.GetAsync(ApiRoutes.Prefix + "/organisation-invites/" + invite.Token)).StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }
}
