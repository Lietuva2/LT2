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

            // The page, without JavaScript: planned dates in words, never a countdown, and the not-a-poll notice.
            ForgetAgenda();
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
            html.ShouldContain("<h1>Seimo darbotvarkė</h1>");
            html.ShouldContain("Planuojama ");
            html.ShouldMatch("id=\"agenda-\\d{4}-\\d{2}-\\d{2}-" + AppealSteps.SecondBill + "\"");
            html.ShouldContain(">Niekas nenurodė, ko prašo<");
            html.ShouldNotContain("Dar niekas nenurodė");
            html.ShouldNotContain("Kreipimosi prašo");
            html.ShouldNotContain("/kreipimasis\"");
            html.ShouldContain("Tai nėra reprezentatyvi apklausa");
            Countdown().IsMatch(html).ShouldBeFalse("never a countdown");
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
            html.ShouldNotContain("Prašau balsuoti", customMessage: "the agenda page's rows link to the bill; no island");
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
    /// Owner, 2026-11-18: the page holds only the rows it shows. A long day shows ten and "Rodyti likusius", a link to the
    /// page with that day whole (<c>?diena=</c>) landing on the first row added; the bills the open data does not know yet
    /// come with the rest. Without JavaScript: the server renders the day whole.
    /// </summary>
    [Fact]
    public async Task A_long_day_shows_ten_rows_and_rodyti_likusius_fetches_the_rest_by_the_address()
    {
        var numbers = Enumerable.Range(1, 12).Select(i => $"XVP-L{i:000}").ToList();
        const string Unknown = "XVP-L099";
        var day = ReasonsToComeSteps.VilniusToday().AddDays(9);
        await RemoveLongDayAsync(numbers, Unknown);
        foreach (var number in numbers)
        {
            await Database.ExecuteAsync("INSERT INTO parliament.bill (id, number, title) VALUES (gen_random_uuid(), @n, @t)", ("n", number), ("t", "Ilgos dienos projektas " + number));
            await Database.PlanAsync(number, day, "presentation");
        }

        await Database.PlanAsync(Unknown, day, "presentation");
        try
        {
            ForgetAgenda();
            using var visitor = NewBrowser();
            var id = $"diena-{day:yyyy-MM-dd}";
            var cut = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke")), id);
            Regex.Count(cut, "<li class=\"tl-item\"").ShouldBe(AgendaRules.PagePerDay, "ten rows; the rest is not in the HTML");
            cut.ShouldContain($"<a href=\"/darbotvarke?diena={day:yyyy-MM-dd}#{id}-likusieji\" id=\"{id}-rodyti\">Rodyti likusius</a>");
            cut.ShouldNotContain(Unknown, customMessage: "the unknown bills come with the rest");
            cut.ShouldNotContain("Rodyti visus");

            var whole = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/darbotvarke?diena={day:yyyy-MM-dd}")), id);
            foreach (var number in numbers)
            {
                whole.ShouldContain($"id=\"agenda-{day:yyyy-MM-dd}-{number}\"");
            }

            whole.ShouldContain($"<ul class=\"tl-items\" id=\"{id}-likusieji\">", customMessage: "where the link lands");
            whole.IndexOf($"id=\"{id}-nezinomi\"", StringComparison.Ordinal).ShouldBeGreaterThan(whole.IndexOf($"id=\"{id}-likusieji\"", StringComparison.Ordinal), "the unknown bills last");
            whole.ShouldNotContain("Rodyti likusius");

            var malformed = DayBlock(WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke?diena=rytoj")), id);
            Regex.Count(malformed, "<li class=\"tl-item\"").ShouldBe(AgendaRules.PagePerDay, "a value the page would not make is the default");
        }
        finally
        {
            await RemoveLongDayAsync(numbers, Unknown);
            ForgetAgenda();
        }
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
    /// Owner, 2026-11-18: the front page shows the next two sitting days, five rows of each, and "Rodyti likusius" opens the
    /// rest of a longer day in place by the address; the bills the open data does not know yet are counted, and listed only
    /// in a day shown whole.
    /// </summary>
    [Fact]
    public async Task The_front_page_shows_five_rows_of_a_day_and_rodyti_likusius_opens_the_rest_in_place()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        var today = ReasonsToComeSteps.VilniusToday();
        var unknown = Enumerable.Range(1, 5).Select(i => $"XVP-98{i:00}").ToList();
        await Database.PlanAsync(AppealSteps.SecondBill, today);
        foreach (var number in unknown)
        {
            await Database.PlanAsync(number, today);
        }

        try
        {
            ForgetAgenda();
            using var visitor = NewBrowser();
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/"));
            var block = html[html.IndexOf($"id=\"home-day-{today:yyyy-MM-dd}\"", StringComparison.Ordinal)..];
            block = block[..block.IndexOf("</section>", StringComparison.Ordinal)];
            block.ShouldContain($"id=\"agenda-{today:yyyy-MM-dd}-{AppealSteps.SecondBill}\"");
            Regex.Count(block, "<li class=\"tl-item\"").ShouldBeLessThanOrEqualTo(5);
            foreach (var number in unknown)
            {
                block.ShouldNotContain(number, customMessage: "counted in the day, not listed on the front page");
            }

            // Owner, 2026-11-18: "Rodyti likusius" opens the rest of the day in place on the front page, by the address.
            block.ShouldMatch($"<a href=\"/\\?diena={today:yyyy-MM-dd}#home-day-{today:yyyy-MM-dd}-(likusieji|nezinomi)\" id=\"home-day-{today:yyyy-MM-dd}-rodyti\">Rodyti likusius</a>");
            block.ShouldNotContain("Visi ");
            Regex.Count(html, "<section class=\"tl-day\"").ShouldBeLessThanOrEqualTo(2, "the next two sitting days");
            html.ShouldContain("<a href=\"/darbotvarke\">", customMessage: "one link to the whole agenda, by the heading");

            var whole = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync($"/?diena={today:yyyy-MM-dd}"));
            whole = whole[whole.IndexOf($"id=\"home-day-{today:yyyy-MM-dd}\"", StringComparison.Ordinal)..];
            whole = whole[..whole.IndexOf("</section>", StringComparison.Ordinal)];
            whole.ShouldContain($"id=\"home-day-{today:yyyy-MM-dd}-nezinomi\"", customMessage: "the whole day, with the bills the open data does not know yet");
            foreach (var number in unknown)
            {
                whole.ShouldContain(number);
            }

            whole.ShouldNotContain("Rodyti likusius");
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
    public async Task The_agenda_shows_the_stage_votes_of_the_last_seven_days_only()
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
            html.ShouldContain("id=\"recent-" + ReasonsToComeSteps.TaxConsiderationVote + "\"");
            html.ShouldContain("href=\"/seimas/balsavimai/" + ReasonsToComeSteps.TaxConsiderationVote + "\"");
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
    public async Task The_first_menu_item_Projektai_opens_the_agenda()
    {
        // Design 8.21: "Projektai" is first; its dropdown's first tab is /darbotvarke (owner, 2026-11-17).
        using var visitor = NewBrowser();
        var html = await visitor.Client.GetStringAsync("/seimo-nariai");
        var nav = html[html.IndexOf("top__nav", StringComparison.Ordinal)..];
        nav.IndexOf("<summary class=\"drop__toggle\" id=\"nav-projects\">Projektai</summary>", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        nav.IndexOf("id=\"nav-projects\"", StringComparison.Ordinal).ShouldBeLessThan(nav.IndexOf("href=\"/darbotvarke\"", StringComparison.Ordinal));
        nav.IndexOf("href=\"/darbotvarke\"", StringComparison.Ordinal).ShouldBeLessThan(nav.IndexOf("href=\"/seimo-nariai\"", StringComparison.Ordinal));
    }

    /// <summary>The agenda page reads the front page's copy, kept a minute (owner, 2026-11-18): read afresh.</summary>
    private void ForgetAgenda() => Factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);

    [GeneratedRegex(@"(?i)\b(liko|po)\s+\d+\s*(d\.|dien|val)")]
    private static partial Regex Countdown();
}
