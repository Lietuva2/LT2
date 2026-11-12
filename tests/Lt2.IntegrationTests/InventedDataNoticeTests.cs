using System.Net;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Parliament;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// While the parliament data comes from the Seimas stand-in, every page says it is invented. The web host itself never
/// calls the Seimas, so an open-data outage cannot take the site down.
/// </summary>
public sealed class InventedDataNoticeTests(DatabaseFixture database)
{
    private const string Notice = "Seimo narių ir balsavimų duomenys čia išgalvoti";

    [Fact]
    public async Task Every_page_says_the_parliament_data_is_invented_once_a_stand_in_response_is_stored_and_not_before()
    {
        string[] pages = ["/", "/prisijungti"];
        await using (var before = new Lt2Factory(database))
        {
            using var browser = before.NewBrowser();
            foreach (var page in pages)
            {
                WebUtility.HtmlDecode(await Html(browser, page)).ShouldNotContain(Notice, customMessage: page);
            }
        }

        var run = Guid.CreateVersion7();
        await database.ExecuteAsync(
            "INSERT INTO parliament.import_run (id, started_at, status, source, synthetic, requests, changes) VALUES (@id, now(), 'succeeded', 'http://seimas-standin:8080/sip/', true, 1, 0)",
            ("id", run));
        await database.ExecuteAsync(
            "INSERT INTO parliament.raw_document (id, request, service, url, sha256, body, synthetic, import_run_id, first_fetched_at, last_fetched_at) " +
            "VALUES (@id, 'p2b.ad_seimo_kadencijos', 'p2b.ad_seimo_kadencijos', 'http://seimas-standin:8080/sip/p2b.ad_seimo_kadencijos', 'x', '\\x00', true, @run, now(), now())",
            ("id", Guid.CreateVersion7()), ("run", run));
        try
        {
            await using var after = new Lt2Factory(database);
            using var browser = after.NewBrowser();
            foreach (var page in pages)
            {
                var html = WebUtility.HtmlDecode(await Html(browser, page));
                html.ShouldContain(Notice, customMessage: page);
                html.ShouldContain("role=\"note\" id=\"invented-data\"", customMessage: page);
            }
        }
        finally
        {
            await database.ExecuteAsync("DELETE FROM parliament.raw_document WHERE import_run_id = @run", ("run", run));
            await database.ExecuteAsync("DELETE FROM parliament.import_run WHERE id = @run", ("run", run));
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
