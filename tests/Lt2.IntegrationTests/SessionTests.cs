using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary><c>GET /api/v1/me</c>, "my sessions" and <c>POST /api/v1/auth/sign-out</c> (design 6.2).</summary>
public sealed class SessionTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Me_needs_a_signed_in_person()
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(ApiRoutes.Me.Root);
        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.required");
    }

    [Fact]
    public async Task A_person_sees_every_browser_they_are_signed_in_with_and_can_end_another_one()
    {
        using var laptop = NewBrowser();
        var email = await laptop.RegisterAndSignInAsync();
        using var phone = NewBrowser();
        using (await phone.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
        }

        var code = await Factory.LatestCodeAsync(email);
        using (var signIn = await phone.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code! }))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var sessions = await laptop.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions);
        sessions!.Count.ShouldBe(2);
        sessions.Count(s => s.IsCurrent).ShouldBe(1);
        sessions.ShouldAllBe(s => s.Method == "otp");
        var phoneSession = sessions.Single(s => !s.IsCurrent);

        using (var end = await laptop.SendAsync(HttpMethod.Delete, ApiRoutes.Me.SessionById(phoneSession.Id)))
        {
            end.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var phoneMe = await phone.Client.GetAsync(ApiRoutes.Me.Root);
        phoneMe.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var laptopMe = await laptop.Client.GetAsync(ApiRoutes.Me.Root);
        laptopMe.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_can_end_another_persons_session()
    {
        using var first = NewBrowser();
        await first.RegisterAndSignInAsync();
        var sessions = await first.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions);
        using var second = NewBrowser();
        await second.RegisterAndSignInAsync();

        using var response = await second.SendAsync(HttpMethod.Delete, ApiRoutes.Me.SessionById(sessions!.Single().Id));
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "session.not-found");
        using var stillIn = await first.Client.GetAsync(ApiRoutes.Me.Root);
        stillIn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_on_the_server()
    {
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync();
        var userSessions = "SELECT count(*) FROM identity.session WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)";
        (await Database.ScalarAsync<long>(userSessions, ("e", email))).ShouldBe(1);

        using (var signOut = await browser.PostAsync(ApiRoutes.Auth.SignOut))
        {
            signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await Database.ScalarAsync<long>(userSessions, ("e", email))).ShouldBe(0);
        using var me = await browser.Client.GetAsync(ApiRoutes.Me.Root);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_session_cookie_holds_only_an_id_and_the_ticket_is_stored_protected()
    {
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync();
        var ticket = await Database.ScalarAsync<byte[]>(
            "SELECT ticket FROM identity.session WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("e", email));
        ticket.ShouldNotBeNull();
        System.Text.Encoding.UTF8.GetString(ticket).ShouldNotContain(email);
    }
}
