using System.Net;
using System.Text.RegularExpressions;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.23.6, 8.12.1 (the designer's proposal of 2026-11-17): a link to a comment (<c>?komentaras=</c>) or an argument
/// (<c>?argumentas=</c>) lands on it in the served HTML, without script: the argument shown even beyond the first two of its
/// side, its "Aptarimas" rendered open with the comments through the linked one on the thread's own pages (10, then 20 a
/// step), the linked one
/// marked; a hidden, unknown or garbled one changes nothing and reveals nothing.
/// </summary>
public sealed partial class CommentLinkTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    /// <summary>
    /// A bill with three published "for" arguments, and 32 comments by four members under the one the page shows only after
    /// "show all" (the third of its side). The argument's id and the comments' ids, oldest first.
    /// </summary>
    private async Task<(Guid Reason, IReadOnlyList<Guid> Comments)> PrepareAsync()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (first, _) = await Factory.MemberAsync("Pirmoji Rašytoja");
        using var _f = first;
        await Database.PrepareBillAsync(first, moderator, Bill, "for");
        foreach (var (name, text) in new[] { ("Antrasis Rašytojas", "Antras argumentas už: mažesnė našta dirbantiems."), ("Trečioji Rašytoja", "Trečias argumentas už: paprastesnė deklaracija.") })
        {
            var (writer, _) = await Factory.MemberAsync(name);
            using var _w = writer;
            using var written = await writer.WriteReasonAsync(Bill, "for", text);
            written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
        }

        using var visitor = NewBrowser();
        var forSide = (await visitor.AllReasonsAsync(Bill)).Where(r => r.Side == "for").ToList();
        forSide.Count.ShouldBe(3, "three published arguments on one side");
        var third = forSide[2].Id;

        var comments = new List<Guid>();
        for (var m = 0; m < 4; m++)
        {
            var (member, _) = await Factory.MemberAsync("Komentuotojas " + (m + 1));
            using var _m = member;
            for (var i = 0; i < 8; i++)
            {
                comments.Add(await member.CommentOkAsync("reason", third, $"Komentaras nr. {comments.Count + 1} apie trečią argumentą."));
            }
        }

        return (third, comments);
    }

    private async Task<string> PageAsync(string address)
    {
        using var visitor = NewBrowser();
        using var page = await visitor.Client.GetAsync(address, TestContext.Current.CancellationToken);
        page.StatusCode.ShouldBe(HttpStatusCode.OK, address);
        return WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static bool ThreadOpen(string html, Guid reason) =>
        Regex.IsMatch(html, $"<details[^>]*id=\"discussion-{reason:N}\"[^>]*\\sopen[\\s>=]");

    [Fact]
    public async Task A_link_to_the_25th_comment_of_an_argument_beyond_the_first_two_shows_the_argument_its_open_thread_through_30_and_marks_it()
    {
        try
        {
            var (reason, comments) = await PrepareAsync();
            var linked = comments[24];

            // Without the link the argument waits for "Rodyti daugiau argumentų" and its comments are not in the page: the checks below can fail.
            var plain = await PageAsync("/seimas/" + Bill);
            plain.ShouldNotContain($"id=\"{BillTextLinks.ReasonElement(reason)}\"", Case.Sensitive);
            plain.ShouldNotContain(CommentLimits.ElementId(linked), Case.Sensitive);

            using var visitor = NewBrowser();
            var place = await visitor.GetJsonAsync<CommentPlaceResponse>(CommentRoutes.PlaceOf(linked));
            place.ShouldBe(new CommentPlaceResponse(linked, "reason", reason, Bill, 25));

            var address = BillTextLinks.ToComment(Bill, linked);
            address.ShouldBe($"/seimas/{Bill}?komentaras={linked:N}#comment-{linked:N}");
            var html = await PageAsync(address.Split('#')[0]);
            html.ShouldContain($"id=\"{BillTextLinks.ReasonElement(reason)}\"", Case.Sensitive, "the argument is shown though it is the third of its side");
            html.IndexOf($"id=\"{BillTextLinks.ReasonElement(reason)}\"", StringComparison.Ordinal)
                .ShouldBeLessThan(html.IndexOf("Antras argumentas už", StringComparison.Ordinal), "owner, 2026-11-19: first in its side's column");
            ThreadOpen(html, reason).ShouldBeTrue("its \"Aptarimas\" is rendered open");
            for (var i = 0; i < comments.Count; i++)
            {
                if (i < 30)
                {
                    html.ShouldContain($"id=\"{CommentLimits.ElementId(comments[i])}\"", Case.Sensitive, $"comment {i + 1}: the whole discussion through the linked one, rounded up to 30");
                }
                else
                {
                    html.ShouldNotContain($"id=\"{CommentLimits.ElementId(comments[i])}\"", Case.Sensitive, $"comment {i + 1} waits for \"Rodyti daugiau komentarų\"");
                }
            }

            Regex.Count(html, "comment--linked").ShouldBe(1);
            html.ShouldContain($"class=\"comment stack stack--tight comment--linked\" id=\"{CommentLimits.ElementId(linked)}\"", Case.Sensitive);
            html.ShouldContain($"komentarai={reason:N}-32", Case.Sensitive, "then the usual \"Rodyti daugiau komentarų\"");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    /// <summary>
    /// Owner, 2026-11-19: a discussion comes a page at a time (<c>?limit=&amp;cursor=</c>, as the other lists of the API): the first
    /// page at least ten, then from where the shown ones end, with the next page's cursor until the last; the island's "Rodyti
    /// daugiau komentarų" reads it so.
    /// </summary>
    [Fact]
    public async Task A_discussion_comes_a_page_at_a_time_by_limit_and_cursor()
    {
        try
        {
            var (reason, comments) = await PrepareAsync();
            using var visitor = NewBrowser();

            var first = await visitor.GetJsonAsync<CommentThreadResponse>(CommentRoutes.ThreadOf("reason", reason));
            (first.Count, first.Items.Count, first.NextCursor).ShouldBe((32, CommentLimits.First, "10"));
            first.Items.Select(i => i.Id).ShouldBe(comments.Take(10));
            (await visitor.GetJsonAsync<CommentThreadResponse>(CommentRoutes.ThreadOf("reason", reason, 3))).Items.Count.ShouldBe(CommentLimits.First, "the first page never fewer than ten");

            var next = await visitor.GetJsonAsync<CommentThreadResponse>(CommentRoutes.ThreadOf("reason", reason, CommentLimits.Step, first.NextCursor));
            (next.Count, next.NextCursor).ShouldBe((32, "30"), "owner, 2026-11-19: 20 more a press");
            next.Items.Select(i => i.Id).ShouldBe(comments.Skip(10).Take(20));
            (await visitor.GetJsonAsync<CommentThreadResponse>(CommentRoutes.ThreadOf("reason", reason, null, "10"))).Items.Select(i => i.Id)
                .ShouldBe(comments.Skip(10).Take(20), "a later page without a limit is one step");
            var last = await visitor.GetJsonAsync<CommentThreadResponse>(CommentRoutes.ThreadOf("reason", reason, 5, "30"));
            last.Items.Select(i => i.Id).ShouldBe(comments.Skip(30));
            last.NextCursor.ShouldBeNull("the last page");
            (await visitor.GetJsonAsync<CommentThreadResponse>(CommentRoutes.ThreadOf("reason", reason, null, "x"))).Items.Select(i => i.Id).ShouldBe(comments.Take(10), "a cursor the API would not make starts at the first");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_link_to_a_hidden_unknown_or_garbled_comment_renders_the_page_as_usual_and_reveals_nothing()
    {
        try
        {
            var (reason, comments) = await PrepareAsync();
            var hidden = comments[24];
            var moderator = (await ModeratorAsync()).Browser;
            await moderator.ActOnCommentAsync(CommentRoutes.Hide, hidden);

            using var visitor = NewBrowser();
            using (var gone = await visitor.Client.GetAsync(CommentRoutes.PlaceOf(hidden), TestContext.Current.CancellationToken))
            {
                await gone.ShouldBeProblemAsync(HttpStatusCode.NotFound, "comment.not-found");
            }

            using (var unknown = await visitor.Client.GetAsync(CommentRoutes.PlaceOf(Guid.NewGuid()), TestContext.Current.CancellationToken))
            {
                await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "comment.not-found");
            }

            foreach (var address in new[] { BillTextLinks.ToComment(Bill, hidden), BillTextLinks.ToComment(Bill, Guid.NewGuid()), $"/seimas/{Bill}?komentaras=ne-id" })
            {
                var html = await PageAsync(address.Split('#')[0]);
                html.ShouldContain("Antras argumentas už", Case.Sensitive, $"{address}: the page renders as usual");
                html.ShouldNotContain("comment--linked", Case.Sensitive, address);
                html.ShouldNotContain($"id=\"{BillTextLinks.ReasonElement(reason)}\"", Case.Sensitive, $"{address}: nothing reveals where a hidden comment stood");
                html.ShouldNotContain("Komentaras nr. 25 ", Case.Sensitive, address);
            }

            // A shown comment of another bill does not open anything here.
            var elsewhere = await PageAsync(BillTextLinks.ToComment(AppealSteps.SecondBill, comments[3]).Split('#')[0]);
            elsewhere.ShouldNotContain("comment--linked", Case.Sensitive);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_link_to_an_argument_beyond_the_first_two_of_its_side_shows_and_marks_it()
    {
        try
        {
            var (reason, _) = await PrepareAsync();
            var address = BillTextLinks.ToReason(Bill, reason);
            address.ShouldBe($"/seimas/{Bill}?argumentas={reason:N}#reason-{reason:D}");

            var html = await PageAsync(address.Split('#')[0]);
            html.ShouldContain($"class=\"said said--linked\" id=\"{BillTextLinks.ReasonElement(reason)}\"", Case.Sensitive);
            Regex.Count(html, "said--linked").ShouldBe(1);
            ThreadOpen(html, reason).ShouldBeFalse("a link to an argument opens no discussion");

            var unknown = await PageAsync(BillTextLinks.ToReason(Bill, Guid.NewGuid()).Split('#')[0]);
            unknown.ShouldNotContain("said--linked", Case.Sensitive);
            unknown.ShouldNotContain($"id=\"{BillTextLinks.ReasonElement(reason)}\"", Case.Sensitive);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
