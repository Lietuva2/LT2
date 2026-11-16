using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>The real web application over the test database, reached at <c>https://lt2.test</c>.</summary>
public class Lt2Factory(DatabaseFixture database, int codeRequestLimit = 100_000, int registrationLimit = 100_000) : WebApplicationFactory<Lt2.Web.Components.App>
{
    public const string Origin = "https://lt2.test";

    /// <summary>The client address every test request appears to come from (a documentation range, RFC 5737).</summary>
    public static readonly IPAddress ClientAddress = IPAddress.Parse("192.0.2.10");

    public DatabaseFixture Database { get; } = database;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Lt2", Database.AppConnectionString);
        builder.UseSetting("Security:IpHashKey", "test-ip-hash-key");

        // Design 8.20.2: the key of the known browsers' hashes, so a browser that signed in is known to the account.
        builder.UseSetting("Security:DeviceHashKey", "test-device-hash-key");
        builder.UseSetting("Moderation:ContactAddress", "moderatoriai@lt2.test");
        // No test calls a real AI provider, whatever the environment of the machine says (AGENTS.md); tests that need one put a fake in.
        builder.UseSetting("Ai:Provider", "");
        builder.UseSetting("Ai:ApiKey", "");
        // The per-address limits are out of reach for the factory a whole run shares from one address: a limit the run could
        // reach would make a test pass or fail by how many requests the tests before it made. RateLimitTests checks them low on
        // factories of its own.
        builder.UseSetting("RateLimits:CodeRequest:PermitLimit", codeRequestLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimits:CodeRequest:WindowSeconds", "600");
        builder.UseSetting("RateLimits:Registration:PermitLimit", registrationLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimits:Registration:WindowSeconds", "600");
        builder.UseSetting("RateLimits:SignIn:PermitLimit", "100000");
        builder.UseSetting("RateLimits:SignIn:WindowSeconds", "600");

        // Design 8.19: the key of MP mail's hashes and links, and the MP link's limits high enough for a whole run from one
        // address (RateLimitTests checks them low on a factory of its own).
        builder.UseSetting("Security:MailHashKey", "test-mail-hash-key");

        // The test database's parliament is the stand-in's (synthetic), which locks MP mail (design 8.19.1) unless the mail is
        // declared caught: nothing here reaches a mailbox. MpSendingTests checks the lock on factories of its own.
        builder.UseSetting("Email:Caught", "true");
        builder.UseSetting("RateLimits:MpMail:PermitLimit", "100000");
        builder.UseSetting("RateLimits:MpMailToken:PermitLimit", "100000");
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ClientAddressStartupFilter>());
    }

    /// <summary>A new browser: its own cookies, sending this site's Origin like a real browser does.</summary>
    public TestBrowser NewBrowser()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add("Origin", Origin);
        return new TestBrowser(client, this);
    }

    /// <summary>
    /// The in-memory test server has no network connection, so it reports no client address. A real request always has
    /// one (Kestrel, or Caddy's forwarded header), and the audit log and rate limits depend on it.
    /// </summary>
    private sealed class ClientAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= ClientAddress;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
