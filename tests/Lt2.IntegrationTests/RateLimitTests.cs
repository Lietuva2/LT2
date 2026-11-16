using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Design 6.4: rate-limit policies per client for code requests and registration.</summary>
public sealed class RateLimitTests(DatabaseFixture database) : IAsyncDisposable
{
    /// <summary>Design 6.8: a forwarded address counts only from the proxy named in configuration.</summary>
    [Theory]
    [InlineData("192.0.2.10", true)]
    [InlineData("192.0.2.99", false)]
    [InlineData("", false)]
    public async Task A_forwarded_client_address_is_believed_only_from_the_configured_proxy(string proxy, bool believed)
    {
        await using var factory = new ProxyFactory(database, proxy);
        using var browser = factory.NewBrowser();
        for (var i = 0; i < 3; i++)
        {
            using var allowed = await SendCodeRequestAsync(browser, "203.0.113.1");
            allowed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        // Another forwarded address: a new visitor if the header is believed, the same limited one if it is not.
        using var next = await SendCodeRequestAsync(browser, "203.0.113.2");
        next.StatusCode.ShouldBe(believed ? HttpStatusCode.Accepted : HttpStatusCode.TooManyRequests);
    }

    private static async Task<HttpResponseMessage> SendCodeRequestAsync(TestBrowser browser, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.Auth.Code)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new RequestCodeRequest { Email = TestData.Email() }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add(ApiHeaders.Antiforgery, await browser.AntiforgeryTokenAsync());
        return await browser.Client.SendAsync(request);
    }

    /// <summary>The test client comes from 192.0.2.10 (<see cref="Lt2Factory.ClientAddress"/>); the proxy is whatever is configured.</summary>
    private sealed class ProxyFactory(DatabaseFixture database, string proxy) : Lt2Factory(database, codeRequestLimit: 3)
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ReverseProxy:Address", proxy);
        }
    }
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
                new RegisterRequest { Email = TestData.Email(), InvitationCode = TestData.Invitation });
            allowed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using var limited = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = TestData.Email(), InvitationCode = TestData.Invitation });
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

    /// <summary>
    /// Design 8.11.2: taking, changing or withdrawing a side, following and unfollowing, and "Man svarbu" share one limit of
    /// 60 requests a minute per account, a guard against a runaway script.
    /// </summary>
    [Fact]
    public async Task The_61st_side_follow_or_mark_of_one_account_in_a_minute_is_refused_and_another_account_is_not()
    {
        var (member, _) = await _factory.MemberAsync();
        using var _m = member;
        var (other, _) = await _factory.MemberAsync();
        using var _o = other;

        // 60 follows and unfollows in turn, all allowed.
        for (var i = 0; i < 30; i++)
        {
            using var follow = await member.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.BillOf(AppealSteps.TaxBill));
            follow.StatusCode.ShouldBe(HttpStatusCode.NoContent, $"follow {i + 1}");
            using var unfollow = await member.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.Follows.FollowRoutes.BillOf(AppealSteps.TaxBill));
            unfollow.StatusCode.ShouldBe(HttpStatusCode.NoContent, $"unfollow {i + 1}");
        }

        // The 61st, a side this time: the same limit.
        using var limited = await member.TakeSideAsync(AppealSteps.TaxBill, "for");
        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");
        limited.Headers.RetryAfter.ShouldNotBeNull();
        using var mark = await member.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.MpOf(ReasonsToComeSteps.Petraitis));
        await mark.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");

        // Another account is counted on its own.
        using var theirs = await other.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.BillOf(AppealSteps.TaxBill));
        theirs.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var _u = await other.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.Follows.FollowRoutes.BillOf(AppealSteps.TaxBill));
    }

    /// <summary>
    /// Design 8.19.7: an MP's link is limited per token and per client address, like the sign-in routes, and an unknown token
    /// answers like a known one until the limit.
    /// </summary>
    [Fact]
    public async Task An_mps_link_is_limited_per_token_and_per_client_address()
    {
        await using var factory = new MpMailLimitFactory(database);
        using var browser = factory.NewBrowser();
        var token = new string('A', 43);
        for (var i = 0; i < 2; i++)
        {
            using var answer = await browser.Client.GetAsync(Lt2.Api.Contracts.Digests.MpMailRoutes.TokenOf(token));
            answer.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var limited = await browser.Client.GetAsync(Lt2.Api.Contracts.Digests.MpMailRoutes.PreferencesPageOf(token)))
        {
            limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests, "the page shares its token's limit");
        }

        // Another token from the same address: within the address's limit of three (a request refused for its token is not counted), then refused.
        using (var other = await browser.Client.GetAsync(Lt2.Api.Contracts.Digests.MpMailRoutes.TokenOf(new string('B', 43))))
        {
            other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var byAddress = await browser.Client.GetAsync(Lt2.Api.Contracts.Digests.MpMailRoutes.TokenOf(new string('C', 43)));
        await byAddress.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "rate-limited");
    }

    private sealed class MpMailLimitFactory(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimits:MpMail:PermitLimit", "3");
            builder.UseSetting("RateLimits:MpMailToken:PermitLimit", "2");
        }
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
