using System.Net;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>Security headers of design 6.4, health endpoints, the OpenAPI document and the server-rendered pages.</summary>
public sealed class HostTests(DatabaseFixture database) : ApiTest(database)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/prisijungti")]
    [InlineData("/api/v1/auth/antiforgery")]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var headers = response.Headers;
        var csp = headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'self'");
        csp.ShouldContain("script-src 'self' 'wasm-unsafe-eval';");
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldNotContain("unsafe-inline");
        headers.GetValues("Strict-Transport-Security").Single().ShouldBe("max-age=63072000; includeSubDomains; preload");
        headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        headers.GetValues("Permissions-Policy").Single().ShouldContain("camera=()");
    }

    [Fact]
    public async Task The_start_page_is_server_rendered_in_lithuanian_without_inline_script()
    {
        using var browser = NewBrowser();
        var html = await browser.Client.GetStringAsync("/");

        html.ShouldContain("<html lang=\"lt\">");
        html.ShouldContain("<h1 id=\"home-title\">Lietuva 2.0</h1>");
        System.Net.WebUtility.HtmlDecode(html).ShouldContain("Vyksta uždaras bandymas");
        html.ShouldContain("_framework/blazor.web");
        System.Text.RegularExpressions.Regex.Matches(html, "<script(?![^>]*\\bsrc=)[^>]*>").ShouldBeEmpty();
        html.ShouldNotContain(" style=\"");
    }

    [Fact]
    public async Task The_fonts_are_served_from_the_site_itself()
    {
        using var browser = NewBrowser();
        var html = await browser.Client.GetStringAsync("/");
        html.ShouldMatch("<link rel=\"stylesheet\" href=\"fonts/fonts\\.[a-z0-9]+\\.css\"");

        var css = await browser.Client.GetStringAsync("/fonts/fonts.css");
        css.ShouldNotContain("//fonts.");
        using var font = await browser.Client.GetAsync("/fonts/sofia-sans-latin.woff2");
        font.StatusCode.ShouldBe(HttpStatusCode.OK);
        font.Content.Headers.ContentType!.MediaType.ShouldBe("font/woff2");
    }

    [Fact]
    public async Task The_prerendered_sign_in_page_tells_visitors_without_javascript_why_it_does_not_work()
    {
        using var browser = NewBrowser();
        var html = System.Net.WebUtility.HtmlDecode(await browser.Client.GetStringAsync("/prisijungti"));

        html.ShouldContain("<noscript><p class=\"note\">Prisijungti ir registruotis galima tik įjungus „JavaScript“.");
        System.Text.RegularExpressions.Regex.IsMatch(html, "<input[^>]*id=\"email\"[^>]*disabled").ShouldBeTrue("the form is disabled until WebAssembly runs");
    }

    [Fact]
    public async Task The_signed_in_page_sends_strangers_to_sign_in()
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync("/sveiki");
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldStartWith("https://lt2.test/prisijungti");
    }

    [Fact]
    public async Task The_signed_in_page_greets_the_person_when_prerendered_in_process()
    {
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync();
        var html = await browser.Client.GetStringAsync("/sveiki");
        html.ShouldContain($"Sveiki, {email}!");
    }

    [Fact]
    public async Task Health_endpoints_answer()
    {
        using var browser = NewBrowser();
        (await browser.Client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.Client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_openapi_document_lists_the_auth_operations()
    {
        using var browser = NewBrowser();
        var document = await browser.Client.GetStringAsync("/openapi/v1.json");
        foreach (var path in new[] { "/api/v1/auth/register", "/api/v1/auth/code", "/api/v1/auth/sign-in", "/api/v1/auth/sign-out", "/api/v1/me", "/api/v1/auth/passkeys" })
        {
            document.ShouldContain($"\"{path}\"");
        }
    }

    [Fact]
    public async Task Unknown_api_routes_answer_with_a_problem()
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync("/api/v1/nera");
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not-found");
    }
}
