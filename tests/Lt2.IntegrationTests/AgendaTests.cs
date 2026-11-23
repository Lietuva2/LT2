using System.Net;
using Lt2.Api.Contracts;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Client.Agenda;
using Lt2.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.11.1, 8.12.5 over HTTP: the agenda answer and page, the bill page's planned date and controls, and the front
/// page built on the agenda. Public, readable without JavaScript, nothing about the caller.
/// </summary>
public sealed partial class AgendaTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task The_agenda_lists_planned_bills_by_day_with_both_sides_for_every_bill()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var record = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "for");
        await Database.FreeBillAsync(AppealSteps.SecondBill);

        var today = ReasonsToComeSteps.VilniusToday();
        await Database.PlanAsync(AppealSteps.SecondBill, today.AddDays(5), "adoption");
        await Database.PlanAsync(AppealSteps.ThirdBill, today.AddDays(7));
        try
        {
            using var visitor = NewBrowser();
            var agenda = await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda);
            agenda.Days.Select(d => d.Date).ShouldBeInOrder();
            agenda.Days.ShouldAllBe(d => d.Date >= today);

            var second = agenda.Days.Single(d => d.Date == today.AddDays(5)).Items.Single(i => i.Number == AppealSteps.SecondBill);
            second.Title.ShouldNotBeNull();
            second.Stage.ShouldBe("adoption");
            second.Record.ShouldNotBeNull("both sides for every bill (design 8.12.5)");
            second.Record.Id.ShouldBeNull();
            second.Record.Status.ShouldBe("open");
            second.Record.Counts.ShouldBe(new Lt2.Api.Contracts.Appeals.SideCountsResponse(0, 0, 0, 0));

            var third = agenda.Days.Single(d => d.Date == today.AddDays(7)).Items.Single(i => i.Number == AppealSteps.ThirdBill);
            third.Record.ShouldNotBeNull();
            third.Record.Id.ShouldBe(record.Id);
            third.Record.Counts.For.ShouldBe(1);
            third.Record.Threshold.Participants.ShouldBe(1);

            // The page, without JavaScript: planned dates in words, never a countdown.
            ForgetAgenda();
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
            html.ShouldContain("<title>Seimo darbotvarkė · Lietuva 2.0</title>");
            html.ShouldContain("<h1 class=\"seimas-title\">Seimas</h1>");
            html.ShouldContain("<b aria-current=\"page\" id=\"view-agenda\">Darbotvarkė</b>");
            html.ShouldContain("<a href=\"/projektai\" id=\"tab-active\">Aktyvūs</a>");
            html.ShouldContain("<a href=\"/projektai?skirtukas=uzdaryti\" id=\"tab-closed\">Uždaryti</a>");
            html.ShouldContain("<p class=\"lead\">Planuojama svarstyti artimiausiu metu.</p>");
            html.ShouldContain("Planuojama ");
            html.ShouldMatch("id=\"agenda-\\d{4}-\\d{2}-\\d{2}-" + AppealSteps.SecondBill + "\"");

            // Owner, 2026-11-19: the front page's row ("Prašau balsuoti" in the row; a visitor's sides lead to signing in and
            // back to the bill), with no line saying nobody took part beside the buttons and no link but the title to the bill.
            var row = Row(html, today.AddDays(5), AppealSteps.SecondBill);
            row.ShouldContain("Prašau balsuoti");
            row.ShouldContain($"<a class=\"btn btn--for cta\" id=\"home-for-{today.AddDays(5):yyyy-MM-dd}-{AppealSteps.SecondBill}\" href=\"/prisijungti#tikslas=%2Fseimas%2F{AppealSteps.SecondBill}\"");
            row.ShouldNotContain("Niekas nenurodė");
            row.ShouldNotContain("Nurodyti, ko prašote");
            Row(html, today.AddDays(7), AppealSteps.ThirdBill).ShouldNotContain("Ko prašo, argumentai");
            Row(html, today.AddDays(7), AppealSteps.ThirdBill).ShouldContain($"id=\"home-counts-{today.AddDays(7):yyyy-MM-dd}-{AppealSteps.ThirdBill}\"");
            // Owner, 2026-11-21: a list's row shows one number a side, without the count for accounts older than 30 days.
            Row(html, today.AddDays(7), AppealSteps.ThirdBill).ShouldMatch(
                "id=\"home-counts-[0-9-]+" + AppealSteps.ThirdBill + "\">\\s*<span class=\"nowrap\"><span class=\"choice choice--for\">už</span> 1 ·</span>\\s*<span class=\"nowrap\"><span class=\"choice choice--against\">prieš</span> 0</span>\\s*</span>");
            html.ShouldNotContain("Dar niekas nenurodė");
            html.ShouldNotContain("Kreipimosi prašo");
            html.ShouldNotContain("/kreipimasis\"");
            Countdown().IsMatch(html).ShouldBeFalse("never a countdown");

            // The coming list is the only sheet and has no heading row (the tab row of Seimas is the sticky part); the votes
            // held are the tab "Balsavimai", not a list. Nothing under the list: no note, no hint, no "not a representative poll".
            html.ShouldContain("<section class=\"sheet stack\" aria-label=\"Planuojami projektai\" id=\"agenda-coming\">");
            html.ShouldNotContain("list-head");
            html.ShouldNotContain("id=\"agenda-recent\"");
            html.ShouldNotContain("id=\"agenda-no-recent\"");
            html.ShouldNotContain("agenda-votes-older");
            html.ShouldNotContain("Ankstesni balsavimai");
            html.ShouldContain("<a href=\"/seimas/balsavimai\" id=\"tab-seimas-balsavimai\">Balsavimai</a>");
            html.ShouldNotContain("Tai nėra reprezentatyvi apklausa");
            html.ShouldNotContain("id=\"not-a-poll\"");
            html.ShouldNotContain("id=\"agenda-older-note\"");
            html.ShouldNotContain("Skliaustuose");
            html.ShouldNotContain("id=\"threshold-what\"");
            html.ShouldNotContain("Kas yra balsavusiųjų dauguma");
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
            await Database.UnplanAsync(AppealSteps.ThirdBill);
        }
    }

    /// <summary>
    /// Owner, 2026-11-18: the agenda has no order within a day, so a day's bills go by activity (participants plus published
    /// arguments), then stage, then number, and the bills the open data does not know yet come last, folded into one line on
    /// the page that opens without script; each day's head says how many it holds.
    /// </summary>
    [Fact]
    public async Task Within_a_day_bills_go_by_activity_then_stage_and_bills_the_open_data_does_not_know_come_last_folded()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "for");
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        const string Unknown = "XVP-9871";
        var day = ReasonsToComeSteps.VilniusToday().AddDays(8);
        await Database.PlanAsync(Unknown, day, "adoption");
        await Database.PlanAsync(AppealSteps.SecondBill, day, "adoption");
        await Database.PlanAsync(AppealSteps.ThirdBill, day, "presentation");
        try
        {
            using var visitor = NewBrowser();
            var agenda = await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda);
            var numbers = agenda.Days.Single(d => d.Date == day).Items.Select(i => i.Number).ToList();
            numbers.IndexOf(AppealSteps.ThirdBill).ShouldBeLessThan(numbers.IndexOf(AppealSteps.SecondBill), "one participant before none, whatever the stage");
            numbers.IndexOf(AppealSteps.SecondBill).ShouldBeLessThan(numbers.IndexOf(Unknown), "a bill the open data does not know comes last");

            ForgetAgenda();
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
            var id = $"diena-{day:yyyy-MM-dd}";
            var block = html[html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal)..];
            block = block[..block.IndexOf("</section>", StringComparison.Ordinal)];
            block.ShouldContain("projektai</span>", customMessage: "the day's head counts its bills");
            block.IndexOf($"id=\"agenda-{day:yyyy-MM-dd}-{AppealSteps.ThirdBill}\"", StringComparison.Ordinal)
                .ShouldBeLessThan(block.IndexOf($"id=\"agenda-{day:yyyy-MM-dd}-{AppealSteps.SecondBill}\"", StringComparison.Ordinal));
            var folded = block[block.IndexOf($"id=\"{id}-nezinomi\"", StringComparison.Ordinal)..];
            folded.ShouldContain("<summary class=\"linkish small\">Dar 1 projektas, kurio atviruosiuose Seimo duomenyse dar nėra</summary>");
            folded.ShouldContain($"id=\"agenda-{day:yyyy-MM-dd}-{Unknown}\"");
            block.IndexOf($"id=\"{id}-nezinomi\"", StringComparison.Ordinal).ShouldBeGreaterThan(block.IndexOf($"id=\"agenda-{day:yyyy-MM-dd}-{AppealSteps.SecondBill}\"", StringComparison.Ordinal));
            block.ShouldContain($"id=\"home-against-{day:yyyy-MM-dd}-{AppealSteps.ThirdBill}\"", customMessage: "the front page's row, with its sides (owner, 2026-11-19)");
            block.ShouldNotContain("class=\"tl-item__side\" id=\"home-side-", customMessage: "a visitor's row has no island: its sides are links");
        }
        finally
        {
            await Database.UnplanAsync(Unknown);
            await Database.UnplanAsync(AppealSteps.SecondBill);
            await Database.UnplanAsync(AppealSteps.ThirdBill);
            ForgetAgenda();
        }
    }

    /// <summary>
    /// Owner, 2026-11-19 (one way for every list): the page holds only the rows it shows. A long day shows ten, then the
    /// shared "show more": "Rodoma 10 iš 66" (the day's head count, the bills the open data does not know yet included) and
    /// "Rodyti daugiau", a link to the page with that day grown by 50 (owner, 2026-11-19: the first page small, each further one
    /// larger; <c>?diena=2026-11-24:60</c>) landing on the first row added, and from there by 50 more. Without JavaScript the
    /// server renders the grown day; its last page lists the unknown bills after the last known row, counts them as shown
    /// ("Rodoma 66 iš 66") and has no link. A value the page would not make is the default, never an error. The grown page is
    /// the same for two members.
    /// </summary>
    [Fact]
    public async Task A_long_day_shows_ten_rows_and_rodyti_daugiau_grows_it_by_fifty_by_the_address()
    {
        var numbers = Enumerable.Range(1, 65).Select(i => $"XVP-L{i:000}").ToList();
        const string Unknown = "XVP-L099";
        var day = ReasonsToComeSteps.VilniusToday().AddDays(9);
        await RemoveLongDayAsync(numbers, Unknown);
        await PlanLongDayAsync(numbers, Unknown, day);
        try
        {
            ForgetAgenda();
            using var visitor = NewBrowser();
            var items = (await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda)).Days.Single(d => d.Date == day).Items;
            var known = items.Where(i => i.Title is not null).ToList();
            known.Count.ShouldBeGreaterThan(60, "a day longer than its first two pages");
            known.Count.ShouldBeLessThanOrEqualTo(110, "three pages hold the day");
            var id = $"diena-{day:yyyy-MM-dd}";

            var cut = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke")), id);
            Regex.Count(cut, "<li class=\"tl-item\"").ShouldBe(AgendaRules.PagePerDay, "ten rows; the rest is not in the HTML");
            cut.ShouldContain($"<p class=\"small num\" role=\"status\" id=\"{id}-shown\">Rodoma 10 iš {items.Count}</p>");
            var next = $"/darbotvarke?diena={day:yyyy-MM-dd}:60#agenda-{day:yyyy-MM-dd}-{known[AgendaRules.PagePerDay].Number}";
            cut.ShouldContain($"<a class=\"show-more__link\" href=\"{next}\" id=\"{id}-more\">Rodyti daugiau</a>");
            cut.ShouldNotContain(Unknown, customMessage: "the unknown bills come after the last known row");
            cut.ShouldNotContain("Rodyti likusius");

            var grown = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/darbotvarke?diena={day:yyyy-MM-dd}:60")), id);
            Regex.Count(grown, "<li class=\"tl-item\"").ShouldBe(60, "the first ten and 50 more");
            grown.ShouldContain($"id=\"agenda-{day:yyyy-MM-dd}-{known[AgendaRules.PagePerDay].Number}\"", customMessage: "where the link lands");
            grown.ShouldNotContain($"id=\"agenda-{day:yyyy-MM-dd}-{known[60].Number}\"", customMessage: "the 61st waits for the next press");
            grown.ShouldContain($"<p class=\"small num\" role=\"status\" id=\"{id}-shown\">Rodoma 60 iš {items.Count}</p>");
            grown.ShouldContain($"href=\"/darbotvarke?diena={day:yyyy-MM-dd}:110#agenda-{day:yyyy-MM-dd}-{known[60].Number}\"", customMessage: "50 more again");
            grown.ShouldNotContain(Unknown);

            var last = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/darbotvarke?diena={day:yyyy-MM-dd}:110")), id);
            Regex.Count(last, "<li class=\"tl-item\"").ShouldBe(known.Count);
            foreach (var number in numbers)
            {
                last.ShouldContain($"id=\"agenda-{day:yyyy-MM-dd}-{number}\"");
            }

            last.IndexOf($"id=\"{id}-nezinomi\"", StringComparison.Ordinal)
                .ShouldBeGreaterThan(last.IndexOf($"id=\"agenda-{day:yyyy-MM-dd}-{known[^1].Number}\"", StringComparison.Ordinal), "the unknown bills after the last known row");
            last.ShouldContain($"<p class=\"small num\" role=\"status\" id=\"{id}-shown\">Rodoma {items.Count} iš {items.Count}</p>", customMessage: "the folded bills count as shown with their line");
            last.ShouldNotContain("show-more__link", customMessage: "every row shown: no link");

            foreach (var value in new[] { "rytoj", $"{day:yyyy-MM-dd}", $"{day:yyyy-MM-dd}:5", $"{day:yyyy-MM-dd}:-20", $"{day:yyyy-MM-dd}:daug", "2026-13-01:20" })
            {
                using var answer = await visitor.Client.GetAsync($"/darbotvarke?diena={Uri.EscapeDataString(value)}");
                answer.StatusCode.ShouldBe(HttpStatusCode.OK, value);
                var page = DayBlock(WebUtility.HtmlDecode(await answer.Content.ReadAsStringAsync()), id);
                Regex.Count(page, "<li class=\"tl-item\"").ShouldBe(AgendaRules.PagePerDay, $"{value}: a value the page would not make is the default");
            }

            var all = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/darbotvarke?diena={day:yyyy-MM-dd}:9999")), id);
            Regex.Count(all, "<li class=\"tl-item\"").ShouldBe(known.Count, "a number beyond the day shows the day");

            // A grown page holds nothing of a member's own: the same for two members (design 8.7, "Privacy").
            var (first, _) = await Factory.MemberAsync();
            using var _f = first;
            var (second, _) = await Factory.MemberAsync();
            using var _s = second;
            var firstPage = await first.Client.GetStringAsync($"/darbotvarke?diena={day:yyyy-MM-dd}:60");
            firstPage.ShouldContain($"id=\"home-side-{day:yyyy-MM-dd}-{known[59].Number}\"", customMessage: "a row added carries the member's island");
            FrontPageTests.Frameless(firstPage).ShouldBe(FrontPageTests.Frameless(await second.Client.GetStringAsync($"/darbotvarke?diena={day:yyyy-MM-dd}:60")));
        }
        finally
        {
            await RemoveLongDayAsync(numbers, Unknown);
            ForgetAgenda();
        }
    }
    /// <summary>
    /// Owner, 2026-11-19: the agenda page's rows are the front page's, so a member's carry the islands of the mark and the
    /// side column. The page's HTML is the same for two members and holds nothing of either one's own (design 8.7,
    /// "Privacy"): their sides, follows, names and addresses are filled in the browser only. Without the front page's
    /// "Jūsų projektai" and greeting. The buttons are disabled in the served page, which says JavaScript is needed.
    /// </summary>
    [Fact]
    public async Task The_agenda_page_is_the_same_for_two_members_and_holds_neither_ones_own()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var day = ReasonsToComeSteps.VilniusToday().AddDays(2);
        await Database.PlanAsync(AppealSteps.SecondBill, day);
        var (first, firstEmail) = await Factory.MemberAsync("Pirmoji Darbotvarkės");
        using var _f = first;
        var (second, secondEmail) = await Factory.MemberAsync("Antrasis Darbotvarkės");
        using var _s = second;
        try
        {
            (await first.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await first.FollowBillAsync(AppealSteps.SecondBill);

            ForgetAgenda();
            var firstPage = await first.Client.GetStringAsync("/darbotvarke");
            var secondPage = await second.Client.GetStringAsync("/darbotvarke");

            // The proof that the sweep reads the right page: the member's version, with the row's islands.
            var place = $"{day:yyyy-MM-dd}-{AppealSteps.SecondBill}";
            firstPage.ShouldContain($"id=\"home-side-{place}\"");
            firstPage.ShouldMatch($"<button type=\"button\" class=\"btn btn--for cta\" id=\"home-for-{place}\" aria-pressed=\"false\"[^>]* disabled");
            firstPage.ShouldContain("id=\"needs-script\"");
            FrontPageTests.Frameless(firstPage).ShouldBe(FrontPageTests.Frameless(secondPage), "the same page for every member, apart from the frame");
            foreach (var page in new[] { firstPage, secondPage })
            {
                var text = WebUtility.HtmlDecode(page);
                foreach (var own in new[] { firstEmail, secondEmail, "Pirmoji Darbotvarkės", "Antrasis Darbotvarkės" })
                {
                    text.ShouldNotContain(own, Case.Insensitive);
                }

                text.ShouldNotContain("aria-pressed=\"true\"");
                text.ShouldNotContain("Jūs prašote balsuoti");
                text.ShouldNotContain("Sekate");
                text.ShouldNotContain("id=\"my-agenda\"", customMessage: "no \"Jūsų projektai\" on the agenda page");
                text.ShouldNotContain("id=\"home-title\"");
                var state = FrontPageTests.StateOf(page);
                state.ShouldNotContain(firstEmail, Case.Insensitive);
                state.ShouldNotContain(secondEmail, Case.Insensitive);
                state.ShouldNotContain("Darbotvarkės", Case.Insensitive);
            }
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
            ForgetAgenda();
        }
    }

    /// <summary>One row of the agenda page, by its day and bill.</summary>
    private static string Row(string html, DateOnly day, string number)
    {
        var row = html[html.IndexOf($"id=\"agenda-{day:yyyy-MM-dd}-{number}\"", StringComparison.Ordinal)..];
        return row[..row.IndexOf("</li>", StringComparison.Ordinal)];
    }

    /// <summary>A long sitting day: these bills known to the open data (no participants, presentation), and one it does not know.</summary>
    private async Task PlanLongDayAsync(List<string> numbers, string unknown, DateOnly day)
    {
        foreach (var number in numbers)
        {
            await Database.ExecuteAsync("INSERT INTO parliament.bill (id, number, title) VALUES (gen_random_uuid(), @n, @t)", ("n", number), ("t", "Ilgos dienos projektas " + number));
            await Database.PlanAsync(number, day, "presentation");
        }

        await Database.PlanAsync(unknown, day, "presentation");
    }

    private async Task RemoveLongDayAsync(List<string> numbers, string unknown)
    {
        foreach (var number in numbers.Append(unknown))
        {
            await Database.UnplanAsync(number);
        }

        await Database.ExecuteAsync("DELETE FROM parliament.bill WHERE number LIKE 'XVP-L%'");
    }

    /// <summary>One day's block of the agenda page.</summary>
    private static string DayBlock(string html, string id)
    {
        var block = html[html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal)..];
        return block[..block.IndexOf("</section>", StringComparison.Ordinal)];
    }

    /// <summary>
    /// Owner, 2026-11-18: the front page shows the next two sitting days, five rows of each. Owner, 2026-11-19: under a longer
    /// day the shared "Rodoma 5 iš N · Rodyti daugiau" grows it by 50 in place by the address (<c>/?diena=…:55</c>), landing on
    /// the first row added; the bills the open data does not know yet are counted in the day and listed once every known row
    /// shows. A day whose rows all fit has no such line.
    /// </summary>
    [Fact]
    public async Task The_front_page_shows_five_rows_of_a_day_and_rodyti_daugiau_grows_it_in_place_by_fifty()
    {
        var numbers = Enumerable.Range(1, 60).Select(i => $"XVP-L{i:000}").ToList();
        const string Unknown = "XVP-L099";
        var today = ReasonsToComeSteps.VilniusToday();
        await RemoveLongDayAsync(numbers, Unknown);
        await PlanLongDayAsync(numbers, Unknown, today);
        try
        {
            ForgetAgenda();
            using var visitor = NewBrowser();
            var items = (await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda)).Days.Single(d => d.Date == today).Items;
            var known = items.Where(i => i.Title is not null).ToList();
            var id = $"home-day-{today:yyyy-MM-dd}";
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/"));
            var block = DayBlock(html, id);
            Regex.Count(block, "<li class=\"tl-item\"").ShouldBe(AgendaRules.FrontPerDay);
            block.ShouldNotContain(Unknown, customMessage: "counted in the day, listed after its last known row");
            block.ShouldContain($"<p class=\"small num\" role=\"status\" id=\"{id}-shown\">Rodoma 5 iš {items.Count}</p>");
            block.ShouldContain($"<a class=\"show-more__link\" href=\"/?diena={today:yyyy-MM-dd}:55#agenda-{today:yyyy-MM-dd}-{known[AgendaRules.FrontPerDay].Number}\" id=\"{id}-more\">Rodyti daugiau</a>");
            block.ShouldNotContain("Rodyti likusius");
            Regex.Count(html, "<section class=\"tl-day\"").ShouldBeLessThanOrEqualTo(2, "the next two sitting days");
            ReasonsToComeSteps.HeadRow(html, "h-home-coming").ShouldContain("<a href=\"/darbotvarke\" id=\"home-agenda-all\">", customMessage: "one link to the whole agenda, beside the heading");

            known.Count.ShouldBeGreaterThan(55, "the day's known bills do not fit two pages");
            known.Count.ShouldBeLessThanOrEqualTo(105, "three pages hold them");
            var grown = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/?diena={today:yyyy-MM-dd}:55")), id);
            Regex.Count(grown, "<li class=\"tl-item\"").ShouldBe(55, "the first five and 50 more");
            grown.ShouldContain($"<p class=\"small num\" role=\"status\" id=\"{id}-shown\">Rodoma 55 iš {items.Count}</p>");
            grown.ShouldContain($"href=\"/?diena={today:yyyy-MM-dd}:105#agenda-{today:yyyy-MM-dd}-{known[55].Number}\"");
            grown.ShouldNotContain(Unknown);

            grown = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/?diena={today:yyyy-MM-dd}:105")), id);
            Regex.Count(grown, "<li class=\"tl-item\"").ShouldBe(known.Count);
            grown.ShouldContain($"id=\"{id}-nezinomi\"", customMessage: "the last page: the bills the open data does not know yet");
            grown.ShouldContain(Unknown);
            grown.ShouldContain($"Rodoma {items.Count} iš {items.Count}</p>");
            grown.ShouldNotContain("show-more__link");
        }
        finally
        {
            await RemoveLongDayAsync(numbers, Unknown);
            ForgetAgenda();
        }
    }

    /// <summary>Owner, 2026-11-19: a day whose rows all fit its first page has no "Rodoma N iš M" line; its unknown bills show.</summary>
    [Fact]
    public async Task A_day_whose_rows_all_fit_has_no_show_more_line_and_lists_its_unknown_bills()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var day = ReasonsToComeSteps.VilniusToday().AddDays(1);
        var unknown = Enumerable.Range(1, 5).Select(i => $"XVP-98{i:00}").ToList();
        await Database.PlanAsync(AppealSteps.SecondBill, day);
        foreach (var number in unknown)
        {
            await Database.PlanAsync(number, day);
        }

        try
        {
            ForgetAgenda();
            using var visitor = NewBrowser();
            var agenda = await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda);
            var items = agenda.Days.Single(d => d.Date == day).Items;
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
            var block = DayBlock(html, $"diena-{day:yyyy-MM-dd}");
            items.Count(i => i.Title is not null).ShouldBeLessThanOrEqualTo(AgendaRules.PagePerDay, "a day whose rows all fit");
            block.ShouldContain($"id=\"agenda-{day:yyyy-MM-dd}-{AppealSteps.SecondBill}\"", customMessage: "the day read is the one planned");
            block.ShouldNotContain("show-more", customMessage: "every row fits: no line");
            block.ShouldContain($"id=\"diena-{day:yyyy-MM-dd}-nezinomi\"");
            foreach (var number in unknown)
            {
                block.ShouldContain(number);
            }
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
            foreach (var number in unknown)
            {
                await Database.UnplanAsync(number);
            }

            ForgetAgenda();
        }
    }
    [Fact]
    public async Task A_bill_whose_moderator_date_has_passed_is_on_the_agenda_at_its_newer_date()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var today = ReasonsToComeSteps.VilniusToday();
        await Database.UnplanAsync(AppealSteps.SecondBill);
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, first_seen, last_seen, note)
            VALUES (gen_random_uuid(), @b, @past, 'consideration', 'moderator', now() - interval '20 days', now() - interval '20 days', 'Pataisyta data.'),
                   (gen_random_uuid(), @b, @next, 'adoption', 'agenda', now(), now(), NULL)
            """,
            ("b", AppealSteps.SecondBill), ("past", today.AddDays(-3)), ("next", today.AddDays(4)));
        try
        {
            using var visitor = NewBrowser();
            var agenda = await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda);
            var item = agenda.Days.Single(d => d.Date == today.AddDays(4)).Items.Single(i => i.Number == AppealSteps.SecondBill);
            item.Stage.ShouldBe("adoption");

            ForgetAgenda();
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
            html.ShouldContain($"id=\"agenda-{today.AddDays(4):yyyy-MM-dd}-{AppealSteps.SecondBill}\"");
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
        }
    }

    [Fact]
    public async Task The_agenda_answer_holds_the_stage_votes_of_the_last_seven_days_only_and_the_page_lists_none()
    {
        var old = await Database.MoveVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, DateTimeOffset.UtcNow.AddDays(-2));
        try
        {
            using var visitor = NewBrowser();
            var agenda = await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda);
            var vote = agenda.RecentVotes.Single(v => v.VoteId == ReasonsToComeSteps.TaxConsiderationVote);
            vote.Number.ShouldBe(AppealSteps.TaxBill);
            vote.Stage.ShouldBe("consideration");
            vote.Totals.For.ShouldBe(74);
            agenda.RecentVotes.ShouldAllBe(v => v.HeldAt >= DateTimeOffset.UtcNow.AddDays(-7));

            ForgetAgenda();
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
            // The votes held are no list of the agenda page: they are in the vote history, the tab "Balsavimai".
            html.ShouldNotContain("id=\"recent-" + ReasonsToComeSteps.TaxConsiderationVote + "\"");
        }
        finally
        {
            await Database.RestoreVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, old);
        }

        using var later = NewBrowser();
        (await later.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda)).RecentVotes.ShouldNotContain(v => v.VoteId == ReasonsToComeSteps.TaxConsiderationVote);
    }

    [Fact]
    public async Task The_agenda_answer_is_the_same_for_everyone_and_cacheable()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.PlanAsync(AppealSteps.SecondBill, ReasonsToComeSteps.VilniusToday().AddDays(3));
        try
        {
            var (asker, _) = await Factory.MemberAsync();
            using var _a = asker;
            (await asker.TakeSideAsync(AppealSteps.SecondBill, "for")).Dispose();
            var (other, _) = await Factory.MemberAsync();
            using var _o = other;
            using var visitor = NewBrowser();

            using var forAsker = await asker.Client.GetAsync(AgendaRoutes.Agenda);
            using var forOther = await other.Client.GetAsync(AgendaRoutes.Agenda);
            using var forVisitor = await visitor.Client.GetAsync(AgendaRoutes.Agenda);
            var body = await forVisitor.Content.ReadAsStringAsync();
            (await forAsker.Content.ReadAsStringAsync()).ShouldBe(body);
            (await forOther.Content.ReadAsStringAsync()).ShouldBe(body);
            forVisitor.Headers.CacheControl!.Public.ShouldBeTrue();
            forVisitor.Headers.ETag.ShouldNotBeNull();
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
        }
    }

    [Fact]
    public async Task The_bill_page_of_a_bill_nobody_took_part_in_shows_the_planned_date_and_the_same_controls_without_javascript()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var date = ReasonsToComeSteps.VilniusToday().AddDays(4);
        await Database.PlanAsync(AppealSteps.SecondBill, date, "adoption");
        try
        {
            using var visitor = NewBrowser();
            (await visitor.GetJsonAsync<BillResponse>(ParliamentRoutes.BillByNumber(AppealSteps.SecondBill))).Planned!.Date.ShouldBe(date);
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
            html.ShouldContain("id=\"bill-planned\"");
            html.ShouldContain("Planuojama ");
            html.ShouldContain("id=\"nobody-yet\"");
            html.ShouldContain("id=\"no-description\"");

            // Owner, 2026-11-18: the e-seimas address once, under "no description yet"; the stages repeat it not.
            var eSeimas = (await visitor.GetJsonAsync<BillResponse>(ParliamentRoutes.BillByNumber(AppealSteps.SecondBill))).ESeimasUrl.ShouldNotBeNull();
            html.Split($"href=\"{eSeimas}\"").Length.ShouldBe(2, "the e-seimas address once on the page");
            html.ShouldContain($"<a href=\"{eSeimas}\" rel=\"external\">Projekto tekstas e. seime</a>");
            html.ShouldNotContain("Projektas e-seime");
            html.ShouldContain("id=\"side-picker\"");
            html.ShouldContain("href=\"/prisijungti\"");
            html.ShouldNotContain("Noriu kreipimosi");
            html.ShouldNotContain("aprašymą pačiam");
            html.ShouldContain("Tai nėra reprezentatyvi apklausa");
            Countdown().IsMatch(html).ShouldBeFalse("never a countdown");
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
        }
    }

    [Fact]
    public async Task The_first_menu_item_Seimas_opens_the_agenda()
    {
        // Design 8.21: "Seimas" is first; its dropdown's first tab, "Projektai", is /darbotvarke (owner, 2026-11-17); where the
        // device can hover, the item itself is a link to it (owner, 2026-11-19).
        using var visitor = NewBrowser();
        var html = await visitor.Client.GetStringAsync("/statistika");
        var nav = html[html.IndexOf("top__nav", StringComparison.Ordinal)..];
        nav.IndexOf("<a class=\"drop__toggle drop__link\" href=\"/darbotvarke\" id=\"nav-projects\">Seimas</a>", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        nav.IndexOf("id=\"nav-projects\"", StringComparison.Ordinal).ShouldBeLessThan(nav.IndexOf("id=\"nav-tab-projektai\"", StringComparison.Ordinal));
        nav.IndexOf("id=\"nav-tab-projektai\"", StringComparison.Ordinal).ShouldBeLessThan(nav.IndexOf("href=\"/statistika\"", StringComparison.Ordinal));
    }

    /// <summary>The agenda page reads the front page's copy, kept a minute (owner, 2026-11-18): read afresh.</summary>
    private void ForgetAgenda() => Factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);

    [GeneratedRegex(@"(?i)\b(liko|po)\s+\d+\s*(d\.|dien|val)")]
    private static partial Regex Countdown();
}
