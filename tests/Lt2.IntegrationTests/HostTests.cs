using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
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
    public async Task A_signed_in_page_of_the_site_is_not_stored()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        foreach (var path in new[] { "/darbotvarke", "/sveiki" })
        {
            using var page = await member.Client.GetAsync(path);
            page.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            page.Content.Headers.ContentType!.MediaType.ShouldBe("text/html", path);
            page.Headers.CacheControl.ShouldNotBeNull(path).NoStore.ShouldBeTrue(path);
        }
    }

    /// <summary>
    /// Design 6.8, the rule itself on a bare pipeline: the site's pages are no-store for everyone today anyway, because every
    /// page with an island carries an antiforgery token and ASP.NET Core marks such answers <c>no-cache, no-store</c>.
    /// </summary>
    [Fact]
    public async Task The_security_headers_make_html_for_a_signed_in_session_no_store_and_leave_a_visitors_html_alone()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.Use((context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Test-Signed-In"))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));
            }

            return next(context);
        });
        Lt2.Web.Security.SecurityHeaders.UseLt2SecurityHeaders(app);
        app.MapGet("/page", () => Results.Content("<p>Puslapis</p>", "text/html; charset=utf-8"));
        app.MapGet("/file.txt", () => Results.Text("tekstas"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        async Task<bool> NoStoreAsync(string path, bool signedIn)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (signedIn)
            {
                request.Headers.Add("X-Test-Signed-In", "1");
            }

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return response.Headers.CacheControl?.NoStore ?? false;
        }

        (await NoStoreAsync("/page", signedIn: true)).ShouldBeTrue("a signed-in page");
        (await NoStoreAsync("/page", signedIn: false)).ShouldBeFalse("a visitor's page");
        (await NoStoreAsync("/file.txt", signedIn: true)).ShouldBeFalse("not a page");
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
        foreach (var path in new[]
                 {
                     "/api/v1/auth/register", "/api/v1/auth/code", "/api/v1/auth/sign-in", "/api/v1/auth/sign-out", "/api/v1/me", "/api/v1/auth/passkeys",
                     "/api/v1/mps/{slug}/claim", "/api/v1/me/mp", "/api/v1/me/mp/takeover", "/api/v1/me/mp/office", "/api/v1/me/mp/office/invitations",
                     "/api/v1/me/mp/office/{id}", "/api/v1/me/mp/office/acceptance", "/api/v1/admin/mps/{slug}/link/end",
                 })
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
