using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Client;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.12.5 (owner, 2026-11-23): a row of <c>/projektai</c> is the agenda's and the front page's (TimelineItem). The
/// answer behind it carries what that row needs (the stage, the record's public numbers, the organisations with a public
/// statement); a visitor's row offers "Prašau balsuoti" as links to sign in, a closed record's row offers no sides, a member's
/// row carries the islands of their own mark and side column, and the served page is the same for every member and holds nothing
/// of anyone's own.
/// </summary>
public sealed class AppealListRowTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Open = AppealSteps.ThirdBill;
    private const string Empty = AppealSteps.SecondBill;
    private const string Source = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas";

    /// <summary>One row of the list's page, by the bill's number.</summary>
    private static string Row(string html, string number)
    {
        var row = html[html.IndexOf($"<li class=\"tl-item\" id=\"bill-{number}\"", StringComparison.Ordinal)..];
        return row[..row.IndexOf("</li>", StringComparison.Ordinal)];
    }

    private static async Task<string> PageAsync(TestBrowser browser, string address)
    {
        using var response = await browser.Client.GetAsync(address, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, address);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_list_answer_carries_each_bills_stage_record_and_organisations_and_stays_public()
    {
        await Database.FreeBillAsync(Empty);
        await Database.ClearStatementsAsync(Empty);
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var (rep, _) = await Factory.MemberAsync();
        using var _r = rep;
        var record = await Database.PrepareBillAsync(author, moderator, Open, "for");
        var today = ReasonsToComeSteps.VilniusToday();
        await Database.PlanAsync(Empty, today.AddDays(5), "adoption");
        try
        {
            var organisation = await rep.RegisterOrganisationAsync();
            await rep.PublishStatementAsync(Empty, organisation.Slug, "against", "Pozicija sąrašo eilutei (bandymas).");

            using var visitor = NewBrowser();
            var open = await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, limit: 200));

            // A bill nobody took part in: no record, open, zeros; its planned item's stage; and the organisation with a public statement.
            var empty = open.Items.Single(i => i.Bill.Number == Empty);
            (empty.Id, empty.Status).ShouldBe(((Guid?)null, "open"));
            empty.Planned.ShouldNotBeNull().Stage.ShouldBe("adoption");
            empty.Counts.ShouldBe(new SideCountsResponse(0, 0, 0, 0));
            var listed = empty.Organisations.ShouldHaveSingleItem();
            (listed.Slug, listed.Stance).ShouldBe((organisation.Slug, "against"));
            listed.Name.ShouldNotBeNullOrEmpty();
            listed.Label.ShouldNotBeNull();

            // A bill with an open record: its public numbers, no organisations, no stage while nothing is planned.
            var withRecord = open.Items.Single(i => i.Bill.Number == Open);
            (withRecord.Id, withRecord.Status, withRecord.Counts.For).ShouldBe((record.Id, "open", 1));
            withRecord.Organisations.ShouldBeEmpty();

            // The statement is withdrawn: the row names no organisation any more.
            await Database.ClearStatementsAsync(Empty);
            (await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, limit: 200)))
                .Items.Single(i => i.Bill.Number == Empty).Organisations.ShouldBeEmpty();

            // A closed record, on the closed tab: closed, its frozen numbers, and still the organisations with a statement on it.
            var otherRep = (await Factory.MemberAsync()).Browser;
            using var _o = otherRep;
            var other = await otherRep.RegisterOrganisationAsync();
            await otherRep.PublishStatementAsync(Open, other.Slug, "for", "Pozicija uždaromam projektui (bandymas).");
            using (var closing = await moderator.PostAsync(
                AppealRoutes.With(AppealRoutes.CloseRecord, record.Id),
                new CloseRecordRequest { Reason = "withdrawn", DecidedOn = today.AddDays(-1), SourceUrl = Source, Statement = "Iniciatoriai atsiėmė." }))
            {
                closing.StatusCode.ShouldBe(HttpStatusCode.NoContent, await closing.Content.ReadAsStringAsync());
            }

            var closed = (await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, true, null, limit: 200))).Items.Single(i => i.Bill.Number == Open);
            (closed.Id, closed.Status, closed.Counts.For).ShouldBe((record.Id, "closed", 1));
            closed.Organisations.Select(o => (o.Slug, o.Stance)).ShouldBe([(other.Slug, "for")]);
            (await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, limit: 200))).Items.ShouldNotContain(i => i.Bill.Number == Open, "a closed record is not on the active tab");

            // Public and the same for everyone, with its validator.
            var (asker, _) = await Factory.MemberAsync();
            using var _s = asker;
            using var forVisitor = await visitor.Client.GetAsync(AppealRoutes.AppealsList(null, false, true, null, limit: 200), TestContext.Current.CancellationToken);
            using var forMember = await asker.Client.GetAsync(AppealRoutes.AppealsList(null, false, true, null, limit: 200), TestContext.Current.CancellationToken);
            (await forMember.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(await forVisitor.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            forVisitor.Headers.CacheControl!.Public.ShouldBeTrue();
            forVisitor.Headers.ETag.ShouldNotBeNull();
        }
        finally
        {
            await Database.ClearStatementsAsync(Empty);
            await Database.ClearStatementsAsync(Open);
            await Database.UnplanAsync(Empty);
            await Database.FreeBillAsync(Open);
        }
    }

    [Fact]
    public async Task A_visitor_sees_the_sign_in_sides_on_an_open_row_the_stage_and_the_organisations_and_none_on_a_closed_row()
    {
        await Database.FreeBillAsync(Empty);
        await Database.ClearStatementsAsync(Empty);
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var (rep, _) = await Factory.MemberAsync();
        using var _r = rep;
        var record = await Database.PrepareBillAsync(author, moderator, Open, "for");
        await Database.PlanAsync(Empty, ReasonsToComeSteps.VilniusToday().AddDays(4), "adoption");
        try
        {
            var organisation = await rep.RegisterOrganisationAsync();
            await rep.PublishStatementAsync(Empty, organisation.Slug, "for", "Pozicija sąrašo eilutei (bandymas).");

            using var visitor = NewBrowser();
            var html = await PageAsync(visitor, "/projektai?q=" + Uri.EscapeDataString(Empty));
            var row = Row(html, Empty);
            row.ShouldContain($"<a class=\"tag tag--ink num\" href=\"/seimas/{Empty}\">{Empty}</a>");
            row.ShouldContain("<span class=\"tag\">Priėmimas</span>", customMessage: "the stage tag of the planned item");
            row.ShouldContain("<span class=\"tag\">Planuojama ", customMessage: "the planned date is a tag, the list has no day bands");
            row.ShouldContain($"id=\"agenda-organisations-{Empty}\"");
            row.ShouldContain($"href=\"/o/{organisation.Slug}\"");
            row.ShouldContain("Prašau balsuoti");
            row.ShouldContain($"<a class=\"btn btn--for cta\" id=\"home-for-{Empty}\" href=\"/prisijungti#tikslas=%2Fseimas%2F{Empty}\"");
            row.ShouldContain($"<a class=\"btn btn--against cta\" id=\"home-against-{Empty}\" href=\"/prisijungti#tikslas=%2Fseimas%2F{Empty}\"");
            row.ShouldContain("dar niekas nedalyvauja");
            html.ShouldNotContain("id=\"home-side-", customMessage: "a visitor's row has no island: its sides are links");
            html.ShouldNotContain("id=\"home-mine-");
            html.ShouldNotContain("id=\"needs-script\"");

            var openRow = Row(await PageAsync(visitor, "/projektai?q=" + Uri.EscapeDataString(Open)), Open);
            openRow.ShouldContain($"id=\"home-counts-{Open}\"", customMessage: "the agenda's figures: a number a side and the bar");
            openRow.ShouldContain($"id=\"home-for-{Open}\"");
            openRow.ShouldContain("<span class=\"label\">Dalyviai</span>", customMessage: "every block of numbers says whose it is, on an open row too");

            // The record is closed: its row, on the closed tab, has the figures and the way to the bill, and no side to take.
            using (var closing = await moderator.PostAsync(
                AppealRoutes.With(AppealRoutes.CloseRecord, record.Id),
                new CloseRecordRequest { Reason = "rejected", DecidedOn = ReasonsToComeSteps.VilniusToday().AddDays(-1), SourceUrl = Source, Statement = "Seimas projektą atmetė." }))
            {
                closing.StatusCode.ShouldBe(HttpStatusCode.NoContent, await closing.Content.ReadAsStringAsync());
            }

            var closed = Row(await PageAsync(visitor, "/projektai?skirtukas=uzdaryti&q=" + Uri.EscapeDataString(Open)), Open);
            closed.ShouldContain($"id=\"home-counts-{Open}\"");
            closed.ShouldNotContain("Į projekto puslapį", customMessage: "the title and the number tag lead to the bill (owner, 2026-11-23)");
            closed.ShouldContain($"href=\"/seimas/{Open}\">{Open}</a>");
            closed.ShouldContain("<span class=\"label\">Dalyviai</span>", customMessage: "the closed row's figures say whose numbers they are");
            closed.ShouldNotContain("Prašau balsuoti");
            closed.ShouldNotContain("class=\"btn btn--for");
            closed.ShouldNotContain($"id=\"home-for-{Open}\"");

            // A member's closed row has no island for the side column either; the mark under the title is the only one.
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            var own = Row(await PageAsync(member, "/projektai?skirtukas=uzdaryti&q=" + Uri.EscapeDataString(Open)), Open);
            own.ShouldNotContain($"id=\"home-side-{Open}\"");
            own.ShouldNotContain("Prašau balsuoti");
        }
        finally
        {
            await Database.ClearStatementsAsync(Empty);
            await Database.UnplanAsync(Empty);
            await Database.FreeBillAsync(Open);
        }
    }

    [Fact]
    public async Task The_list_is_the_same_for_two_members_holds_neither_ones_own_and_carries_the_islands_only_for_a_member()
    {
        await Database.FreeBillAsync(Empty);
        var (first, firstEmail) = await Factory.MemberAsync("Pirmoji Sąrašo");
        using var _f = first;
        var (second, secondEmail) = await Factory.MemberAsync("Antrasis Sąrašo");
        using var _s = second;
        try
        {
            (await first.TakeSideAsync(Empty, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await first.FollowBillAsync(Empty);

            var address = "/projektai?q=" + Uri.EscapeDataString(Empty);
            var firstPage = await first.Client.GetStringAsync(address, TestContext.Current.CancellationToken);
            var secondPage = await second.Client.GetStringAsync(address, TestContext.Current.CancellationToken);

            // The proof that the check reads the right page: the member's version, with the row's islands and their places.
            firstPage.ShouldContain($"<li class=\"tl-item\" id=\"bill-{Empty}\"");
            firstPage.ShouldContain($"id=\"home-side-{Empty}\"", customMessage: "the side column is an island for a member");
            firstPage.ShouldMatch($"<button type=\"button\" class=\"btn btn--for cta\" id=\"home-for-{Empty}\" aria-pressed=\"false\"[^>]* disabled");
            firstPage.ShouldContain("id=\"needs-script\"", customMessage: "the served page says JavaScript is needed");
            firstPage.ShouldNotContain($"<a class=\"btn btn--for cta\" id=\"home-for-{Empty}\"", customMessage: "a member's sides are buttons, not links to sign in");
            FrontPageTests.Frameless(firstPage).ShouldBe(FrontPageTests.Frameless(secondPage), "the same page for every member, apart from the frame");

            // A visitor's page has neither the islands nor the note.
            using var visitor = NewBrowser();
            var visitorPage = await PageAsync(visitor, address);
            visitorPage.ShouldNotContain("id=\"home-side-");
            visitorPage.ShouldNotContain("id=\"needs-script\"");

            foreach (var page in new[] { firstPage, secondPage })
            {
                var text = WebUtility.HtmlDecode(page);
                foreach (var own in new[] { firstEmail, secondEmail, "Pirmoji Sąrašo", "Antrasis Sąrašo" })
                {
                    text.ShouldNotContain(own, Case.Insensitive);
                }

                text.ShouldNotContain("aria-pressed=\"true\"");
                text.ShouldNotContain("Sekate", customMessage: "the member's mark is filled in the browser only");
                text.ShouldNotContain("id=\"home-mine-");
                text.ShouldNotContain("Jūs prašote balsuoti");
                var state = FrontPageTests.StateOf(page);
                state.ShouldNotContain(firstEmail, Case.Insensitive);
                state.ShouldNotContain(secondEmail, Case.Insensitive);
                state.ShouldNotContain("Sąrašo", Case.Insensitive);
            }

            // A row added by "Rodyti daugiau" starts its islands in place: the grown page carries one for every row shown.
            var grown = await first.Client.GetStringAsync("/projektai?rodyti=70", TestContext.Current.CancellationToken);
            Regex.Count(grown, "id=\"home-side-").ShouldBe(Regex.Count(grown, "<li class=\"tl-item\" id=\"bill-"), "every open row is an island, none is left a link");
        }
        finally
        {
            await Database.FreeBillAsync(Empty);
        }
    }

    /// <summary>
    /// Owner, 2026-11-23: a search says what it found in the other view, as one line under the count ("Uždarytuose rasta: N →" on
    /// "Aktyvūs", "Aktyviuose rasta: N →" on "Uždaryti") linking to the same search there; only with a query and only when the
    /// other view has a match, never carrying the order or the filter; the page stays public and the same for everyone.
    /// </summary>
    [Fact]
    public async Task A_search_names_what_it_found_in_the_other_view()
    {
        await Database.FreeBillAsync(Empty);
        await Database.FreeBillAsync(Open);
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, moderator, Open, "for");
        try
        {
            using (var closing = await moderator.PostAsync(
                AppealRoutes.With(AppealRoutes.CloseRecord, record.Id),
                new CloseRecordRequest { Reason = "withdrawn", DecidedOn = ReasonsToComeSteps.VilniusToday().AddDays(-1), SourceUrl = Source, Statement = "Iniciatoriai atsiėmė." }))
            {
                closing.StatusCode.ShouldBe(HttpStatusCode.NoContent, await closing.Content.ReadAsStringAsync());
            }

            using var visitor = NewBrowser();
            async Task<int> Total(bool closed) =>
                (await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList("XVP", false, closed, null, limit: 1))).Total;
            var active = await Total(false);
            var closed = await Total(true);
            active.ShouldBeGreaterThan(0, "an active bill matches the query");
            closed.ShouldBeGreaterThan(0, "the closed bill matches the query");

            // A query matching both: each view links to the other with the other's count, the query escaped as the list's links do.
            var onActive = await PageAsync(visitor, "/projektai?q=XVP&rikiuoti=data&planuojami=taip");
            onActive.ShouldContain($"<p class=\"small num\" id=\"other-closed\"><a href=\"/projektai?skirtukas=uzdaryti&q=XVP\">Uždarytuose rasta: {Display.Number(closed)} →</a></p>");
            onActive.ShouldNotContain("id=\"other-active\"");
            onActive.IndexOf("id=\"found\"", StringComparison.Ordinal).ShouldBeLessThan(onActive.IndexOf("id=\"other-closed\"", StringComparison.Ordinal), "under the count line");
            var onClosed = await PageAsync(visitor, "/projektai?skirtukas=uzdaryti&q=XVP");
            onClosed.ShouldContain($"<p class=\"small num\" id=\"other-active\"><a href=\"/projektai?q=XVP\">Aktyviuose rasta: {Display.Number(active)} →</a></p>");
            onClosed.ShouldNotContain("id=\"other-closed\"");

            // The closed view has no match for the query, so it is not offered there; the order and the filter of the active view are not carried to the link.
            (await PageAsync(visitor, "/projektai?skirtukas=uzdaryti&q=" + Uri.EscapeDataString("XVP-0618")))
                .ShouldNotContain("id=\"other-closed\"");
            var withOrder = await PageAsync(visitor, "/projektai?q=XVP-0618&rikiuoti=data&planuojami=taip");
            withOrder.ShouldContain("href=\"/projektai?skirtukas=uzdaryti&q=XVP-0618\"");

            // No query (or a blank one): no line on either view.
            foreach (var address in new[] { "/projektai", "/projektai?skirtukas=uzdaryti", "/projektai?q=", "/projektai?skirtukas=uzdaryti&q=%20" })
            {
                var plain = await PageAsync(visitor, address);
                plain.ShouldNotContain("id=\"other-closed\"", customMessage: address);
                plain.ShouldNotContain("id=\"other-active\"", customMessage: address);
            }

            // Nothing in the other view: no line. XVP-0533 is active only; the closed bill is XVP-0618.
            (await PageAsync(visitor, "/projektai?q=" + Empty)).ShouldNotContain("id=\"other-closed\"");
            (await PageAsync(visitor, "/projektai?skirtukas=uzdaryti&q=" + Open)).ShouldNotContain("id=\"other-active\"");
            (await PageAsync(visitor, "/projektai?q=" + Empty)).ShouldNotContain("id=\"other-active\"");

            // The count is the closed list's for that query: a query only the closed bill matches says 1 on "Aktyvūs".
            var closedOnly = await PageAsync(visitor, "/projektai?q=" + Open);
            closedOnly.ShouldContain("id=\"other-closed\"");
            closedOnly.ShouldContain("Uždarytuose rasta: 1 →");
            (await PageAsync(visitor, "/projektai?skirtukas=uzdaryti&q=" + Empty)).ShouldContain("Aktyviuose rasta: 1 →");

            // Public and the same for everyone: a member's page holds the same line.
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            (await PageAsync(member, "/projektai?q=" + Open)).ShouldContain("Uždarytuose rasta: 1 →");
        }
        finally
        {
            await Database.FreeBillAsync(Open);
        }
    }
}
