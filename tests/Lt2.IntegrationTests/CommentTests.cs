using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.23 over HTTP: comments under an argument, a statement and a response are public at once under the right name;
/// the server decides the voice (a representative under its organisation's statement, the MP's account or office under the
/// MP's response, in a passkey session); the text's rules (the length setting, pre-screening refuses personal data, a link is
/// allowed and shown as a link); the switches; deleting; "Sutinku"; the discussion following its target; reports and
/// moderation; the author's notice; the export and deletion with the account; and nothing counted. Each test frees its bill
/// and removes the MP accounts it made.
/// </summary>
public sealed class CommentTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    /// <summary>A vote on <see cref="Bill"/> to copy an adoption vote from (as <c>MpResponseTests</c>).</summary>
    private const long BillVote = 99100102;

    private static readonly string[] ThreadFields = ["billNumber", "billOpen", "cards", "count", "items", "kind", "nextCursor", "open", "targetId"];
    private static readonly string[] CommentFields = ["agreedBy", "author", "createdAt", "id", "text"];
    private static readonly string[] AuthorFields = ["label", "mp", "name", "organisationSlug", "profileId", "voice"];

    [Fact]
    public async Task A_member_comments_under_an_argument_a_statement_and_a_response_and_each_is_public_at_once_under_the_display_name()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var (member, _) = await Factory.MemberAsync("Ona Komentatorė");
        using var visitor = NewBrowser();
        try
        {
            var response = (await mp.Browser.RespondOkAsync(Bill, "for", "Balsuosiu už: pataisa mažina naštą šeimoms.")).Id;
            var onReason = await member.CommentOkAsync("reason", bill.ReasonId, "Argumentas svarus, bet trūksta skaičių.");
            var onStatement = await member.CommentOkAsync("statement", bill.StatementId, "Ar asociacija skaičiavo poveikį savivaldybėms?");
            var onResponse = await member.CommentOkAsync("response", response, "Ačiū už atsakymą, laukiame balsavimo.");

            foreach (var (kind, target, id) in new[] { ("reason", bill.ReasonId, onReason), ("statement", bill.StatementId, onStatement), ("response", response, onResponse) })
            {
                var thread = await visitor.ThreadAsync(kind, target);
                (thread.Kind, thread.TargetId, thread.BillNumber, thread.Count, thread.Open, thread.BillOpen).ShouldBe((kind, target, Bill, 1, true, true), kind);
                var item = thread.Items.ShouldHaveSingleItem();
                (item.Id, item.Author.Voice, item.Author.Name).ShouldBe((id, "person", "Ona Komentatorė"), kind);
                item.AgreedBy.ShouldBeEmpty();
            }

            // The public answers about the bill carry each target's count (design 8.23.14).
            (await visitor.RecordAsync(Bill)).Reasons.Single(r => r.Id == bill.ReasonId).Comments.ShouldBe(1);
            (await visitor.StatementsAsync(Bill)).Items.Single(s => s.Statement.Id == bill.StatementId).Statement.Comments.ShouldBe(1);
            (await visitor.ResponsesAsync(Bill)).Items.Single().Response.Comments.ShouldBe(1);

            // The page reads without script: each thread is on it, with its anchor, the same for a visitor and a member.
            var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + Bill));
            foreach (var id in new[] { onReason, onStatement, onResponse })
            {
                page.ShouldContain($"id=\"{CommentLimits.ElementId(id)}\"");
            }

            // Design 8.16.1: the summary's word and count, the word kept for screen readers on narrow rows.
            page.ShouldContain("<span class=\"discuss__word\">Aptarimas</span> <span class=\"num\">(1)</span>");
            page.ShouldContain("Argumentas svarus, bet trūksta skaičių.");
            page.ShouldContain("Ačiū už atsakymą, laukiame balsavimo.");

            // The same lists for a member: what differs is only filled in the browser (design 8.23.6).
            var (reader, _) = await Factory.MemberAsync("Skaitanti Narė");
            using var _rd = reader;
            var memberPage = WebUtility.HtmlDecode(await reader.Client.GetStringAsync("/seimas/" + Bill));
            var lists = CommentLists(page);
            lists.Count.ShouldBe(3, "the swept page holds the three threads");
            CommentLists(memberPage).ShouldBe(lists);
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_representative_under_its_own_statement_and_the_mp_under_its_own_response_are_shown_only_as_the_organisation_and_the_mp()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var (office, officeKey, officeEmail) = await Factory.OfficeMemberAsync(mp, "Biuro Narė Komentarams");
        using var _o = office;
        using var _k = officeKey;
        using var visitor = NewBrowser();
        try
        {
            var response = (await mp.Browser.RespondOkAsync(Bill, "against", "Balsuosiu prieš.")).Id;

            // The representative under the organisation's own statement: the organisation's name and mark, never the person's.
            await bill.Representative.CommentOkAsync("statement", bill.StatementId, "Asociacija paaiškina: poveikis skaičiuotas.");
            var asOrganisation = (await visitor.ThreadAsync("statement", bill.StatementId)).Items.Single();
            (asOrganisation.Author.Voice, asOrganisation.Author.Name, asOrganisation.Author.OrganisationSlug).ShouldBe(("organisation", bill.OrganisationName, bill.OrganisationSlug));

            // Elsewhere the representative is a person, under their own name, tied to no organisation.
            await bill.Representative.CommentOkAsync("reason", bill.ReasonId, "Kaip asmuo manau, kad argumentas per griežtas.");
            var asPerson = (await visitor.ThreadAsync("reason", bill.ReasonId)).Items.Single();
            (asPerson.Author.Voice, asPerson.Author.Name, asPerson.Author.OrganisationSlug).ShouldBe(("person", "Tomas Atstovas", (string?)null));

            // The MP's account under its own response speaks as the MP; an office member too; a code session is refused.
            await mp.Browser.CommentOkAsync("response", response, "Seimo narys patikslina savo atsakymą.");
            await office.CommentOkAsync("response", response, "Biuras primena posėdžio datą.");
            var thread = await visitor.ThreadAsync("response", response);
            thread.Items.Select(i => (i.Author.Voice, i.Author.Mp!.Slug, i.Author.Name)).ShouldAllBe(a => a.Voice == "mp" && a.Slug == mp.Slug && a.Name == null);
            using (var code = await Factory.CodeSessionAsync(officeEmail))
            {
                await (await code.CommentAsync("response", response, "Kodu prisijungęs biuras.")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
            }

            // The MP's own account comments nowhere else (design 8.5.4).
            await (await mp.Browser.CommentAsync("reason", bill.ReasonId, "Seimo nario paskyra po argumentu.")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");

            // Nothing public names the person behind the organisation's or the MP's comment.
            var json = await visitor.Client.GetStringAsync(CommentRoutes.ThreadOf("response", response)) + await visitor.Client.GetStringAsync(CommentRoutes.ThreadOf("statement", bill.StatementId));
            json.ShouldNotContain("Biuro Narė Komentarams");
            json.ShouldNotContain("Tomas Atstovas");
            json.ShouldNotContain((await Database.UserIdAsync(mp.Email)).ToString());
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task The_text_is_checked_where_it_is_written_personal_data_refuses_it_and_a_https_address_is_a_link()
    {
        var (moderator, administrator) = await StaffAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (member, _) = await Factory.MemberAsync("Linas Rašytojas");
        using var visitor = NewBrowser();
        try
        {
            await administrator.Browser.SetSettingAsync("Texts:MaxLength", 100);
            await (await member.CommentAsync("reason", bill.ReasonId, new string('a', 101))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "comment.too-long");
            await member.CommentOkAsync("reason", bill.ReasonId, new string('b', 100));
            await (await member.CommentAsync("reason", bill.ReasonId, "   ")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");

            // Pre-screening refuses personal data with a message naming what was found; nothing reaches the moderators.
            using (var email = await member.CommentAsync("reason", bill.ReasonId, "Rašykite man: linas@pavyzdys.test"))
            {
                var problem = await email.ProblemAsync();
                problem.GetProperty("code").GetString().ShouldBe("comment.screened");
                problem.GetProperty("detail").GetString()!.ShouldContain("el. pašto adresas");
            }

            using (var phone = await member.CommentAsync("reason", bill.ReasonId, "Skambinkite +370 612 34567"))
            {
                (await phone.ProblemAsync()).GetProperty("detail").GetString()!.ShouldContain("telefono numeris");
            }

            (await moderator.Browser.GetJsonAsync<CommentModerationResponse>(CommentRoutes.Moderation)).Reported.ShouldNotContain(c => c.Bill.Number == Bill);

            // A link is allowed: an https address becomes a link with its rel; http and a bare name stay text.
            await member.CommentOkAsync("reason", bill.ReasonId, "Šaltinis https://pavyzdys.test/tyrimas, plg. http://kitas.pavyzdys.test ir lrs.lt");
            var page = await visitor.Client.GetStringAsync("/seimas/" + Bill);
            page.ShouldContain("<a href=\"https://pavyzdys.test/tyrimas\" rel=\"nofollow ugc noopener noreferrer\" target=\"_blank\">https://pavyzdys.test/tyrimas</a>");
            page.ShouldNotContain("href=\"http://kitas.pavyzdys.test");
        }
        finally
        {
            await administrator.Browser.ResetSettingAsync("Texts:MaxLength");
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_kind_switched_off_refuses_new_comments_and_keeps_the_written_ones_readable_and_agreeable()
    {
        var (moderator, administrator) = await StaffAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (member, _) = await Factory.MemberAsync("Jonas Skaitytojas");
        using var visitor = NewBrowser();
        try
        {
            var written = await member.CommentOkAsync("reason", bill.ReasonId, "Parašyta prieš išjungiant.");
            await administrator.Browser.SetSettingAsync("Comments:OnReasons", false);
            await (await member.CommentAsync("reason", bill.ReasonId, "Po išjungimo.")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comments.off");
            var thread = await visitor.ThreadAsync("reason", bill.ReasonId);
            (thread.Open, thread.Count).ShouldBe((false, 1));
            thread.Items.Single().Id.ShouldBe(written);
            (await bill.ArgumentAuthor.AgreeAsync(written)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "Sutinku works as before");

            // The other kinds are their own switches.
            await member.CommentOkAsync("statement", bill.StatementId, "Po pozicija dar galima.");
        }
        finally
        {
            await administrator.Browser.ResetSettingAsync("Comments:OnReasons");
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task Sutinku_is_public_with_names_once_per_person_never_on_ones_own_and_taken_back_while_the_bill_is_open()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (writer, _) = await Factory.MemberAsync("Rašanti Narė");
        var (first, _) = await Factory.MemberAsync("Pirmas Sutinkantis");
        var (second, _) = await Factory.MemberAsync(null);
        using var visitor = NewBrowser();
        try
        {
            var comment = await writer.CommentOkAsync("reason", bill.ReasonId, "Reikėtų pereinamojo laikotarpio.");
            (await first.MyCommentsAsync(Bill)).AgreedBefore.ShouldBeFalse("nobody agreed yet: the first press asks first");
            (await first.AgreeAsync(comment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await first.AgreeAsync(comment)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "again changes nothing");
            (await second.AgreeAsync(comment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await visitor.ThreadAsync("reason", bill.ReasonId)).Items.Single().AgreedBy.Select(a => a.Name).ShouldBe(["Pirmas Sutinkantis", null]);
            var mine = await first.MyCommentsAsync(Bill);
            (mine.AgreedBefore, mine.Agreed.Contains(comment)).ShouldBe((true, true));

            await (await writer.AgreeAsync(comment)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "agreement.own-comment");
            (await first.DisagreeAsync(comment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await visitor.ThreadAsync("reason", bill.ReasonId)).Items.Single().AgreedBy.Select(a => a.Name).ShouldBe([null]);

            // No positions consent is asked: a member without it agrees all the same.
            var (noConsent, _) = await Factory.MemberAsync("Be Sutikimo", consent: false);
            (await noConsent.AgreeAsync(comment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // The order stays the order of writing, whatever the marks (design 8.23.1).
            var later = await writer.CommentOkAsync("reason", bill.ReasonId, "Antras komentaras.");
            (await first.AgreeAsync(later)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await second.AgreeAsync(later)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await visitor.ThreadAsync("reason", bill.ReasonId)).Items.Select(i => i.Id).ShouldBe([comment, later]);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task The_author_deletes_their_own_comment_with_its_sutinku_and_nobody_deletes_or_marks_once_the_bill_is_closed()
    {
        const long vote = 99199811;
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (writer, _) = await Factory.MemberAsync("Trinanti Narė");
        var (other, _) = await Factory.MemberAsync("Kitas Narys");
        using var visitor = NewBrowser();
        try
        {
            var gone = await writer.CommentOkAsync("reason", bill.ReasonId, "Šį ištrinsiu.");
            (await other.AgreeAsync(gone)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await (await other.DeleteCommentAsync(gone)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "comment.author-only");
            (await writer.MyCommentsAsync(Bill)).Deletable.ShouldContain(gone);
            (await other.MyCommentsAsync(Bill)).Deletable.ShouldNotContain(gone);
            (await writer.DeleteCommentAsync(gone)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await visitor.ThreadAsync("reason", bill.ReasonId)).Count.ShouldBe(0);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment_agreement WHERE comment_id = @c", ("c", gone))).ShouldBe(0);

            // The final vote: read-only, nothing written, deleted, given or taken back.
            var kept = await writer.CommentOkAsync("reason", bill.ReasonId, "Šis liks.");
            (await other.AgreeAsync(kept)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await Database.AddAdoptionVoteAsync(vote, BillVote);
            var thread = await visitor.ThreadAsync("reason", bill.ReasonId);
            (thread.Open, thread.BillOpen, thread.Count).ShouldBe((false, false, 1));
            await (await other.CommentAsync("reason", bill.ReasonId, "Per vėlu.")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.bill-not-open");
            await (await writer.DeleteCommentAsync(kept)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.bill-not-open");
            await (await other.DisagreeAsync(kept)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.bill-not-open");
            var (late, _) = await Factory.MemberAsync("Vėlyvas Narys");
            await (await late.AgreeAsync(kept)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.bill-not-open");

            // A moderator may still hide and restore (design 6.9).
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, kept);
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Restore, kept);
        }
        finally
        {
            await Database.RemoveVoteAsync(vote);
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task The_discussion_follows_its_target_withdrawn_goes_hidden_hides_and_a_withdrawn_response_stays_read_only()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var (member, _) = await Factory.MemberAsync("Sekanti Diskusiją");
        using var visitor = NewBrowser();
        try
        {
            // A hidden argument hides its comments; restored, they are back with their "Sutinku".
            var onReason = await member.CommentOkAsync("reason", bill.ReasonId, "Po argumentu.");
            (await bill.Representative.AgreeAsync(onReason)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using (var hid = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, bill.ReasonId), new StatementRequest { Statement = "Bandymas." }))
            {
                hid.StatusCode.ShouldBe(HttpStatusCode.NoContent, await hid.Content.ReadAsStringAsync());
            }

            (await visitor.Client.GetAsync(CommentRoutes.ThreadOf("reason", bill.ReasonId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            await (await member.AgreeAsync(onReason)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "comment.not-found");
            using (var restored = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.RestoreReason, bill.ReasonId), new StatementRequest { Statement = "Bandymas." }))
            {
                restored.StatusCode.ShouldBe(HttpStatusCode.NoContent, await restored.Content.ReadAsStringAsync());
            }

            (await visitor.ThreadAsync("reason", bill.ReasonId)).Items.Single().AgreedBy.Select(a => a.Name).ShouldBe(["Tomas Atstovas"]);

            // A withdrawn argument takes its discussion with it.
            using (var withdrawn = await bill.ArgumentAuthor.SendAsync(HttpMethod.Delete, AppealRoutes.ReasonOf(bill.ReasonId)))
            {
                withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await withdrawn.Content.ReadAsStringAsync());
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE target_id = @t", ("t", bill.ReasonId))).ShouldBe(0);

            // So does a withdrawn statement.
            await member.CommentOkAsync("statement", bill.StatementId, "Po pozicija.");
            using (var gone = await bill.Representative.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.Organisations.OrganisationRoutes.StatementOf(Bill, bill.OrganisationSlug)))
            {
                gone.StatusCode.ShouldBe(HttpStatusCode.NoContent, await gone.Content.ReadAsStringAsync());
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE target_id = @t", ("t", bill.StatementId))).ShouldBe(0);

            // A withdrawn response stays with its discussion, read-only; written again, it opens again.
            var response = (await mp.Browser.RespondOkAsync(Bill, "for", "Balsuosiu už.")).Id;
            var underResponse = await member.CommentOkAsync("response", response, "Po atsakymu.");
            var (other, _) = await Factory.MemberAsync("Sutikusi Prieš Atsiimant");
            using var _ot = other;
            (await other.AgreeAsync(underResponse)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "agreed while it was open");
            (await mp.Browser.WithdrawResponseAsync(Bill)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var thread = await visitor.ThreadAsync("response", response);
            (thread.Count, thread.Open, thread.BillOpen).ShouldBe((1, false, false), "read-only: no writing, no Sutinku, no deleting");

            // Read-only means no "Sutinku" given or taken back and no deleting (design 8.23.7).
            await (await bill.Representative.AgreeAsync(underResponse)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.read-only");
            await (await other.DisagreeAsync(underResponse)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.read-only");
            await (await member.DeleteCommentAsync(underResponse)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.read-only");
            (await visitor.ThreadAsync("response", response)).Items.Single().AgreedBy.Count.ShouldBe(1, "the mark stays as it was");
            await (await member.CommentAsync("response", response, "Dar vienas.")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.target-closed");
            await mp.Browser.RespondOkAsync(Bill, "for", "Balsuosiu už, vėl.");
            (await visitor.ThreadAsync("response", response)).Open.ShouldBeTrue();
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_reported_comment_reaches_the_list_without_the_reporter_and_hiding_restoring_and_palikti_work_with_the_author_told()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (writer, writerEmail) = await Factory.MemberAsync("Pranešta Autorė");
        var (reporter, reporterEmail) = await Factory.MemberAsync("Slaptas Pranešėjas");
        var (agreeing, _) = await Factory.MemberAsync("Sutinkanti Narė");
        using var visitor = NewBrowser();
        try
        {
            var comment = await writer.CommentOkAsync("reason", bill.ReasonId, "Komentaras, apie kurį pranešama.");
            (await moderator.Browser.GetJsonAsync<CommentModerationResponse>(CommentRoutes.Moderation)).Reported.ShouldNotContain(c => c.Id == comment, "not listed unless reported");
            (await agreeing.AgreeAsync(comment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await reporter.ReportCommentAsync(comment);

            var json = await moderator.Browser.Client.GetStringAsync(CommentRoutes.Moderation);
            var listed = JsonSerializer.Deserialize<CommentModerationResponse>(json, JsonSerializerOptions.Web)!.Reported.Single(c => c.Id == comment);
            (listed.Text, listed.Author.Name, listed.Target.Kind, listed.Target.AuthorName, listed.Reports.Single().Ground).ShouldBe(("Komentaras, apie kurį pranešama.", "Pranešta Autorė", "reason", "Rūta Mockutė", "insult"));
            json.ShouldNotContain("Slaptas Pranešėjas");
            json.ShouldNotContain(reporterEmail);
            json.ShouldNotContain((await Database.UserIdAsync(reporterEmail)).ToString());

            // The bill page's answer is for moderators only.
            (await moderator.Browser.GetJsonAsync<List<QueueCommentResponse>>(CommentRoutes.OnBillOf(Bill))).Single(c => c.Id == comment).Reports.Count.ShouldBe(1);
            (await writer.Client.GetAsync(CommentRoutes.OnBillOf(Bill))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            // Hidden: gone from the page and the count with its "Sutinku"; the author gets the statement; the report is answered.
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, comment, "Įžeidžiantis tonas.");
            (await visitor.ThreadAsync("reason", bill.ReasonId)).Count.ShouldBe(0);
            (await visitor.Client.GetStringAsync("/seimas/" + Bill)).ShouldNotContain("Komentaras, apie kurį pranešama.");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment_agreement WHERE comment_id = @c", ("c", comment))).ShouldBe(0);
            (await Database.CommentModerationNoticesAsync(writerEmail)).ShouldBe(["comment-hidden"]);
            var moderation = await moderator.Browser.GetJsonAsync<CommentModerationResponse>(CommentRoutes.Moderation);
            moderation.Reported.ShouldNotContain(c => c.Id == comment);
            moderation.Hidden.Single(c => c.Id == comment).HideStatement.ShouldBe("Įžeidžiantis tonas.");

            // Restored: back, told; "Palikti" answers a new report and tells nobody.
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Restore, comment, "Peržiūrėjus – tinkamas.");
            (await visitor.ThreadAsync("reason", bill.ReasonId)).Items.Single().AgreedBy.ShouldBeEmpty("the agreements went with the hiding");
            await reporter.ReportCommentAsync(comment, "spam", null);
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Keep, comment, null);
            (await moderator.Browser.GetJsonAsync<CommentModerationResponse>(CommentRoutes.Moderation)).Reported.ShouldNotContain(c => c.Id == comment);
            (await Database.CommentModerationNoticesAsync(writerEmail)).ShouldBe(["comment-hidden", "comment-restored"]);

            // The writer is reached through the comment and restricted: no comment in any voice, "Sutinku" still works.
            (await moderator.Browser.GetJsonAsync<AuthorViewResponse>(CommentRoutes.With(CommentRoutes.Writer, comment))).DisplayName.ShouldBe("Pranešta Autorė");
            using (var restricted = await moderator.Browser.PostAsync(CommentRoutes.With(CommentRoutes.RestrictWriter, comment), new RestrictionRequest { Statement = "Bandymas: rašymas apribotas." }))
            {
                restricted.StatusCode.ShouldBe(HttpStatusCode.Created, await restricted.Content.ReadAsStringAsync());
            }

            await (await writer.CommentAsync("reason", bill.ReasonId, "Apribota.")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "writing.restricted");
            (await writer.MyCommentsAsync(Bill)).Restricted.ShouldBeTrue();
            var someone = await agreeing.CommentOkAsync("reason", bill.ReasonId, "Kito žmogaus komentaras.");
            (await writer.AgreeAsync(someone)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "a restriction does not block Sutinku (design 8.23.10)");
            (await visitor.Client.GetStringAsync(CommentRoutes.ThreadOf("reason", bill.ReasonId))).ShouldNotContain("restrict", Case.Insensitive);
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM appeals.writing_restriction WHERE user_id = @u", ("u", await Database.UserIdAsync(writerEmail)));
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task No_answer_ties_a_comment_or_a_sutinku_to_a_side_and_the_public_thread_holds_exactly_its_fields()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (forSide, forEmail) = await Factory.MemberAsync("Prašo Už");
        var (againstSide, _) = await Factory.MemberAsync("Prašo Prieš");
        var (noSide, _) = await Factory.MemberAsync("Be Pozicijos");
        using var visitor = NewBrowser();
        try
        {
            (await forSide.TakeSideAsync(Bill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await againstSide.TakeSideAsync(Bill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var ids = new List<Guid>();
            foreach (var (browser, text) in new[] { (forSide, "Komentaras A."), (againstSide, "Komentaras B."), (noSide, "Komentaras C.") })
            {
                ids.Add(await browser.CommentOkAsync("reason", bill.ReasonId, text));
            }

            (await forSide.AgreeAsync(ids[2])).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await againstSide.AgreeAsync(ids[2])).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var json = await visitor.Client.GetStringAsync(CommentRoutes.ThreadOf("reason", bill.ReasonId));
            using var document = JsonDocument.Parse(json);
            CommentSteps.Fields(document.RootElement).ShouldBe(ThreadFields);
            var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
            items.Count.ShouldBe(3, "the swept answer holds the comments");
            foreach (var item in items)
            {
                CommentSteps.Fields(item).ShouldBe(CommentFields);
                CommentSteps.Fields(item.GetProperty("author")).ShouldBe(AuthorFields);
                item.GetProperty("author").GetProperty("profileId").ValueKind.ShouldBe(JsonValueKind.Null, "nobody here has a public profile (design 8.25.4)");
                foreach (var agreer in item.GetProperty("agreedBy").EnumerateArray())
                {
                    CommentSteps.Fields(agreer).ShouldBe(["name", "profileId"]);
                    agreer.GetProperty("profileId").ValueKind.ShouldBe(JsonValueKind.Null);
                }
            }

            document.RootElement.GetProperty("cards").GetArrayLength().ShouldBe(0, "no card without a public profile");

            // Three people, three sides (for, against, none): the items differ only in their own words and names.
            json.ShouldNotContain("\"for\"");
            json.ShouldNotContain("\"against\"");
            json.ShouldNotContain((await Database.UserIdAsync(forEmail)).ToString());
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment_agreement WHERE comment_id = @c", ("c", ids[2]))).ShouldBe(2);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task Comments_and_their_sutinku_change_no_count_threshold_list_agenda_front_page_statistic_or_summary()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var visitor = NewBrowser();
        try
        {
            var routes = new[]
            {
                AppealRoutes.Appeals, AppealRoutes.AppealsList(null, false, false, null, "participants"),
                AppealRoutes.AppealsList(null, false, false, null, "arguments"), AppealRoutes.AppealsList(null, false, false, null, "activity"), AgendaRoutes.Agenda,
                "/", "/darbotvarke", "/projektai", "/statistika",
            };
            var before = new Dictionary<string, string>();
            foreach (var route in routes)
            {
                before[route] = Steady(await visitor.Client.GetStringAsync(route));
            }

            var recordBefore = await visitor.RecordAsync(Bill);
            var candidatesBefore = await CandidatesAsync();
            var (member, _) = await Factory.MemberAsync("Daug Komentuojanti");
            var (agreeing, _) = await Factory.MemberAsync("Daug Sutinkanti");
            for (var i = 0; i < 3; i++)
            {
                var id = await member.CommentOkAsync("reason", bill.ReasonId, "Komentaras nr. " + i);
                (await agreeing.AgreeAsync(id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            await member.CommentOkAsync("statement", bill.StatementId, "Komentaras po pozicija.");
            foreach (var route in routes)
            {
                var after = Steady(await visitor.Client.GetStringAsync(route));
                var at = Enumerable.Range(0, Math.Min(after.Length, before[route].Length)).FirstOrDefault(i => after[i] != before[route][i]);
                after.ShouldBe(before[route], $"{route} differs at {at}: «{before[route][Math.Max(0, at - 80)..Math.Min(before[route].Length, at + 120)]}» → «{after[Math.Max(0, at - 80)..Math.Min(after.Length, at + 120)]}»");
            }

            var record = await visitor.RecordAsync(Bill);
            (record.Counts, record.Threshold).ShouldBe((recordBefore.Counts, recordBefore.Threshold));
            record.Reasons.Select(r => (r.Id, r.Endorsements)).ShouldBe(recordBefore.Reasons.Select(r => (r.Id, r.Endorsements)), "the arguments' order and marks");
            (await CandidatesAsync()).ShouldBe(candidatesBefore, "what the summaries read");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task An_arguments_author_is_told_of_new_comments_by_others_in_one_email_without_their_text_and_not_with_the_switch_off()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (member, _) = await Factory.MemberAsync("Komentuojanti Kitam");
        try
        {
            await Database.ExecuteAsync("DELETE FROM notify.bill_event WHERE kind = 'comment'");
            await bill.ArgumentAuthor.CommentOkAsync("reason", bill.ReasonId, "Mano paties pastaba po savo argumentu.");
            (await RunJobAsync()).ShouldNotBeNull();
            (await Database.CommentNoticesAsync(bill.ArgumentAuthorEmail)).ShouldBeEmpty("one's own comment tells nobody");

            var first = await member.CommentOkAsync("reason", bill.ReasonId, "Slaptas komentaro tekstas, kurio laiške būti neturi.");
            await member.CommentOkAsync("reason", bill.ReasonId, "Dar vienas komentaras tame pačiame bėgime.");
            await RunJobAsync();
            var notice = (await Database.CommentNoticesAsync(bill.ArgumentAuthorEmail)).ShouldHaveSingleItem("two comments in one run make one email");
            var line = notice.GetProperty("arguments").EnumerateArray().ShouldHaveSingleItem();
            line.GetProperty("billNumber").GetString().ShouldBe(Bill);
            // Design 8.23.14 point 4: the link lands in the discussion, on the first new comment the email reports.
            line.GetProperty("path").GetString().ShouldBe(Lt2.Api.Contracts.Comments.BillTextLinks.ToComment(Bill, first));
            notice.GetRawText().ShouldNotContain("Slaptas komentaro tekstas");
            notice.GetRawText().ShouldNotContain("Komentuojanti Kitam");

            // Told once: the next run tells nothing new; with the switch off, nothing is sent.
            await RunJobAsync();
            (await Database.CommentNoticesAsync(bill.ArgumentAuthorEmail)).Count.ShouldBe(1);
            using (var off = await bill.ArgumentAuthor.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.Follows.FollowRoutes.Notices, new Lt2.Api.Contracts.Follows.NoticeSettingsRequest { Comments = false }))
            {
                off.StatusCode.ShouldBe(HttpStatusCode.NoContent, await off.Content.ReadAsStringAsync());
            }

            await member.CommentOkAsync("reason", bill.ReasonId, "Po išjungimo.");
            await RunJobAsync();
            (await Database.CommentNoticesAsync(bill.ArgumentAuthorEmail)).Count.ShouldBe(1);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task The_export_holds_a_persons_comments_and_marks_and_deleting_the_account_removes_them_and_keeps_the_mps()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (person, _) = await Factory.MemberAsync("Išeinanti Narė");
        var (other, _) = await Factory.MemberAsync("Lieka Narys");
        var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var mpDeleted = false;
        try
        {
            var own = await person.CommentOkAsync("reason", bill.ReasonId, "Mano komentaras eksportui.");
            var others = await other.CommentOkAsync("reason", bill.ReasonId, "Kito komentaras.");
            (await person.AgreeAsync(others)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await other.AgreeAsync(own)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var export = await person.Client.GetStringAsync(ApiRoutes.Me.Export);
            using (var document = JsonDocument.Parse(export))
            {
                var appeals = document.RootElement.GetProperty("appeals");
                appeals.GetProperty("comments").EnumerateArray().Single().GetProperty("text").GetString().ShouldBe("Mano komentaras eksportui.");
                appeals.GetProperty("commentAgreements").EnumerateArray().Single().GetProperty("comment").GetGuid().ShouldBe(others);
            }

            export.ShouldNotContain("Kito komentaras.", Case.Sensitive, "another person's words are not in the export");
            await person.DeleteAccountAsync();
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE id = @c", ("c", own))).ShouldBe(0);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment_agreement WHERE comment_id = @c", ("c", others))).ShouldBe(0);

            // A comment in the MP's name stays without the account that wrote it (design 8.23.7).
            var response = (await mp.Browser.RespondOkAsync(Bill, "for", "Balsuosiu už.")).Id;
            var asMp = await mp.Browser.CommentOkAsync("response", response, "Seimo nario komentaras.");
            await mp.Browser.DeleteAccountAsync();
            mpDeleted = true;
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE id = @c AND author_user_id IS NULL", ("c", asMp))).ShouldBe(1);
            using var reader = NewBrowser();
            (await reader.ThreadAsync("response", response)).Items.Single().Author.Voice.ShouldBe("mp");
        }
        finally
        {
            if (!mpDeleted)
            {
                await mp.Browser.DeleteAccountAsync();
            }

            mp.Dispose();
            await Database.FreeBillAsync(Bill);
        }
    }
    [Fact]
    public async Task The_moderators_list_page_shows_a_reported_comment_and_the_hidden_list_a_hidden_one()
    {
        var sink = new CollectingSink();
        await using var logged = new LoggingFactory(Database, sink);
        var moderator = await StaffMembers.ActiveAsync(logged, Database, "moderator");
        using var _s = moderator;
        var bill = await logged.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (writer, _) = await logged.MemberAsync("Puslapyje Rodoma");
        try
        {
            var comment = await writer.CommentOkAsync("statement", bill.StatementId, "Komentaras moderatorių sąraše.");
            await bill.Representative.ReportCommentAsync(comment);
            using var page = await moderator.Browser.Client.GetAsync("/moderavimas?skiltis=komentarai");
            page.StatusCode.ShouldBe(HttpStatusCode.OK, string.Join("\n", sink.Lines.Where(l => l.Contains("xception", StringComparison.Ordinal)).Take(3)));
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            html.ShouldContain("Komentaras moderatorių sąraše.");
            html.ShouldNotContain("Tomas Atstovas", Case.Sensitive, "who reported is never shown");
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, comment);
            var hidden = WebUtility.HtmlDecode(await moderator.Browser.Client.GetStringAsync("/moderavimas?skiltis=paslepti"));
            hidden.ShouldContain("Komentaras moderatorių sąraše.");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    /// <summary>A run late enough that its comments' window (which stops two minutes before the run, design 8.23.14) holds what was just written.</summary>
    private static readonly TimeSpan FollowRulesMargin = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A page without what changes on every request: the request-forgery token, a script's nonce and the islands' markers with
    /// their protected state (which the islands' visible markup, compared, renders).
    /// </summary>
    /// <summary>
    /// A page or answer without what changes on every request, and without a bill's last activity (owner, 2026-11-17: a comment is
    /// the bill's activity, design 8.12.5; only the "naujausia" order reads it, and these comparisons are of the others).
    /// </summary>
    private static string Steady(string html) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(html, "(name=\"__RequestVerificationToken\" value=\"|nonce=\")[^\"]*\"", "$1\""),
                "<!--Blazor[^>]*-->",
                "<!--Blazor-->"),
            "\"lastActivityAt\":(\"[^\"]*\"|null)",
            "\"lastActivityAt\":…");

    /// <summary>Each thread's list of comments as the server rendered it.</summary>
    private static List<string> CommentLists(string html) =>
        System.Text.RegularExpressions.Regex.Matches(html, "<ol class=\"stack stack--tight comments__list\" id=\"comments-[0-9a-f]+\">.*?</ol>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => m.Value).ToList();

    private async Task<FollowRunOutcome> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(DateTimeOffset.UtcNow + FollowRulesMargin, TestContext.Current.CancellationToken);
    }

    private async Task<string> CandidatesAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var candidates = await scope.ServiceProvider.GetRequiredService<IDigestSource>().CandidatesAsync(CancellationToken.None);
        return JsonSerializer.Serialize(candidates.Where(c => c.BillNumber == Bill).ToList());
    }
}
