using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Parliament;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.13.1, the pages: a bill's readable title is the link of its row in the lists of bills, with the official title in
/// fine print under it (a bill without one shows the official title as the link); the bill's page has it as the heading with
/// "Oficialus pavadinimas: …" and the label of its source; the history page shows what changed in the title.
/// </summary>
public sealed partial class ReadableTitleTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.SecondBill;
    private const string Title = "Keičiama vietinių rinkliavų tvarka";
    private const string Other = "Savivaldybės galės nustatyti rinkliavas";
    private const string Text = "Projektas keičia vietinių rinkliavų tvarką.";

    private static async Task DescribeAsync(TestBrowser moderator, string? title, string text = Text, string statement = "Bandymas.")
    {
        using var described = await moderator.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(Bill), new ModeratorDescriptionRequest { Description = text, Statement = statement, PlainTitle = title });
        described.StatusCode.ShouldBe(HttpStatusCode.NoContent, await described.Content.ReadAsStringAsync());
    }

    private async Task<string> OfficialTitleAsync()
    {
        using var visitor = NewBrowser();
        var bill = await visitor.GetJsonAsync<BillResponse>(ParliamentRoutes.BillByNumber(Bill));
        return bill.Title;
    }

    private static async Task<string> PageAsync(TestBrowser browser, string path)
    {
        using var response = await browser.Client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static string RowOf(string html, string idMarker)
    {
        var start = html.IndexOf(idMarker, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, idMarker);
        var row = html[start..];
        return row[..row.IndexOf("</li>", StringComparison.Ordinal)];
    }

    private void ForgetFrontPage() => Factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);

    /// <summary>The title block of a row: the link and, when there is a readable title, the official one under it.</summary>
    private static void ShouldLeadWith(string row, string link, string? official, string href)
    {
        row.ShouldContain($"<a class=\"tl-item__title\" href=\"{href}\">{link}</a>");
        if (official is null)
        {
            row.ShouldNotContain("tl-item__official");
        }
        else
        {
            row.ShouldContain($"</a><span class=\"fine tl-item__official\">{official}</span>");
        }
    }

    [Fact]
    public async Task The_lists_of_bills_lead_with_the_readable_title_and_keep_the_official_one_under_it()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = (await ModeratorAsync()).Browser;
        var official = await OfficialTitleAsync();
        var today = ReasonsToComeSteps.VilniusToday();
        var day = today.AddDays(6);
        await Database.PlanAsync(Bill, day, "adoption");
        try
        {
            using var visitor = NewBrowser();
            var href = "/seimas/" + Bill;
            var listPath = $"/projektai?q={Bill}";
            var rowMarker = $"<li class=\"tl-item\" id=\"bill-{Bill}\"";
            var agendaMarker = $"id=\"agenda-{day:yyyy-MM-dd}-{Bill}\"";

            // A bill without a readable title: the official title is the link, as always.
            await DescribeAsync(moderator, null);
            ForgetFrontPage();
            ShouldLeadWith(RowOf(await PageAsync(visitor, listPath), rowMarker), official, null, href);
            ShouldLeadWith(RowOf(await PageAsync(visitor, "/darbotvarke"), agendaMarker), official, null, href);

            // With one: the readable title is the link, the official title in fine print under it, on the list, the agenda and the front page.
            await DescribeAsync(moderator, Title, Text + " Nuo 2027 metų.", "Pridėtas trumpas pavadinimas.");
            ForgetFrontPage();
            var list = await PageAsync(visitor, listPath);
            ShouldLeadWith(RowOf(list, rowMarker), Title, official, href);
            RowOf(list, rowMarker).ShouldContain($"<a class=\"tag tag--ink num\" href=\"{href}\">{Bill}</a>", customMessage: "the number's tag keeps its line above");
            ShouldLeadWith(RowOf(await PageAsync(visitor, "/darbotvarke"), agendaMarker), Title, official, href);
            ForgetFrontPage();
            ShouldLeadWith(RowOf(await PageAsync(visitor, "/"), agendaMarker), Title, official, href);

            // The answers carry it too: the agenda's item and the list's bill.
            var agenda = await visitor.GetJsonAsync<Lt2.Api.Contracts.Agenda.AgendaResponse>(Lt2.Api.Contracts.Agenda.AgendaRoutes.Agenda);
            agenda.Days.SelectMany(d => d.Items).Single(i => i.Number == Bill).PlainTitle.ShouldBe(Title);
            var listed = await visitor.GetJsonAsync<Lt2.Api.Contracts.Appeals.BillListResponse>(AppealRoutes.AppealsList(Bill, false, false, null));
            listed.Items.Single(i => i.Bill.Number == Bill).Bill.PlainTitle.ShouldBe(Title);

            // Removed with the description: the official title leads again.
            using (var removed = await moderator.PostAsync(AppealRoutes.RemoveDescriptionOf(Bill), new StatementRequest { Statement = "Pašalinta." }))
            {
                removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());
            }

            ShouldLeadWith(RowOf(await PageAsync(visitor, listPath), rowMarker), official, null, href);
        }
        finally
        {
            await Database.UnplanAsync(Bill);
            await Database.FreeBillAsync(Bill);
            ForgetFrontPage();
        }
    }

    /// <summary>Design 8.13.1: every list of votes names the bill by its readable title, the official one under it, and so does the vote's page.</summary>
    [Fact]
    public async Task The_lists_of_votes_lead_with_the_bills_readable_title()
    {
        const string Votes = AppealSteps.ThirdBill;
        await Database.FreeBillAsync(Votes);
        var moderator = (await ModeratorAsync()).Browser;
        try
        {
            using var visitor = NewBrowser();
            var jonas = ParliamentRoutes.MpBySlug("jonas-petraitis");
            var of = (Func<IEnumerable<VoteLink>, VoteLink[]>)(votes => [.. votes.Where(v => v.Bill?.Number == Votes)]);
            of((await visitor.GetJsonAsync<MpProfileResponse>(jonas)).RecentVotes.Select(v => v.Vote)).ShouldNotBeEmpty("the invented parliament's votes on the bill");

            using (var described = await moderator.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(Votes),
                       new ModeratorDescriptionRequest { Description = AppealSteps.Description, Statement = "Bandymas.", PlainTitle = Title }))
            {
                described.StatusCode.ShouldBe(HttpStatusCode.NoContent, await described.Content.ReadAsStringAsync());
            }

            // The history of votes, an MP's votes and profile, a faction's page, the bill's own votes: every vote of the bill names it so.
            var history = await visitor.GetJsonAsync<VoteListResponse>(ParliamentRoutes.Votes + "?q=" + Votes);
            var ofBill = history.Items.Select(i => i.Summary.Vote).Where(v => v.Bill?.Number == Votes).ToList();
            ofBill.ShouldNotBeEmpty();
            ofBill.ShouldAllBe(v => v.Bill!.PlainTitle == Title);
            history.Items.Select(i => i.Summary.Vote).Where(v => v.Bill is not null && v.Bill.Number != Votes).ShouldAllBe(v => v.Bill!.PlainTitle == null);
            var official = ofBill[0].Bill!.Title;
            of((await visitor.GetJsonAsync<MpProfileResponse>(jonas)).RecentVotes.Select(v => v.Vote)).ShouldAllBe(v => v.Bill!.PlainTitle == Title);
            of((await visitor.GetJsonAsync<MpVotesResponse>(ParliamentRoutes.MpVotesBySlug("jonas-petraitis"))).Items.Select(v => v.Vote)).ShouldAllBe(v => v.Bill!.PlainTitle == Title);
            of((await visitor.GetJsonAsync<BodyResponse>(ParliamentRoutes.FactionBySlug("reformu-partijos-frakcija"))).RecentVotes.Select(v => v.Vote)).ShouldAllBe(v => v.Bill!.PlainTitle == Title);
            (await visitor.GetJsonAsync<BillResponse>(ParliamentRoutes.BillByNumber(Votes))).Votes.ShouldAllBe(v => v.Vote.Bill!.PlainTitle == Title);

            // The pages: the vote's page is where the readable title leads, with the official title under it.
            var voteId = ofBill[0].Id;
            var link = $"<a class=\"tl-item__title\" href=\"/seimas/balsavimai/{voteId}\">{Title}</a><span class=\"fine tl-item__official\">{official}</span>";
            (await PageAsync(visitor, "/seimas/balsavimai?q=" + Votes)).ShouldContain(link);
            (await PageAsync(visitor, "/p/jonas-petraitis")).ShouldContain(link);

            // The vote's own page and its answer: the readable title is the heading, the official title follows it.
            (await visitor.GetJsonAsync<VoteResponse>(ParliamentRoutes.VoteById(voteId))).Summary.Vote.Bill!.PlainTitle.ShouldBe(Title);
            var votePage = await PageAsync(visitor, $"/seimas/balsavimai/{voteId}");
            votePage.ShouldContain($"<h1>{Title}</h1>");
            votePage.ShouldContain($"id=\"official-title\">Oficialus pavadinimas: {official}</p>");
            votePage.ShouldContain($"<meta property=\"og:title\" content=\"Balsavimas · {Title}\"", customMessage: "the readable title goes into the link preview");

            // A vote without a bill keeps its wording; a bill without a readable title keeps its official one (VoteRow).
            await Database.FreeBillAsync(Votes);
            (await PageAsync(visitor, "/p/jonas-petraitis")).ShouldNotContain("tl-item__official");
            var bare = await PageAsync(visitor, $"/seimas/balsavimai/{voteId}");
            bare.ShouldNotContain("id=\"official-title\"");
            bare.ShouldNotContain($"<h1>{Title}</h1>");
        }
        finally
        {
            await Database.FreeBillAsync(Votes);
        }
    }

    [Fact]
    public async Task A_followed_bill_carries_its_readable_title()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = (await ModeratorAsync()).Browser;
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        try
        {
            await member.FollowBillAsync(Bill);
            (await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.Single(b => b.Number == Bill).PlainTitle.ShouldBeNull();

            await DescribeAsync(moderator, Title);
            var followed = (await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.Single(b => b.Number == Bill);
            followed.PlainTitle.ShouldBe(Title);
            followed.Title.ShouldBe(await OfficialTitleAsync(), "the official title stays the Parliament module's");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
            await member.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task The_bill_page_has_the_readable_title_as_its_heading_then_the_official_title_and_the_label_of_its_source()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = (await ModeratorAsync()).Browser;
        var official = await OfficialTitleAsync();
        try
        {
            using var visitor = NewBrowser();

            // Without a readable title: the official title is the heading, as today.
            await DescribeAsync(moderator, null);
            var plain = await PageAsync(visitor, "/seimas/" + Bill);
            plain.ShouldContain($"<h1>{official}</h1>");
            plain.ShouldNotContain("id=\"official-title\"");
            plain.ShouldNotContain("id=\"title-source\"");
            plain.ShouldContain($"<title>{Bill} · {official} · Lietuva 2.0</title>");

            // A moderator's title: the heading, the official title, and the plain label of who wrote it.
            await DescribeAsync(moderator, Title, Text + " Nuo 2027 metų.", "Pridėtas trumpas pavadinimas.");
            var page = await PageAsync(visitor, "/seimas/" + Bill);
            page.ShouldContain($"<h1>{Title}</h1>");
            page.ShouldContain($"<title>{Bill} · {Title} · Lietuva 2.0</title>");
            Regex.Match(page, "<p class=\"small bill-official\" id=\"official-title\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value
                .ShouldBe($"Oficialus pavadinimas: {official}");
            var source = Regex.Match(page, "<p class=\"fine\" id=\"title-source\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value.Trim();
            source.ShouldBe("Parašė moderatorius");
            page.IndexOf("id=\"official-title\"", StringComparison.Ordinal).ShouldBeGreaterThan(page.IndexOf("</h1>", StringComparison.Ordinal));
            page.IndexOf("id=\"title-source\"", StringComparison.Ordinal).ShouldBeGreaterThan(page.IndexOf("id=\"official-title\"", StringComparison.Ordinal));

            // An AI's title, and one a moderator checked, carry the tag of the description's label.
            foreach (var (value, label) in new[] { ("ai", "Sukurta DI · žmonių neperžiūrėta"), ("ai-draft", "Sukurta DI · taisė žmonės") })
            {
                await Database.ExecuteAsync("UPDATE appeals.bill_record SET description_source = @s WHERE bill_number = @n", ("s", value), ("n", Bill));
                var labelled = await PageAsync(visitor, "/seimas/" + Bill);
                labelled.ShouldContain($"<h1>{Title}</h1>", customMessage: value);
                Regex.Match(labelled, "<p class=\"fine\" id=\"title-source\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value
                    .ShouldContain($"<span class=\"tag\">{label}</span>", customMessage: value);
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task The_history_page_leads_with_the_readable_title_and_shows_what_changed_in_it()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = (await ModeratorAsync()).Browser;
        var official = await OfficialTitleAsync();
        try
        {
            using var visitor = NewBrowser();
            await DescribeAsync(moderator, null);
            (await PageAsync(visitor, $"/seimas/{Bill}/istorija")).ShouldContain($"<p class=\"lead\">{official}</p>");

            await DescribeAsync(moderator, Title, Text, "Pridėtas trumpas pavadinimas.");
            await DescribeAsync(moderator, Other, Text, "Tik pavadinimas.");
            await DescribeAsync(moderator, Other, Text + " Nuo 2027 metų.", "Tik tekstas.");
            await DescribeAsync(moderator, null, Text + " Nuo 2028 metų.", "Pavadinimas pašalintas, tekstas pakeistas.");

            var html = await PageAsync(visitor, $"/seimas/{Bill}/istorija");
            html.ShouldContain($"<p class=\"lead\">{official}</p>", customMessage: "the readable title was removed in the last edit");
            var edits = Regex.Match(html, "<ol class=\"rows\">(.*)</ol>", RegexOptions.Singleline).Groups[1].Value
                .Split("<li>", StringSplitOptions.RemoveEmptyEntries).Select(e => e.Split("</li>")[0]).ToList();
            edits.Count.ShouldBe(5, "the first description, then four edits, newest first");

            // Newest first: title removed with the text changed; text only; title only; title and text first written.
            edits[0].ShouldContain("Trumpas pavadinimas");
            edits[0].ShouldContain("Aprašymas", customMessage: "both changed: each under its label");
            edits[0].ShouldContain("pašalinta:", customMessage: "the removed title is struck through");
            edits[1].ShouldNotContain("Trumpas pavadinimas");
            edits[1].ShouldNotContain("<span class=\"label\">Aprašymas</span>", customMessage: "a text alone needs no label");
            edits[1].ShouldContain("<span class=\"label\">Paaiškinimas</span>", customMessage: "the moderator's public statement has its own (design 8.13)");
            edits[1].ShouldContain("Tik tekstas.");
            edits[2].ShouldContain("Trumpas pavadinimas");
            edits[2].ShouldNotContain("Aprašymas");
            edits[2].ShouldContain("pridėta:");
            edits[2].ShouldContain("pašalinta:");
            edits[3].ShouldContain("Trumpas pavadinimas");
            edits[3].ShouldContain(Title, customMessage: "the first title is shown whole");
            edits[3].ShouldNotContain("Aprašymas");
            edits[4].ShouldNotContain("Trumpas pavadinimas", customMessage: "the first description had no title");
            edits[4].ShouldContain(Text);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_hidden_record_shows_no_readable_title_on_its_page()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = (await ModeratorAsync()).Browser;
        try
        {
            await DescribeAsync(moderator, Title);
            var record = await moderator.RecordAsync(Bill);
            record.PlainTitle.ShouldBe(Title);

            // A record the moderators hid has no readable title on its page.
            (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideRecord, record.Id!.Value), new StatementRequest { Statement = "Bandymas: paslėpta." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using var visitor = NewBrowser();
            var page = await PageAsync(visitor, "/seimas/" + Bill);
            page.ShouldNotContain("id=\"official-title\"");
            page.ShouldNotContain($"<h1>{Title}</h1>");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
