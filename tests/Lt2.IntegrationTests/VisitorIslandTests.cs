using System.Net;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Owner, 2026-11-28 (design 2: only signed-in features use WebAssembly): a public page serves a visitor its islands as plain
/// HTML, so the browser never downloads or starts the WebAssembly runtime, and says nothing about needing JavaScript; a
/// signed-in session gets the same page with its islands.
/// </summary>
public sealed class VisitorIslandTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Island = "<!--Blazor:{\"type\":\"webassembly\"";

    private static readonly string[] PublicPages = ["/", "/projektai", "/darbotvarke", "/seimas/XVP-0412", "/p/jonas-petraitis", "/statistika"];

    private static async Task<string> PageAsync(TestBrowser browser, string path)
    {
        using var response = await browser.Client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_visitor_gets_public_pages_without_islands_and_a_member_gets_them_with()
    {
        using var visitor = Factory.NewBrowser();
        foreach (var path in PublicPages)
        {
            var html = await PageAsync(visitor, path);
            html.ShouldNotContain(Island, customMessage: path);
            html.ShouldNotContain("id=\"needs-script\"", customMessage: path);
        }

        // What an island shows a visitor is still there, as plain HTML: "Sekti" is a link to sign in that comes back.
        (await PageAsync(visitor, "/seimas/XVP-0412")).ShouldContain("id=\"follow-sign-in-bill\"");

        using var member = Factory.NewBrowser();
        await member.RegisterAndSignInAsync();
        var bill = await PageAsync(member, "/seimas/XVP-0412");
        bill.ShouldContain(Island);
        bill.ShouldContain("id=\"needs-script\"");
    }
}
