using System.Net;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Parliament;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// While the parliament data comes from the Seimas stand-in, every page says it is invented. The web host itself never
/// calls the Seimas, so an open-data outage cannot take the site down.
/// </summary>
public sealed class InventedDataNoticeTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Notice = "Seimo narių ir balsavimų duomenys čia išgalvoti";

    private static readonly string[] Pages = ["/", "/prisijungti"];

    public async ValueTask InitializeAsync() => await ParliamentSeed.EnsureAsync(database);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Every_page_says_the_parliament_data_is_invented_while_the_stored_responses_come_from_the_stand_in()
    {
        await using var factory = new Lt2Factory(database);
        using var browser = factory.NewBrowser();
        foreach (var page in Pages)
        {
            var html = WebUtility.HtmlDecode(await Html(browser, page));
            html.ShouldContain(Notice, customMessage: page);
            html.ShouldContain("role=\"note\" id=\"invented-data\"", customMessage: page);
        }
    }

    [Fact]
    public async Task No_page_says_so_when_no_stored_response_is_synthetic()
    {
        await database.ExecuteAsync("UPDATE parliament.raw_document SET synthetic = false");
        try
        {
            await using var factory = new Lt2Factory(database);
            using var browser = factory.NewBrowser();
            foreach (var page in Pages)
            {
                WebUtility.HtmlDecode(await Html(browser, page)).ShouldNotContain(Notice, customMessage: page);
            }
        }
        finally
        {
            await database.ExecuteAsync("UPDATE parliament.raw_document SET synthetic = true");
        }
    }

    [Fact]
    public async Task The_web_host_has_no_seimas_client_and_no_import()
    {
        await using var factory = new Lt2Factory(database);
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IParliamentImport>().ShouldBeNull();
        scope.ServiceProvider.GetService<IParliamentDataNotice>().ShouldNotBeNull();
    }

    private static async Task<string> Html(TestBrowser browser, string path)
    {
        using var response = await browser.Client.GetAsync(path);
        return await response.Content.ReadAsStringAsync();
    }
}
