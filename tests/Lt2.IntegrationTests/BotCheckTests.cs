using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Identity;
using Lt2.Identity.Domain;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// The proof of work of registration and code requests (design 6.2, 6.4): <c>GET /api/v1/bot-check/challenge</c>, and the refusal
/// of a registration or a code request without a good solution, with one answer that says nothing about the address.
/// </summary>
public sealed class BotCheckTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("test-bot-check-key");

    private static string Raw(string email, string? invitation = null) =>
        JsonSerializer.Serialize(new { email, invitationCode = invitation }, JsonSerializerOptions.Web);

    [Fact]
    public async Task A_challenge_is_in_the_altcha_format_for_anyone_and_is_never_stored()
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(ApiRoutes.BotCheck.Challenge);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var challenge = (await response.Content.ReadFromJsonAsync<BotChallengeResponse>())!;
        challenge.Algorithm.ShouldBe("SHA-256");
        challenge.Challenge.Length.ShouldBe(64);
        challenge.Signature.Length.ShouldBe(64);
        challenge.MaxNumber.ShouldBe(20_000, "the setting's default");
        challenge.Salt.ShouldMatch(@"^[0-9a-f]{24}\?expires=\d+$");
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldContain("\"maxnumber\"");
        BotCheckSteps.Find(challenge).ShouldBeInRange(0, challenge.MaxNumber);

        // Another challenge is another salt, and so another number to find.
        var next = (await browser.Client.GetFromJsonAsync<BotChallengeResponse>(ApiRoutes.BotCheck.Challenge))!;
        next.Salt.ShouldNotBe(challenge.Salt);
        next.Signature.ShouldNotBe(challenge.Signature);
    }

    [Fact]
    public async Task A_registration_without_a_solution_is_refused_and_creates_nothing()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        using var response = await browser.PostAsync(ApiRoutes.Auth.Register, Raw(email, TestData.Invitation));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "bot-check.failed");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.profile WHERE normalized_email = @e", ("e", email.ToLowerInvariant()))).ShouldBe(0);
        (await Factory.OutboxCountAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task A_code_request_without_a_solution_is_refused_and_sends_nothing()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        await browser.RegisterAsync(email);
        var before = await Factory.OutboxCountAsync(email);

        using var response = await browser.PostAsync(ApiRoutes.Auth.Code, Raw(email));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "bot-check.failed");
        (await Factory.OutboxCountAsync(email)).ShouldBe(before);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    [InlineData("e30=")] // {}
    [InlineData("eyJhbGdvcml0aG0iOiJTSEEtMjU2In0=")] // only the algorithm
    public async Task A_missing_or_malformed_solution_is_refused(string botCheck)
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email(), BotCheck = botCheck });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "bot-check.failed");
    }

    [Fact]
    public async Task A_solution_longer_than_a_real_one_is_refused_before_it_is_read()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email(), BotCheck = new string('A', 1025) });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_wrong_number_a_foreign_signature_and_a_changed_challenge_are_refused()
    {
        using var browser = NewBrowser();
        var challenge = (await browser.Client.GetFromJsonAsync<BotChallengeResponse>(ApiRoutes.BotCheck.Challenge))!;
        var number = BotCheckSteps.Find(challenge);

        var wrongNumber = BotCheckSteps.Payload(challenge, number == 0 ? 1 : number - 1);
        var foreignSignature = BotCheckSteps.Payload(challenge with { Signature = new string('0', 64) }, number);
        // A challenge made up by the visitor, solved honestly but signed with another key.
        var own = BotCheckRules.NewChallenge(Encoding.UTF8.GetBytes("some other key"), 1000, DateTimeOffset.UtcNow.AddMinutes(5));
        var forged = BotCheckSteps.Solve(new BotChallengeResponse { Challenge = own.Challenge, MaxNumber = own.MaxNumber, Salt = own.Salt, Signature = own.Signature });

        foreach (var solution in new[] { wrongNumber, foreignSignature, forged })
        {
            using var response = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email(), BotCheck = solution });
            await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "bot-check.failed");
        }

        // The honest solution of the same challenge still works: the refused attempts used nothing up.
        using var accepted = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email(), BotCheck = BotCheckSteps.Payload(challenge, number) });
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task An_expired_challenge_signed_by_the_server_is_refused()
    {
        using var browser = NewBrowser();
        var expired = BotCheckRules.NewChallenge(Key, 1000, DateTimeOffset.UtcNow.AddMinutes(-1));
        var solution = BotCheckSteps.Solve(new BotChallengeResponse { Challenge = expired.Challenge, MaxNumber = expired.MaxNumber, Salt = expired.Salt, Signature = expired.Signature });

        using var response = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email(), BotCheck = solution });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "bot-check.failed");
    }

    [Fact]
    public async Task A_solution_is_used_once_by_a_registration_or_a_code_request()
    {
        using var browser = NewBrowser();
        var solution = await browser.SolveAsync();
        var email = TestData.Email();

        using (var first = await browser.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = email, InvitationCode = TestData.Invitation, BotCheck = solution }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync());
        }

        using (var again = await browser.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = TestData.Email(), InvitationCode = TestData.Invitation, BotCheck = solution }))
        {
            await again.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "bot-check.failed");
        }

        using var asCode = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email, BotCheck = solution });
        await asCode.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "bot-check.failed");

        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.bot_check_use WHERE signature = @s", ("s", SignatureOf(solution)))).ShouldBe(1);
    }

    [Fact]
    public async Task One_solution_cannot_be_used_twice_at_the_same_time()
    {
        using var browser = NewBrowser();
        var solution = await browser.SolveAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email(), BotCheck = solution });
            return response.StatusCode;
        }));

        answers.Count(a => a == HttpStatusCode.Accepted).ShouldBe(1);
        answers.Count(a => a == HttpStatusCode.BadRequest).ShouldBe(5);
    }

    [Fact]
    public async Task The_refusal_is_checked_before_the_address_and_is_the_same_for_every_address()
    {
        using var browser = NewBrowser();
        var registered = TestData.Email();
        await browser.RegisterAsync(registered);

        // A registered address, an unknown one, and a registration whose invitation code is wrong: one answer, never the
        // invitation's or the address's.
        var answers = new List<string>();
        foreach (var (route, body) in new (string, string)[]
                 {
                     (ApiRoutes.Auth.Code, Raw(registered)),
                     (ApiRoutes.Auth.Code, Raw(TestData.Email("nera"))),
                     (ApiRoutes.Auth.Register, Raw(registered, TestData.Invitation)),
                     (ApiRoutes.Auth.Register, Raw(TestData.Email(), "NERA-TOKIO")),
                     (ApiRoutes.Auth.Register, Raw(TestData.Email())),
                 })
        {
            using var response = await browser.PostAsync(route, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await response.ProblemAsync();
            problem.GetProperty("code").GetString().ShouldBe("bot-check.failed");
            answers.Add(problem.GetProperty("detail").GetString()!);
        }

        answers.Distinct().Count().ShouldBe(1);
        answers[0].ShouldBe("Nepavyko patikrinti, ar jūs ne robotas. Bandykite dar kartą.");
    }

    [Fact]
    public async Task A_good_solution_registers_and_asks_for_a_code_as_before()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        using (var registered = await browser.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = email, InvitationCode = TestData.Invitation, BotCheck = await browser.SolveAsync() }))
        {
            registered.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var first = await Factory.LatestCodeAsync(email);
        first.ShouldNotBeNull();
        using (var asked = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email, BotCheck = await browser.SolveAsync() }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await Factory.LatestCodeAsync(email)).ShouldNotBe(first, "the second request sent a new code");
    }

    [Fact]
    public async Task Signing_in_with_a_code_and_a_passkey_need_no_proof_of_work()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        var code = await browser.RegisterAsync(email);

        // The sign-in itself carries no solution (the shared step posts none).
        using var signedIn = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code });
        signedIn.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var anonymous = NewBrowser();
        using var options = await anonymous.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions);
        options.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_member_changing_the_email_address_needs_no_proof_of_work()
    {
        // Design 6.2: the change runs inside a signed-in account that passed the check at registration, under its own limits.
        using var browser = NewBrowser();
        await browser.RegisterAndSignInAsync();
        using var proof = await browser.PostAsync(ApiRoutes.Me.EmailProofCode);
        proof.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task While_the_check_is_off_no_challenge_is_given_and_nothing_is_asked_for()
    {
        var administrator = (await AdministratorAsync()).Browser;
        await using var _ = await administrator.WithSettingAsync("BotCheck:Enabled", false);
        using var browser = NewBrowser();

        using var challenge = await browser.Client.GetAsync(ApiRoutes.BotCheck.Challenge);
        challenge.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var email = TestData.Email();
        using var registered = await browser.PostAsync(ApiRoutes.Auth.Register, Raw(email, TestData.Invitation));
        registered.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await Factory.LatestCodeAsync(email)).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_difficulty_and_the_lifetime_are_settings_an_administrator_changes()
    {
        var administrator = (await AdministratorAsync()).Browser;
        await using var difficulty = await administrator.WithSettingAsync("BotCheck:MaxNumber", 1_500);
        await using var lifetime = await administrator.WithSettingAsync("BotCheck:ChallengeMinutes", 1);
        using var browser = NewBrowser();

        var challenge = (await browser.Client.GetFromJsonAsync<BotChallengeResponse>(ApiRoutes.BotCheck.Challenge))!;

        challenge.MaxNumber.ShouldBe(1_500);
        var expires = BotCheckRules.ExpiryOf(challenge.Salt)!.Value;
        expires.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(30));
        expires.ShouldBeLessThan(DateTimeOffset.UtcNow.AddSeconds(75));
    }

    [Fact]
    public async Task Housekeeping_deletes_used_challenges_only_after_they_expire()
    {
        var past = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var future = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await Database.ExecuteAsync("INSERT INTO identity.bot_check_use (signature, expires_at) VALUES (@a, now() - interval '1 minute'), (@b, now() + interval '5 minutes')", ("a", past), ("b", future));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIdentityMaintenance>().PurgeExpiredAsync(CancellationToken.None);
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.bot_check_use WHERE signature = @s", ("s", past))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.bot_check_use WHERE signature = @s", ("s", future))).ShouldBe(1);
    }

    [Fact]
    public async Task Challenges_are_limited_per_client_address()
    {
        await using var factory = new LimitedFactory(Database);
        using var browser = factory.NewBrowser();
        for (var i = 0; i < 2; i++)
        {
            using var allowed = await browser.Client.GetAsync(ApiRoutes.BotCheck.Challenge);
            allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var limited = await browser.Client.GetAsync(ApiRoutes.BotCheck.Challenge);
        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");
    }

    private static string SignatureOf(string solution) =>
        JsonDocument.Parse(Convert.FromBase64String(solution)).RootElement.GetProperty("signature").GetString()!;

    private sealed class LimitedFactory(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimits:BotChallenge:PermitLimit", "2");
        }
    }
}
