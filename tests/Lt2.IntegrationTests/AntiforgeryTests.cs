using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Design 6.4: every state-changing API call needs the antiforgery token header and passes an Origin check.</summary>
public sealed class AntiforgeryTests(DatabaseFixture database) : ApiTest(database)
{
    private static RequestCodeRequest Body => new() { Email = TestData.Email() };

    [Fact]
    public async Task The_token_endpoint_sets_a_host_prefixed_cookie_and_returns_the_request_token()
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(ApiRoutes.Auth.Antiforgery);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>())!.Token.ShouldNotBeNullOrWhiteSpace();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-lt2.af=", StringComparison.Ordinal));
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task A_post_without_the_token_header_is_refused()
    {
        using var browser = NewBrowser();
        await browser.AntiforgeryTokenAsync();
        using var response = await browser.PostAsync(ApiRoutes.Auth.Code, Body, antiforgery: false);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "antiforgery.invalid");
    }

    [Fact]
    public async Task A_token_from_another_browser_is_refused()
    {
        using var attacker = NewBrowser();
        var foreignToken = await attacker.AntiforgeryTokenAsync();
        using var victim = NewBrowser();
        await victim.AntiforgeryTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.Auth.Code) { Content = JsonContent.Create(Body) };
        request.Headers.Add(ApiHeaders.Antiforgery, foreignToken);
        using var response = await victim.Client.SendAsync(request);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "antiforgery.invalid");
    }

    [Fact]
    public async Task A_post_from_another_origin_is_refused_even_with_a_valid_token()
    {
        using var browser = NewBrowser();
        var token = await browser.AntiforgeryTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.Auth.Code) { Content = JsonContent.Create(Body) };
        request.Headers.Add(ApiHeaders.Antiforgery, token);
        request.Headers.Remove("Origin");
        request.Headers.Add("Origin", "https://kitas.test");
        browser.Client.DefaultRequestHeaders.Remove("Origin");
        using var response = await browser.Client.SendAsync(request);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "origin.invalid");
    }

    [Fact]
    public async Task A_post_without_an_origin_is_refused()
    {
        using var browser = NewBrowser();
        var token = await browser.AntiforgeryTokenAsync();
        browser.Client.DefaultRequestHeaders.Remove("Origin");
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.Auth.Code) { Content = JsonContent.Create(Body) };
        request.Headers.Add(ApiHeaders.Antiforgery, token);
        using var response = await browser.Client.SendAsync(request);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "origin.invalid");
    }

    [Fact]
    public async Task A_token_issued_before_sign_in_no_longer_works_after_it()
    {
        using var browser = NewBrowser();
        var anonymousToken = await browser.AntiforgeryTokenAsync();
        await browser.RegisterAndSignInAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.Auth.SignOut);
        request.Headers.Add(ApiHeaders.Antiforgery, anonymousToken);
        using var response = await browser.Client.SendAsync(request);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "antiforgery.invalid");
    }

    [Fact]
    public async Task Cross_origin_requests_get_no_cors_permission()
    {
        using var browser = NewBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Options, ApiRoutes.Auth.Code);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Remove("Origin");
        browser.Client.DefaultRequestHeaders.Remove("Origin");
        request.Headers.Add("Origin", "https://kitas.test");
        using var response = await browser.Client.SendAsync(request);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
