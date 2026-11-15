using System.Net;
using Lt2.Api.Contracts;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;

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
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/darbotvarke"));
            html.ShouldContain("<h1>Seimo darbotvarkė</h1>");
            html.ShouldContain("Planuojama ");
            html.ShouldMatch("id=\"agenda-\\d{4}-\\d{2}-\\d{2}-" + AppealSteps.SecondBill + "\"");
            html.ShouldContain("Dar niekas nenurodė, ko prašo");
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
    public async Task The_front_page_shows_the_coming_items_first_and_no_empty_record_heading()
    {
        await Database.PlanAsync(AppealSteps.SecondBill, ReasonsToComeSteps.VilniusToday().AddDays(2));

        // No published record for the moment: earlier tests' records are hidden for the check and brought back after it.
        var open = await Database.ScalarAsync<string>("SELECT coalesce(string_agg(id::text, ','), '') FROM appeals.bill_record WHERE status = 'open'") ?? "";
        var closed = await Database.ScalarAsync<string>("SELECT coalesce(string_agg(id::text, ','), '') FROM appeals.bill_record WHERE status = 'closed'") ?? "";
        var published = open + closed;
        await Database.ExecuteAsync("UPDATE appeals.bill_record SET status = 'rejected' WHERE status IN ('open', 'closed')");
        try
        {
            using var visitor = NewBrowser();
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/"));
            html.ShouldContain("Artimiausiu metu Seime");
            html.ShouldMatch("id=\"agenda-\\d{4}-\\d{2}-\\d{2}-" + AppealSteps.SecondBill + "\"");
            html.ShouldContain("href=\"/darbotvarke\"");
            html.ShouldNotContain("id=\"home-appeals\"");
            html.ShouldContain("Tai nėra reprezentatyvi apklausa");
            html.IndexOf("Artimiausiu metu Seime", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("id=\"not-a-poll\"", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var (ids, status) in new[] { (open, "open"), (closed, "closed") }.Where(x => x.Item1.Length > 0))
            {
                await Database.ExecuteAsync(
                    "UPDATE appeals.bill_record SET status = @s WHERE id = ANY(string_to_array(@ids, ',')::uuid[])", ("s", status), ("ids", ids));
            }

            await Database.UnplanAsync(AppealSteps.SecondBill);
        }

        using var again = NewBrowser();
        if (published.Length > 0)
        {
            WebUtility.HtmlDecode(await again.Client.GetStringAsync("/")).ShouldContain("id=\"home-appeals\"");
        }
    }

    [Fact]
    public async Task The_agenda_is_first_in_the_menu()
    {
        using var visitor = NewBrowser();
        var html = await visitor.Client.GetStringAsync("/seimo-nariai");
        var nav = html[html.IndexOf("top__nav", StringComparison.Ordinal)..];
        nav.IndexOf("href=\"/darbotvarke\"", StringComparison.Ordinal).ShouldBeLessThan(nav.IndexOf("href=\"/seimo-nariai\"", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?i)\b(liko|po)\s+\d+\s*(d\.|dien|val)")]
    private static partial Regex Countdown();
}
