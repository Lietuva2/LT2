using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.11.8 over HTTP: a member's follow of a bill keeps when they last opened its page; the page's one write answers the
/// moment before and what others wrote since (never the member's own, never a hidden text); the member's lists count it; nothing
/// is kept for a bill not followed; the moment goes with the follow and with the account and is in the export; no served page
/// holds any of it.
/// </summary>
public sealed class BillNewsTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    private static async Task<BillSeenResponse> SeenAsync(TestBrowser browser, string bill = Bill)
    {
        using var response = await browser.PostAsync(ApiRoutes.Me.BillSeenOf(bill));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<BillSeenResponse>())!;
    }

    private Task<DateTime> LastSeenAsync(string email) =>
        Database.ScalarAsync<DateTime>(
            "SELECT last_seen_at FROM notify.follow WHERE kind = 'bill' AND key = @b AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("b", Bill), ("e", email));

    private Task<long> FollowRowsAsync(string email) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM notify.follow WHERE kind = 'bill' AND key = @b AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("b", Bill), ("e", email));

    [Fact]
    public async Task A_follower_is_told_what_others_wrote_since_the_last_visit_never_their_own_and_the_visit_moves_the_moment()
    {
        var moderator = await ModeratorAsync();
        await Database.FreeBillAsync(Bill);
        var (reader, readerEmail) = await Factory.MemberAsync("Ona Skaitytoja");
        using var _r = reader;
        await reader.FollowBillAsync(Bill);
        var followedAt = await LastSeenAsync(readerEmail);
        followedAt.ShouldBe(await Database.ScalarAsync<DateTime>(
            "SELECT created_at FROM notify.follow WHERE kind = 'bill' AND key = @b AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("b", Bill), ("e", readerEmail)), "the first moment is the follow's own");

        // Others write: an argument, an organisation's statement, a comment; the reader writes an argument and a comment of their own.
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var _a = bill.ArgumentAuthor;
        using var _o = bill.Representative;
        using (var own = await reader.WriteReasonAsync(Bill, "for", "Mano paties argumentas apie naujienas."))
        {
            own.StatusCode.ShouldBe(HttpStatusCode.Created, await own.Content.ReadAsStringAsync());
        }

        var ownReason = (await reader.RecordAsync(Bill)).Reasons.Single(r => r.Text == "Mano paties argumentas apie naujienas.").Id;
        var othersComment = await bill.Representative.CommentOkAsync("reason", bill.ReasonId, "Kito nario komentaras po argumentu.");
        var ownComment = await reader.CommentOkAsync("reason", bill.ReasonId, "Mano paties komentaras.");
        var hidden = await bill.ArgumentAuthor.CommentOkAsync("statement", bill.StatementId, "Komentaras, kurį moderatorius paslėps.");
        await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, hidden);

        // The lists count it, in one answer each, for the follower only.
        var follows = await reader.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine);
        follows.Bills.Single(b => b.Number == Bill).New.ShouldBe(new BillNewsCountsResponse(1, 1, 1, 0));
        var mine = await reader.GetJsonAsync<MyBillsResponse>(ApiRoutes.Me.Bills + "?numbers=" + Bill);
        mine.Bills.Single().New.ShouldBe(new BillNewsCountsResponse(1, 1, 1, 0));

        // The page's write: the moment before, and the new texts by id.
        var seen = await SeenAsync(reader);
        seen.Following.ShouldBeTrue();
        seen.Since!.Value.UtcDateTime.ShouldBe(DateTime.SpecifyKind(followedAt, DateTimeKind.Utc), TimeSpan.FromMilliseconds(1));
        seen.Reasons.ShouldBe([bill.ReasonId]);
        seen.Statements.ShouldBe([bill.StatementId]);
        seen.Responses.ShouldBeEmpty();
        seen.Comments.ShouldBe([new NewCommentResponse(othersComment, "reason", bill.ReasonId)]);
        seen.Reasons.ShouldNotContain(ownReason);
        seen.Comments.ShouldNotContain(c => c.Id == ownComment || c.Id == hidden);

        // Opened again: the moment moved to the first visit; nothing is new and the lists say nothing.
        var again = await SeenAsync(reader);
        again.Since!.Value.ShouldBeGreaterThan(seen.Since.Value);
        again.Reasons.ShouldBeEmpty();
        again.Comments.ShouldBeEmpty();
        again.Statements.ShouldBeEmpty();
        (await reader.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.Single(b => b.Number == Bill).New.ShouldBeNull();
        (await reader.GetJsonAsync<MyBillsResponse>(ApiRoutes.Me.Bills + "?numbers=" + Bill)).Bills.Single().New.ShouldBeNull();

        // A text changed after it is new again: the organisation edits its statement.
        await bill.Representative.PublishStatementAsync(Bill, bill.OrganisationSlug, "for", "Asociacija pritaria pataisai ir siūlo ją priimti greičiau.");
        (await SeenAsync(reader)).Statements.ShouldBe([bill.StatementId]);

        // The organisation's own representative is not told about its own statement.
        await bill.Representative.FollowBillAsync(Bill);
        await Database.ExecuteAsync(
            """
            UPDATE notify.follow SET last_seen_at = now() - interval '1 day'
            WHERE kind = 'bill' AND key = @b AND user_id IN (
                SELECT r.user_id FROM appeals.organisation_representative r JOIN appeals.organisation o ON o.id = r.organisation_id WHERE o.slug = @s)
            """,
            ("b", Bill), ("s", bill.OrganisationSlug));
        var representativeSeen = await SeenAsync(bill.Representative);
        representativeSeen.Statements.ShouldBeEmpty("its own organisation's statement");
        representativeSeen.Comments.ShouldNotContain(c => c.Id == othersComment, "their own comment");
        representativeSeen.Comments.ShouldContain(c => c.Id == ownComment, "the reader's comment is someone else's to them");
    }

    [Fact]
    public async Task A_bill_not_followed_keeps_nothing_and_answers_nothing_and_a_visitor_is_refused()
    {
        var moderator = await ModeratorAsync();
        await Database.FreeBillAsync(Bill);
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var _a = bill.ArgumentAuthor;
        using var _o = bill.Representative;
        var (stranger, strangerEmail) = await Factory.MemberAsync("Ne Sekėja");
        using var _s = stranger;

        var seen = await SeenAsync(stranger);
        seen.Following.ShouldBeFalse("nothing about the bill for one who does not follow it");
        seen.Since.ShouldBeNull();
        seen.Reasons.ShouldBeEmpty();
        seen.Comments.ShouldBeEmpty();
        seen.Statements.ShouldBeEmpty();
        (await FollowRowsAsync(strangerEmail)).ShouldBe(0, "a visit follows nothing");
        (await stranger.GetJsonAsync<MyBillsResponse>(ApiRoutes.Me.Bills + "?numbers=" + Bill)).Bills.ShouldBeEmpty();

        using var visitor = NewBrowser();
        using var refused = await visitor.PostAsync(ApiRoutes.Me.BillSeenOf(Bill));
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Like every write: the antiforgery header is needed.
        using var forged = await stranger.PostAsync(ApiRoutes.Me.BillSeenOf(Bill), antiforgery: false);
        await forged.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "antiforgery.invalid");
    }

    [Fact]
    public async Task The_moment_is_in_the_export_goes_with_unfollowing_and_with_the_account()
    {
        await Database.FreeBillAsync(Bill);
        var (member, email) = await Factory.MemberAsync("Eksportuojanti Narė");
        using var _m = member;
        await member.FollowBillAsync(Bill);
        await SeenAsync(member);
        var kept = await LastSeenAsync(email);

        using (var response = await member.Client.GetAsync(ApiRoutes.Me.Export))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var export = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var follow = export.RootElement.GetProperty("follows").GetProperty("follows").EnumerateArray().Single(f => f.GetProperty("key").GetString() == Bill);
            follow.GetProperty("lastSeenAt").GetDateTimeOffset().UtcDateTime.ShouldBe(DateTime.SpecifyKind(kept, DateTimeKind.Utc), TimeSpan.FromMilliseconds(1));
        }

        (await member.SendAsync(HttpMethod.Delete, FollowRoutes.BillOf(Bill))).Dispose();
        (await FollowRowsAsync(email)).ShouldBe(0, "the moment goes with the follow");
        (await SeenAsync(member)).Following.ShouldBeFalse();

        // Following again starts from the new follow's own moment.
        await member.FollowBillAsync(Bill);
        (await LastSeenAsync(email)).ShouldBeGreaterThan(kept);

        var userId = await Database.UserIdAsync(email);
        await member.DeleteAccountAsync();
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.follow WHERE user_id = @u", ("u", userId))).ShouldBe(0);
    }

    [Fact]
    public async Task The_served_bill_page_holds_nothing_of_what_is_new_to_the_member()
    {
        var moderator = await ModeratorAsync();
        await Database.FreeBillAsync(Bill);
        var (reader, readerEmail) = await Factory.MemberAsync("Ona Skaitytoja");
        using var _r = reader;
        await reader.FollowBillAsync(Bill);
        var before = await LastSeenAsync(readerEmail);
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var _a = bill.ArgumentAuthor;
        using var _o = bill.Representative;
        await bill.Representative.CommentOkAsync("reason", bill.ReasonId, "Naujas komentaras skaitytojai.");
        (await reader.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.Single(b => b.Number == Bill).New.ShouldNotBeNull("there is something new to the reader");

        var (other, _) = await Factory.MemberAsync("Kita Narė");
        using var _x = other;
        var readersPage = await reader.Client.GetStringAsync("/seimas/" + Bill);
        var othersPage = await other.Client.GetStringAsync("/seimas/" + Bill);
        foreach (var page in new[] { readersPage, othersPage })
        {
            page.ShouldNotContain("tag--new");
            page.ShouldNotContain("only-new");
            page.ShouldNotContain("Rodyti tik naujus");
            page.ShouldNotContain(">Nauja<");
        }

        (await LastSeenAsync(readerEmail)).ShouldBe(before, "reading the page keeps nothing: only the island's write does");
    }
}
