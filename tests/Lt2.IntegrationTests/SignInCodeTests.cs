using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary><c>POST /api/v1/auth/code</c> and <c>POST /api/v1/auth/sign-in</c> (design 6.2).</summary>
public sealed class SignInCodeTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Asking_for_a_code_for_an_unknown_address_looks_the_same_and_sends_nothing()
    {
        using var browser = NewBrowser();
        var email = TestData.Email("nera");
        using var response = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await Factory.OutboxCountAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task A_new_code_replaces_the_previous_one()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        var first = await browser.RegisterAsync(email);

        using (var again = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var second = await Factory.LatestCodeAsync(email);
        second.ShouldNotBeNull();
        if (first != second)
        {
            using var old = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = first });
            await old.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        }

        using var current = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = second });
        current.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_right_code_signs_in_with_a_host_prefixed_secure_cookie_and_confirms_the_address()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        var code = await browser.RegisterAsync(email);

        using var response = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-lt2=", StringComparison.Ordinal));
        cookie.ShouldContain("path=/", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldNotContain("domain=", Case.Insensitive);

        var me = await browser.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root);
        me!.Email.ShouldBe(email);
        me.VerificationLevel.ShouldBe("email");
        me.RegistrationComplete.ShouldBeFalse("the address is proven, but the registration is completed separately");
        me.Roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        var code = await browser.RegisterAsync(email);
        using (var first = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var other = NewBrowser();
        using var second = await other.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code });
        await second.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
    }

    [Fact]
    public async Task Five_wrong_attempts_use_up_the_code()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        var code = await browser.RegisterAsync(email);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            using var attempt = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = wrong });
            await attempt.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        }

        using var right = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code });
        await right.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        var code = await browser.RegisterAsync(email);
        await Database.ExecuteAsync(
            "UPDATE identity.sign_in_code SET expires_at = now() - interval '1 second' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("e", email));

        using var response = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
    }

    [Fact]
    public async Task An_unknown_address_gets_the_same_answer_as_a_wrong_code()
    {
        using var browser = NewBrowser();
        using var unknown = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = TestData.Email("nera"), Code = "123456" });
        var email = TestData.Email();
        var code = await browser.RegisterAsync(email);
        using var wrong = await browser.PostAsync(
            ApiRoutes.Auth.SignIn,
            new SignInRequest { Email = email, Code = code == "123456" ? "654321" : "123456" });

        unknown.StatusCode.ShouldBe(wrong.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var wrongBody = await wrong.Content.ReadAsStringAsync();
        Math.Abs(unknownBody.Length - wrongBody.Length).ShouldBeLessThan(60, "only the trace id may differ");
        (await unknown.ProblemAsync()).GetProperty("code").GetString().ShouldBe((await wrong.ProblemAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_malformed_code_is_a_validation_problem()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = TestData.Email(), Code = "12ab" });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        (await response.ProblemAsync()).GetProperty("errors").GetProperty("code")[0].GetString().ShouldBe("Kodą sudaro šeši skaitmenys.");
    }
}
