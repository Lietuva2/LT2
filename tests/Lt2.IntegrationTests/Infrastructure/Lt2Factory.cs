using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>The real web application over the test database, reached at <c>https://lt2.test</c>.</summary>
public class Lt2Factory(DatabaseFixture database, int codeRequestLimit = 1000, int registrationLimit = 1000) : WebApplicationFactory<Lt2.Web.Components.App>
{
    public const string Origin = "https://lt2.test";

    public DatabaseFixture Database { get; } = database;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Lt2", Database.AppConnectionString);
        builder.UseSetting("Security:IpHashKey", "test-ip-hash-key");
        builder.UseSetting("RateLimits:CodeRequest:PermitLimit", codeRequestLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimits:CodeRequest:WindowSeconds", "600");
        builder.UseSetting("RateLimits:Registration:PermitLimit", registrationLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimits:Registration:WindowSeconds", "600");
        builder.UseSetting("RateLimits:SignIn:PermitLimit", "1000");
        builder.UseSetting("RateLimits:SignIn:WindowSeconds", "600");
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
}
