using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.12.5, 8.11.1 and 8.11.2 (owner, 2026-11-25) over HTTP: a bill's activity is its participants plus its published
/// arguments plus its followers, and that one sum orders <c>/projektai</c> by default and a day's bills on the agenda. The
/// participants' and arguments' orders keep their meaning. No row, in the answers or the pages, shows the follower number.
/// </summary>
public sealed class BillActivityTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly string[] Bills = [AppealSteps.TaxBill, AppealSteps.SecondBill];

    private async Task FreeAsync()
    {
        foreach (var bill in Bills)
        {
            await Database.FreeBillAsync(bill);
            await Database.UnplanAsync(bill);
            await Database.UnfollowBillAsync(bill);
        }

        Factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);
    }

    [Fact]
    public async Task Followers_count_into_a_bills_activity_on_projektai_and_on_the_agenda_and_no_row_shows_them()
    {
        await FreeAsync();
        var day = ReasonsToComeSteps.VilniusToday().AddDays(12);
        var followers = new List<TestBrowser>();
        try
        {
            await Database.PlanAsync(AppealSteps.TaxBill, day, "consideration");
            await Database.PlanAsync(AppealSteps.SecondBill, day, "consideration");
            using var visitor = NewBrowser();

            // A side on the second bill: one participant, and a first side also follows the bill (8.11.2): 2. The tax bill: 0.
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            (await member.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            Before(await ListAsync(visitor, "activity"), AppealSteps.SecondBill, AppealSteps.TaxBill).ShouldBeTrue("2 before 0");
            Before(await DayAsync(visitor, day), AppealSteps.SecondBill, AppealSteps.TaxBill).ShouldBeTrue("the agenda's day: 2 before 0");
            var (listRow, agendaRow) = await RowsAsync(visitor, day);

            // Three follow the tax bill, which has no record: 3 before 2, by default and on the agenda's day.
            for (var i = 0; i < 3; i++)
            {
                var (follower, _) = await Factory.MemberAsync(consent: false);
                followers.Add(follower);
                await follower.FollowBillAsync(AppealSteps.TaxBill);
            }

            Before(await ListAsync(visitor, "activity"), AppealSteps.TaxBill, AppealSteps.SecondBill).ShouldBeTrue("3 followers before 1 participant and 1 follower");
            Before(await ListAsync(visitor, null), AppealSteps.TaxBill, AppealSteps.SecondBill).ShouldBeTrue("activity is the default order");
            Before(await DayAsync(visitor, day), AppealSteps.TaxBill, AppealSteps.SecondBill).ShouldBeTrue("the agenda's day goes by the same sum");

            // The participants' order keeps its meaning: one participant before none, whatever the followers.
            Before(await ListAsync(visitor, "participants"), AppealSteps.SecondBill, AppealSteps.TaxBill).ShouldBeTrue("participants only");

            // No row carries the number: not the list's answer, not the agenda's, not the pages' rows.
            var listJson = await visitor.Client.GetStringAsync(AppealRoutes.AppealsList(AppealSteps.TaxBill, false, false, null));
            listJson.ShouldContain(AppealSteps.TaxBill);
            listJson.ShouldNotContain("follow", Case.Insensitive);
            (await visitor.Client.GetStringAsync(AgendaRoutes.Agenda)).ShouldNotContain("follow", Case.Insensitive);

            // The tax bill's rows are the same with three followers as with none: the number is not in them, nor a word of it.
            var (listRowAfter, agendaRowAfter) = await RowsAsync(visitor, day);
            listRowAfter.ShouldBe(listRow);
            agendaRowAfter.ShouldBe(agendaRow);
            foreach (var row in new[] { listRowAfter, agendaRowAfter })
            {
                row.ShouldNotContain("Sekėj", Case.Insensitive);
            }
        }
        finally
        {
            foreach (var follower in followers)
            {
                follower.Dispose();
            }

            await FreeAsync();
        }
    }

    private static async Task<List<string>> ListAsync(TestBrowser browser, string? sort) =>
        (await browser.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, sort, 200))).Items.Select(i => i.Bill.Number).ToList();

    private static async Task<List<string>> DayAsync(TestBrowser browser, DateOnly day) =>
        (await browser.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda)).Days.Single(d => d.Date == day).Items.Select(i => i.Number).ToList();

    /// <summary>Whether <paramref name="first"/> is listed and comes before <paramref name="second"/> (or the second is not listed at all).</summary>
    private static bool Before(List<string> numbers, string first, string second)
    {
        var i = numbers.IndexOf(first);
        var j = numbers.IndexOf(second);
        return i >= 0 && (j < 0 || i < j);
    }

    /// <summary>The tax bill's row on <c>/projektai</c> (found by its number) and on the agenda page, as a visitor gets them.</summary>
    private async Task<(string List, string Agenda)> RowsAsync(TestBrowser visitor, DateOnly day)
    {
        var list = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/projektai?q=" + Uri.EscapeDataString(AppealSteps.TaxBill)));
        Factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);
        var agenda = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
        return (Row(list, $"<li class=\"tl-item\" id=\"bill-{AppealSteps.TaxBill}\""), Row(agenda, $"id=\"agenda-{day:yyyy-MM-dd}-{AppealSteps.TaxBill}\""));
    }

    private static string Row(string html, string start)
    {
        var at = html.IndexOf(start, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, start);
        var row = html[at..];
        return row[..row.IndexOf("</li>", StringComparison.Ordinal)];
    }
}
