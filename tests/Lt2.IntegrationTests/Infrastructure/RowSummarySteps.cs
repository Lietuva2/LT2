using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>
/// Design 8.12.2 (owner, 2026-11-21): what a list row says of a bill whose threshold is reached, read as a visitor reads it:
/// the agenda's public answer and its page (the front page's rows are the same component over the same cached answer), and
/// <c>/projektai</c>'s answer and page.
/// </summary>
public static class RowSummarySteps
{
    /// <summary>The bill's numbers in <c>GET /api/v1/agenda</c>, from its planned row.</summary>
    public static async Task<AgendaRecordResponse> AgendaRecordAsync(this TestBrowser browser, string number)
    {
        var agenda = await browser.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda);
        return agenda.Days.SelectMany(d => d.Items).First(i => i.Number == number).Record.ShouldNotBeNull();
    }

    /// <summary>The bill's row on <c>/darbotvarke</c> under <paramref name="day"/>, its whole day shown, read afresh (not the minute's copy).</summary>
    public static async Task<string> AgendaRowAsync(this Lt2Factory factory, TestBrowser browser, string number, DateOnly day)
    {
        factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);
        var html = WebUtility.HtmlDecode(await browser.Client.GetStringAsync($"/darbotvarke?diena={day:yyyy-MM-dd}:9999"));
        return Item(html, $"<li class=\"tl-item\" id=\"agenda-{day:yyyy-MM-dd}-{number}\"");
    }

    /// <summary>The bill's row in <c>GET /api/v1/appeals</c>, found by its number.</summary>
    public static async Task<BillListItemResponse> ProjectsItemAsync(this TestBrowser browser, string number)
    {
        var list = await browser.GetJsonAsync<BillListResponse>(AppealRoutes.Appeals + "?q=" + Uri.EscapeDataString(number));
        return list.Items.Single(i => i.Bill.Number == number);
    }

    /// <summary>The bill's row on <c>/projektai</c>, found by its number.</summary>
    public static async Task<string> ProjectsRowAsync(this TestBrowser browser, string number)
    {
        var html = WebUtility.HtmlDecode(await browser.Client.GetStringAsync("/projektai?q=" + Uri.EscapeDataString(number)));
        return Item(html, $"<li id=\"bill-{number}\"");
    }

    private static string Item(string html, string start)
    {
        var at = html.IndexOf(start, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"the page has the row {start}");
        var end = html.IndexOf("</li>", at, StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(html[at..end], @"\s+", " ");
    }
}
