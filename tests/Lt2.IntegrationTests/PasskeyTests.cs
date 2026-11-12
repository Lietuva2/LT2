using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Passkey registration and sign-in on ASP.NET Core Identity's WebAuthn support (design 6.2).</summary>
public sealed class PasskeyTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Creation_options_need_a_signed_in_person()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions);
        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "auth.required");
    }

    [Fact]
    public async Task A_person_adds_a_passkey_and_signs_in_with_it_from_another_browser()
    {
        using var authenticator = new SoftwareAuthenticator();
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync();

        using var creation = await browser.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions);
        creation.StatusCode.ShouldBe(HttpStatusCode.OK);
        var creationJson = await creation.Content.ReadAsStringAsync();
        using (var options = JsonDocument.Parse(creationJson))
        {
            options.RootElement.GetProperty("rp").GetProperty("id").GetString().ShouldBe("lt2.test");
            options.RootElement.GetProperty("user").GetProperty("name").GetString().ShouldBe(email);
        }

        using (var add = await browser.PostAsync(ApiRoutes.Auth.Passkeys, new PasskeyCredentialRequest { Credential = authenticator.Create(creationJson) }))
        {
            add.StatusCode.ShouldBe(HttpStatusCode.NoContent, await add.Content.ReadAsStringAsync());
        }

        (await browser.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!.PasskeyCount.ShouldBe(1);

        using var other = NewBrowser();
        using var request = await other.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions);
        request.StatusCode.ShouldBe(HttpStatusCode.OK);
        var requestJson = await request.Content.ReadAsStringAsync();
        using (var signIn = await other.PostAsync(ApiRoutes.Auth.PasskeySignIn, new PasskeyCredentialRequest { Credential = authenticator.Get(requestJson) }))
        {
            signIn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await signIn.Content.ReadAsStringAsync());
            signIn.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("__Host-lt2=", StringComparison.Ordinal));
        }

        (await other.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!.Email.ShouldBe(email);
        var sessions = await other.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions);
        sessions!.Single(s => s.IsCurrent).Method.ShouldBe("pop");
    }

    [Fact]
    public async Task An_assertion_for_another_challenge_is_refused()
    {
        using var authenticator = new SoftwareAuthenticator();
        using var browser = NewBrowser();
        await browser.RegisterAndSignInAsync();
        using var creation = await browser.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions);
        using (await browser.PostAsync(ApiRoutes.Auth.Passkeys, new PasskeyCredentialRequest { Credential = authenticator.Create(await creation.Content.ReadAsStringAsync()) }))
        {
        }

        using var other = NewBrowser();
        using var request = await other.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions);
        var forged = authenticator.Get(await request.Content.ReadAsStringAsync(), challengeOverride: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        using var signIn = await other.PostAsync(ApiRoutes.Auth.PasskeySignIn, new PasskeyCredentialRequest { Credential = forged });
        await signIn.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.invalid");
        using var me = await other.Client.GetAsync(ApiRoutes.Me.Root);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_credential_without_a_pending_challenge_is_refused()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(ApiRoutes.Auth.PasskeySignIn, new PasskeyCredentialRequest { Credential = "{\"id\":\"x\"}" });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.invalid");
    }

    [Fact]
    public async Task Garbage_instead_of_an_attestation_is_refused()
    {
        using var browser = NewBrowser();
        await browser.RegisterAndSignInAsync();
        using (await browser.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions))
        {
        }

        using var response = await browser.PostAsync(ApiRoutes.Auth.Passkeys, new PasskeyCredentialRequest { Credential = "{\"id\":\"x\",\"type\":\"public-key\"}" });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.not-added");
        (await response.ProblemAsync()).GetProperty("errors").GetProperty("credential")[0].GetString().ShouldBe(
            "Prieigos rakto patikrinti nepavyko. Prie šios paskyros jis nepridėtas, net jei naršyklė ar slaptažodžių tvarkyklė jį išsaugojo; galite bandyti dar kartą.");
    }
}
