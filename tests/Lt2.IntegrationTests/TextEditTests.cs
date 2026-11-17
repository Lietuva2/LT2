using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.2 and 8.23.3 (owner, 2026-11-17) over HTTP: the author edits an argument (its text and side) or a comment until someone
/// reacts to it ("Man svarbu" or a comment under the argument, hidden ones included; "Sutinku" on a comment), because the
/// reactions were given to its exact words; nobody else edits it; the rules of writing hold for the new text (pre-screening
/// makes a flagged argument wait for a moderator again); no earlier text is kept and the audit says only that an edit happened.
/// Each test frees its bill.
/// </summary>
public sealed class TextEditTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    private static Task<HttpResponseMessage> EditReasonAsync(TestBrowser browser, Guid reasonId, string side, string text) =>
        browser.SendAsync(HttpMethod.Put, AppealRoutes.ReasonOf(reasonId), new ReasonRequest { Side = side, Text = text });

    private static Task<HttpResponseMessage> EditCommentAsync(TestBrowser browser, Guid commentId, string text) =>
        browser.SendAsync(HttpMethod.Put, CommentRoutes.OneOf(commentId), new CommentRequest { Text = text });

    private static async Task<Guid> WriteOkAsync(TestBrowser author, string side, string text)
    {
        using (var written = await author.WriteReasonAsync(Bill, side, text))
        {
            written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
        }

        return (await author.MineAsync(Bill)).MyReasons.Single(r => r.Text == text).Id;
    }

    [Fact]
    public async Task An_author_edits_an_argument_until_someone_marks_it_or_comments_under_it_and_nobody_else_can()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var (author, _) = await Factory.MemberAsync("Autorė Redaguojanti");
        using var _a = author;
        var (other, _) = await Factory.MemberAsync("Kitas Narys");
        using var _o = other;
        try
        {
            var record = await Database.PrepareBillAsync(initiator, moderator, Bill, "against");
            var edited = await WriteOkAsync(author, "for", "Pirmasis tekstas, kurį autorė pataisys.");
            (await author.MineAsync(Bill)).Editable.ShouldContain(edited);
            await Database.ExecuteAsync("UPDATE appeals.reason SET seen_at = now() WHERE id = @r", ("r", edited));

            // Nobody else: another member, a moderator; a visitor is not signed in.
            using (var refused = await EditReasonAsync(other, edited, "for", "Svetimas tekstas."))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "reason.edit-author-only");
            }

            (await EditReasonAsync(moderator, edited, "for", "Moderatoriaus tekstas.")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            using (var visitor = NewBrowser())
            {
                (await visitor.Client.PutAsJsonAsync(AppealRoutes.ReasonOf(edited), new ReasonRequest { Side = "for", Text = "x" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            // The author changes the text and the side: shown at once, in place, the moderator's "seen" gone with the old words.
            using (var saved = await EditReasonAsync(author, edited, "against", "Pataisytas tekstas, dabar prieš."))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
                (await saved.ReadAsync<ReasonCreatedResponse>()).ShouldBe(new ReasonCreatedResponse(edited, "published"));
            }

            var shown = (await other.RecordAsync(Bill)).Reasons.Single(r => r.Id == edited);
            (shown.Side, shown.Text).ShouldBe(("against", "Pataisytas tekstas, dabar prieš."));
            (await Database.ScalarAsync<bool>("SELECT seen_at IS NULL AND edited_at IS NOT NULL FROM appeals.reason WHERE id = @r", ("r", edited))).ShouldBeTrue();
            (await Database.ScalarAsync<bool>("SELECT last_activity_at IS NOT NULL FROM appeals.bill_record WHERE id = @a", ("a", record.Id))).ShouldBeTrue();
            var details = await Database.ScalarAsync<string>(
                "SELECT details::text FROM audit.event WHERE action = 'reason.edited' AND target = @t AND actor_role = 'member'", ("t", "reason:" + edited));
            details.ShouldNotBeNull("the edit is audited");
            details.ShouldContain(Bill);
            details.ShouldNotContain("Pataisytas");
            details.ShouldNotContain("against");

            // A "Man svarbu" ends it, under the argument's lock.
            (await other.TakeSideAsync(Bill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await other.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(edited))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using (var reacted = await EditReasonAsync(author, edited, "against", "Dar kartą pataisytas."))
            {
                await reacted.ShouldBeProblemAsync(HttpStatusCode.Conflict, "reason.reacted");
                (await reacted.ProblemAsync()).GetProperty("detail").GetString()!.ShouldContain("Galite jį atsiimti");
            }

            (await author.MineAsync(Bill)).Editable.ShouldNotContain(edited);
            (await other.RecordAsync(Bill)).Reasons.Single(r => r.Id == edited).Text.ShouldBe("Pataisytas tekstas, dabar prieš.");

            // So does a comment under it, even one a moderator hid since.
            var commented = await WriteOkAsync(author, "for", "Antrasis tekstas, po kuriuo kažkas pakomentuos.");
            var comment = await other.CommentOkAsync("reason", commented, "Komentaras po argumentu.");
            await moderator.ActOnCommentAsync(CommentRoutes.Hide, comment);
            using (var reacted = await EditReasonAsync(author, commented, "for", "Pataisytas po komentaro."))
            {
                await reacted.ShouldBeProblemAsync(HttpStatusCode.Conflict, "reason.reacted");
            }

            // Not on a closed record, as writing.
            var late = await WriteOkAsync(author, "for", "Trečiasis tekstas uždarytam įrašui.");
            (await author.MineAsync(Bill)).Editable.ShouldContain(late);
            await Database.ExecuteAsync("UPDATE appeals.bill_record SET status = 'closed', closed_at = now() WHERE id = @a", ("a", record.Id));
            (await author.MineAsync(Bill)).Editable.ShouldBeEmpty();
            using (var closed = await EditReasonAsync(author, late, "for", "Pavėluotas taisymas."))
            {
                await closed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.not-open");
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task An_edit_pre_screening_flags_waits_for_a_moderator_again_and_is_not_shown_meanwhile()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var (author, _) = await Factory.MemberAsync("Autorė Su Nuoroda");
        using var _a = author;
        using var visitor = NewBrowser();
        try
        {
            await Database.PrepareBillAsync(initiator, moderator, Bill, "against");
            var reasonId = await WriteOkAsync(author, "for", "Švarus tekstas be nuorodų.");
            (await visitor.RecordAsync(Bill)).Reasons.ShouldContain(r => r.Id == reasonId);

            using (var saved = await EditReasonAsync(author, reasonId, "for", "Plačiau: https://pavyzdys.test/straipsnis"))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await saved.ReadAsync<ReasonCreatedResponse>()).Status.ShouldBe("pending");
            }

            (await visitor.RecordAsync(Bill)).Reasons.ShouldNotContain(r => r.Id == reasonId, "waiting for a moderator, not public");
            (await author.MineAsync(Bill)).MyReasons.Single(r => r.Id == reasonId).Status.ShouldBe("pending");
            var queued = (await moderator.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Reasons.Single(r => r.Id == reasonId);
            (queued.Status, queued.Seen).ShouldBe(("pending", false));
            queued.Flags.ShouldNotBeEmpty();
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task An_author_edits_a_comment_in_its_voice_until_someone_agrees_and_the_rules_of_writing_hold()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator, Bill);
        var (member, _) = await Factory.MemberAsync("Ona Komentatorė");
        using var _m = member;
        var (other, _) = await Factory.MemberAsync("Kitas Narys");
        using var _o = other;
        using var visitor = NewBrowser();
        try
        {
            var comment = await member.CommentOkAsync("reason", bill.ReasonId, "Pirmasis komentaro tekstas.");
            (await member.MyCommentsAsync(Bill)).Editable.ShouldBe([comment]);

            using (var refused = await EditCommentAsync(other, comment, "Svetimas tekstas."))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "comment.edit-author-only");
            }

            // The rules of writing: pre-screening refuses personal data as it does a new comment.
            using (var screened = await EditCommentAsync(member, comment, "Rašykite man: ona@pavyzdys.test"))
            {
                await screened.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "comment.screened");
            }

            (await EditCommentAsync(member, comment, "Pataisytas komentaro tekstas.")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await visitor.ThreadAsync("reason", bill.ReasonId)).Items.Single(i => i.Id == comment).Text.ShouldBe("Pataisytas komentaro tekstas.");
            var details = await Database.ScalarAsync<string>(
                "SELECT details::text FROM audit.event WHERE action = 'comment.edited' AND target = @t AND actor_role = 'member'", ("t", "comment:" + comment));
            details.ShouldNotBeNull("the edit is audited");
            details.ShouldNotContain("Pataisytas");

            // An organisation's comment is edited by its writer in the organisation's voice, as it was written.
            var organisations = await bill.Representative.CommentOkAsync("statement", bill.StatementId, "Asociacijos komentaras.");
            (await EditCommentAsync(bill.Representative, organisations, "Asociacijos pataisytas komentaras.")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var shown = (await visitor.ThreadAsync("statement", bill.StatementId)).Items.Single(i => i.Id == organisations);
            (shown.Author.Voice, shown.Text).ShouldBe(("organisation", "Asociacijos pataisytas komentaras."));

            // A "Sutinku" ends it, under the comment's lock.
            (await other.AgreeAsync(comment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using (var reacted = await EditCommentAsync(member, comment, "Dar kartą pataisytas."))
            {
                await reacted.ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.reacted");
            }

            (await member.MyCommentsAsync(Bill)).Editable.ShouldBeEmpty();
            (await visitor.ThreadAsync("reason", bill.ReasonId)).Items.Single(i => i.Id == comment).Text.ShouldBe("Pataisytas komentaro tekstas.");

            // A hidden one is the moderators'.
            var hidden = await member.CommentOkAsync("reason", bill.ReasonId, "Komentaras, kurį paslėps.");
            await moderator.ActOnCommentAsync(CommentRoutes.Hide, hidden);
            using (var refused = await EditCommentAsync(member, hidden, "Pataisytas paslėptas."))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.hidden");
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
