using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Design 6.4: rate-limit policies per client for code requests and registration.</summary>
public sealed class RateLimitTests(DatabaseFixture database) : IAsyncDisposable
{
    private readonly Lt2Factory _factory = new(database, codeRequestLimit: 3, registrationLimit: 2);

    [Fact]
    public async Task Code_requests_beyond_the_limit_get_429_with_retry_after()
    {
        using var browser = _factory.NewBrowser();
        for (var i = 0; i < 3; i++)
        {
            using var allowed = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email() });
            allowed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using var limited = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email() });
        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");
        limited.Headers.RetryAfter.ShouldNotBeNull();
        (await limited.ProblemAsync()).GetProperty("detail").GetString().ShouldBe("Per daug bandymų. Palaukite ir bandykite vėliau.");
    }

    [Fact]
    public async Task Registrations_beyond_the_limit_get_429()
    {
        using var browser = _factory.NewBrowser();
        for (var i = 0; i < 2; i++)
        {
            using var allowed = await browser.PostAsync(
                ApiRoutes.Auth.Register,
                new RegisterRequest { Email = TestData.Email(), InvitationCode = TestData.Invitation, ConfirmsAdult = true });
            allowed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using var limited = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = TestData.Email(), InvitationCode = TestData.Invitation, ConfirmsAdult = true });
        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");
    }

    [Fact]
    public async Task The_per_account_limit_stops_sending_codes_without_saying_so()
    {
        using var browser = _factory.NewBrowser();
        var email = TestData.Email();
        await browser.RegisterAsync(email);
        await database.ExecuteAsync(
            """
            INSERT INTO identity.sign_in_code (id, user_id, code_hash, created_at, expires_at, failed_attempts, consumed_at)
            SELECT gen_random_uuid(), id, '\x00'::bytea, now(), now(), 0, now() FROM identity."user", generate_series(1, 4)
            WHERE email = @e
            """,
            ("e", email));
        var before = await _factory.OutboxCountAsync(email);

        using var response = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await _factory.OutboxCountAsync(email)).ShouldBe(before);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
