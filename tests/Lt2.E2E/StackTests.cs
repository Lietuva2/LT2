using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lt2.E2E;

/// <summary>
/// The running stack as a visitor reaches it through Caddy: security headers (design 6.4), the antiforgery cookie, and
/// Caddy's own logs, which must carry no client address and no token (AGENTS.md: "IPs are hashed").
///
/// Runs only when <c>LT2_E2E_BASE_URL</c> is set. The log check reads <c>docker compose logs caddy</c>, so it needs the
/// docker CLI and the compose file (<c>LT2_E2E_COMPOSE_FILE</c>, default <c>deploy/compose.yaml</c> in this repository).
/// </summary>
public sealed partial class StackTests
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("LT2_E2E_BASE_URL");

    [Theory]
    [InlineData("/")]
    [InlineData("/prisijungti")]
    [InlineData("/api/v1/auth/antiforgery")]
    [InlineData("/fonts/fonts.css")]
    [InlineData("/_framework/blazor.web.js")]
    public async Task Every_response_through_caddy_carries_the_security_headers(string path)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(BaseUrl), "LT2_E2E_BASE_URL is not set.");
        using var http = NewClient();
        using var response = await http.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldStartWith("default-src 'self';");
        csp.ShouldContain("script-src 'self' 'wasm-unsafe-eval';");
        csp.ShouldContain("font-src 'self';");
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldNotContain("unsafe-inline");
        response.Headers.GetValues("Strict-Transport-Security").Single().ShouldBe("max-age=63072000; includeSubDomains; preload");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task The_antiforgery_cookie_is_host_only_secure_and_strict()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(BaseUrl), "LT2_E2E_BASE_URL is not set.");
        using var http = NewClient();
        using var response = await http.GetAsync("/api/v1/auth/antiforgery");

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-lt2.af=", StringComparison.Ordinal)).ToLowerInvariant();
        cookie.ShouldContain("; secure");
        cookie.ShouldContain("; path=/");
        cookie.ShouldContain("; samesite=strict");
        cookie.ShouldContain("; httponly");
        cookie.ShouldNotContain("domain=");
    }

    [Fact]
    public async Task Plain_http_is_redirected_to_the_https_origin_with_its_port()
    {
        var httpUrl = Environment.GetEnvironmentVariable("LT2_E2E_HTTP_URL");
        Assert.SkipWhen(string.IsNullOrEmpty(BaseUrl) || string.IsNullOrEmpty(httpUrl), "LT2_E2E_BASE_URL or LT2_E2E_HTTP_URL is not set.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(httpUrl!) };
        using var response = await http.GetAsync("/prisijungti?registruotis=true");

        response.StatusCode.ShouldBe(HttpStatusCode.PermanentRedirect);
        response.Headers.Location!.ToString().ShouldBe(BaseUrl!.TrimEnd('/') + "/prisijungti?registruotis=true");
    }

    [Fact]
    public async Task Caddy_logs_no_client_address_and_no_token()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(BaseUrl), "LT2_E2E_BASE_URL is not set.");
        var since = DateTimeOffset.UtcNow.AddSeconds(-5);
        var marker = Guid.NewGuid().ToString("N");
        using var http = NewClient();

        // A state-changing call carrying everything that must not be logged: the antiforgery token in a header and in
        // a cookie, a user agent, and the visitor's address.
        using var tokenResponse = await http.GetAsync("/api/v1/auth/antiforgery");
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var cookieValue = tokenResponse.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-lt2.af=", StringComparison.Ordinal))
            .Split(';')[0]["__Host-lt2.af=".Length..];
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/code?probe={marker}")
        {
            Content = JsonContent.Create(new { email = $"ruta.mockute.{marker}@pavyzdys.test" }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Origin", BaseUrl);
        request.Headers.UserAgent.ParseAdd("lt2-log-probe/1.0");
        using (var response = await http.SendAsync(request))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var logs = "";
        for (var attempt = 0; attempt < 20 && !logs.Contains(marker, StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(500);
            logs = await CaddyLogsAsync(since);
        }

        var probeLines = logs.Split('\n').Where(l => l.Contains(marker, StringComparison.Ordinal)).ToList();
        probeLines.ShouldNotBeEmpty("Caddy's access log should have a line for the probe request");
        logs.ShouldNotContain(token);
        logs.ShouldNotContain(cookieValue);
        logs.ShouldNotContain("lt2-log-probe");
        logs.ShouldNotContain("\"remote_ip\"");
        logs.ShouldNotContain("\"client_ip\"");
        logs.ShouldNotContain("X-Xsrf-Token", Case.Insensitive);
        logs.ShouldNotContain($"ruta.mockute.{marker}");
        foreach (var line in probeLines)
        {
            IpAddress().IsMatch(line).ShouldBeFalse(line);
        }
    }

    private static HttpClient NewClient()
    {
        var handler = new HttpClientHandler
        {
            // Caddy's local certificate authority is not trusted on the test machine.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            UseCookies = true,
            AllowAutoRedirect = false,
        };
        return new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri(BaseUrl!) };
    }

    private static Task<string> CaddyLogsAsync(DateTimeOffset since) =>
        Compose.RunAsync(
            "logs", "caddy", "--no-color", "--no-log-prefix",
            "--since", since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    /// <summary>An IPv4 address, or an IPv6 address with at least three groups.</summary>
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b|\b[0-9a-f]{1,4}(?::[0-9a-f]{0,4}){2,7}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IpAddress();
}
