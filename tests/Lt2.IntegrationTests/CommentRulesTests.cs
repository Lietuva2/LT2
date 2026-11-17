using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.23 where the review of 2026-11-16 found gaps: a hidden comment is the moderators' (its author cannot delete it), a
/// comment reported is checked shown, the ceiling per person per text (also at once), an MP whose mandate ended, the notices of
/// a hidden organisation's or MP's comment, a merge and a hidden record, and the last representative's account.
/// </summary>
public sealed class CommentRulesTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    private Task<long> NoticesAsync(string kind, string to, string notice) =>
        Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = @k AND lower(payload->>'to') = lower(@to) AND payload->>'notice' = @n", ("k", kind), ("to", to), ("n", notice))!;

    [Fact]
    public async Task A_hidden_comment_cannot_be_deleted_by_its_author_and_is_not_offered_as_deletable()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (writer, _) = await Factory.MemberAsync("Paslėpto Autorė");
        using var _w = writer;
        try
        {
            var comment = await writer.CommentOkAsync("reason", bill.ReasonId, "Paslėpsimas komentaras.");
            (await writer.MyCommentsAsync(Bill)).Deletable.ShouldContain(comment, "deletable while shown");
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, comment);
            (await writer.MyCommentsAsync(Bill)).Deletable.ShouldNotContain(comment);
            await (await writer.DeleteCommentAsync(comment)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.hidden");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE id = @c", ("c", comment))).ShouldBe(1, "kept for the moderators");
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_report_needs_a_shown_comment_and_a_hidden_record_hides_its_threads()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (writer, _) = await Factory.MemberAsync("Rašanti Narė");
        var (reporter, _) = await Factory.MemberAsync("Pranešanti Narė");
        using var _w = writer;
        using var _r = reporter;
        using var visitor = NewBrowser();
        try
        {
            var comment = await writer.CommentOkAsync("reason", bill.ReasonId, "Komentaras po argumentu.");
            using (var hid = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, bill.ReasonId), new StatementRequest { Statement = "Bandymas." }))
            {
                hid.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            using (var report = await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "comment", TargetId = comment, Ground = "insult", Text = "Įžeidžia." }))
            {
                await report.ShouldBeProblemAsync(HttpStatusCode.NotFound, "report.target-not-found");
            }

            using (var restored = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.RestoreReason, bill.ReasonId), new StatementRequest { Statement = "Bandymas." }))
            {
                restored.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            await reporter.ReportCommentAsync(comment);

            // A hidden record hides every thread on it, the statement's too.
            var recordId = await moderator.Browser.RecordIdAsync(Bill);
            using (var hidden = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.HideRecord, recordId), new StatementRequest { Statement = "Bandymas: paslėpta." }))
            {
                hidden.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            (await visitor.Client.GetAsync(CommentRoutes.ThreadOf("reason", bill.ReasonId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await visitor.Client.GetAsync(CommentRoutes.ThreadOf("statement", bill.StatementId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task The_ceiling_per_person_per_text_holds_also_for_two_writes_at_once()
    {
        var moderator = await ModeratorAsync();
        var admin = await AdministratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (writer, _) = await Factory.MemberAsync("Ribą Pasiekianti");
        using var _w = writer;
        try
        {
            await admin.Browser.SetSettingAsync(SettingKeys.CommentsPerPersonPerTarget.Name, 2);
            await writer.CommentOkAsync("reason", bill.ReasonId, "Pirmas.");
            var at = await Task.WhenAll(writer.CommentAsync("reason", bill.ReasonId, "Antras."), writer.CommentAsync("reason", bill.ReasonId, "Trečias."));
            at.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1, "one of the two at once is the second, the other over the ceiling");
            foreach (var response in at)
            {
                response.Dispose();
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE target_id = @t", ("t", bill.ReasonId))).ShouldBe(2);
            await (await writer.CommentAsync("reason", bill.ReasonId, "Ketvirtas.")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "comment.limit");
        }
        finally
        {
            await admin.Browser.ResetSettingAsync(SettingKeys.CommentsPerPersonPerTarget.Name);
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task An_mp_whose_mandate_ended_writes_no_comment_as_the_mp_and_a_hidden_mp_comment_tells_the_mps_account()
    {
        await Database.FreeBillAsync(Bill);
        var moderator = await ModeratorAsync();
        using var mp = await Factory.MpAccountAsync(MpSteps.Ievaite);
        var mandate = await Database.ScalarAsync<string>(
            "SELECT \"to\"::text FROM parliament.mandate WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)", ("p", MpSteps.Ievaite.SeimasPersonId));
        try
        {
            var response = (await mp.Browser.RespondOkAsync(Bill, "for", "Balsuosiu už.")).Id;
            var asMp = await mp.Browser.CommentOkAsync("response", response, "Seimo nario paaiškinimas.");
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, asMp);
            (await NoticesAsync("email.mp-response-notice", mp.Email, "comment-hidden")).ShouldBe(1, "the MP's account is told");

            await Database.ExecuteAsync(
                "UPDATE parliament.mandate SET \"to\" = current_date - 1 WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)", ("p", MpSteps.Ievaite.SeimasPersonId));
            await (await mp.Browser.CommentAsync("response", response, "Po kadencijos.")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "response.mandate-ended");
        }
        finally
        {
            await Database.ExecuteAsync(
                "UPDATE parliament.mandate SET \"to\" = @to::date WHERE person_id = (SELECT id FROM parliament.person WHERE seimas_person_id = @p)", ("to", (object?)mandate ?? DBNull.Value), ("p", MpSteps.Ievaite.SeimasPersonId));
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_hidden_organisation_comment_tells_every_representative_and_the_last_ones_deletion_takes_the_comments_with_the_statements()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (second, secondEmail) = await Factory.MemberAsync("Antras Atstovas");
        using var _s = second;
        try
        {
            using (var invited = await bill.Representative.PostAsync(OrganisationRoutes.InvitesOf(bill.OrganisationSlug), new { }))
            {
                invited.StatusCode.ShouldBe(HttpStatusCode.OK, await invited.Content.ReadAsStringAsync());
                var invite = await invited.ReadAsync<InviteCreatedResponse>();
                using var accepted = await second.PostAsync(OrganisationRoutes.AcceptInvite, new InviteTokenRequest { Token = invite.Token });
                accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
            }

            var asOrganisation = await bill.Representative.CommentOkAsync("statement", bill.StatementId, "Organizacijos vardu.");
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, asOrganisation);
            (await Database.OrganisationNoticesAsync(secondEmail)).ShouldContain("comment-hidden", "every current representative is told");
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Restore, asOrganisation);

            // The last representative's account deleted: the organisation closes, its statements and their comments go
            // (design 8.14.14, 8.23.7; section 12 keeps this open).
            await second.DeleteAccountAsync();
            await bill.Representative.DeleteAccountAsync();
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE id = @c", ("c", asOrganisation))).ShouldBe(0);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_merged_argument_takes_its_comments_with_it()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (writer, _) = await Factory.MemberAsync("Antrą Argumentą Rašanti");
        using var _w = writer;
        try
        {
            using (var written = await writer.WriteReasonAsync(Bill, "against", "Tas pats argumentas kitais žodžiais apie pataisą."))
            {
                written.StatusCode.ShouldBe(HttpStatusCode.Created);
            }

            var duplicate = (await writer.RecordAsync(Bill)).Reasons.Single(r => r.Id != bill.ReasonId && r.Side == "against").Id;
            await writer.CommentOkAsync("reason", duplicate, "Komentaras po dublikatu.");
            using (var merged = await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.MergeReason, duplicate), new MergeReasonRequest { IntoReasonId = bill.ReasonId, Statement = "Tas pats argumentas." }))
            {
                merged.StatusCode.ShouldBe(HttpStatusCode.NoContent, await merged.Content.ReadAsStringAsync());
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.comment WHERE target_id = @t", ("t", duplicate))).ShouldBe(0);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
