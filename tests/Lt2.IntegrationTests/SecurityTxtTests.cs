using System.Net;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace Lt2.IntegrationTests;

/// <summary>Design 6.7: <c>/.well-known/security.txt</c> (RFC 9116) exists only when <c>Site:SecurityContact</c> is set.</summary>
public sealed class SecurityTxtTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Path = "/.well-known/security.txt";

    private sealed class ContactFactory(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Site:SecurityContact", "mailto:sauga@lt2.test");
            builder.UseSetting("Site:Address", "https://lt2.test");
        }
    }

    [Fact]
    public async Task Without_a_security_contact_there_is_no_file()
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(Path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task With_a_security_contact_the_file_has_the_fields_of_RFC_9116()
    {
        await using var factory = new ContactFactory(Database);
        using var browser = factory.NewBrowser();
        var before = DateTimeOffset.UtcNow;
        using var response = await browser.Client.GetAsync(Path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        response.Headers.CacheControl!.MaxAge.ShouldBe(TimeSpan.FromHours(1));
        var lines = (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldBe("Contact: mailto:sauga@lt2.test");
        lines[2].ShouldBe("Preferred-Languages: lt, en");
        lines[3].ShouldBe("Canonical: https://lt2.test/.well-known/security.txt");
        lines[1].ShouldStartWith("Expires: ");
        var expires = DateTimeOffset.Parse(lines[1]["Expires: ".Length..], System.Globalization.CultureInfo.InvariantCulture);
        expires.ShouldBeInRange(before.AddDays(180).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(180).AddMinutes(1));
        lines[1].ShouldEndWith("Z");
    }
}
