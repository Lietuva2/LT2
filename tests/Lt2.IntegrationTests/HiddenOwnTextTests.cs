using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Organisations;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Owner, 2026-11-17 (design 8.3): the author of a hidden argument or comment, and the representatives of an organisation whose
/// statement is hidden, find it in their own never-cached answer with the moderator's statement, to see it in its place; another
/// member's answer and every public answer never hold it. The test frees its bill.
/// </summary>
public sealed class HiddenOwnTextTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    [Fact]
    public async Task Only_the_author_gets_their_hidden_argument_comment_and_statement_back_with_the_moderators_statement()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator, Bill);
        var (other, _) = await Factory.MemberAsync("Kitas Narys");
        using var _o = other;
        using var visitor = NewBrowser();
        try
        {
            var comment = await bill.ArgumentAuthor.CommentOkAsync("statement", bill.StatementId, "Komentaras, kurį paslėps moderatorius.");
            await moderator.ActOnCommentAsync(CommentRoutes.Hide, comment, "Komentare – asmens duomenys.");
            (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, bill.ReasonId), new StatementRequest { Statement = "Argumentas įžeidžia kitus." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await moderator.ActAsync(OrganisationRoutes.HideStatement, bill.StatementId, "Pareiškimas – reklama.");

            // The author: the argument with its status and the statement; the comment with its statement.
            var mine = await bill.ArgumentAuthor.MineAsync(Bill);
            mine.MyReasons.Single(r => r.Id == bill.ReasonId).Status.ShouldBe("hidden");
            mine.HiddenStatements.ShouldBe(new Dictionary<Guid, string> { [bill.ReasonId] = "Argumentas įžeidžia kitus." });
            var myComments = await bill.ArgumentAuthor.MyCommentsAsync(Bill);
            var hidden = myComments.Hidden.ShouldHaveSingleItem();
            (hidden.Id, hidden.Kind, hidden.TargetId, hidden.Text, hidden.Statement)
                .ShouldBe((comment, "statement", bill.StatementId, "Komentaras, kurį paslėps moderatorius.", "Komentare – asmens duomenys."));

            // The representative: the statement with the moderator's statement.
            var represented = (await bill.Representative.GetJsonAsync<List<MyBillOrganisationResponse>>(OrganisationRoutes.MineOnBillOf(Bill))).Single();
            (represented.Statement!.Status, represented.HiddenStatement).ShouldBe(("hidden", "Pareiškimas – reklama."));

            // Nobody else: another member's own answers, and the public answers.
            var others = await other.MineAsync(Bill);
            others.MyReasons.ShouldBeEmpty();
            others.HiddenStatements.ShouldBeEmpty();
            (await other.MyCommentsAsync(Bill)).Hidden.ShouldBeEmpty();
            (await other.GetJsonAsync<List<MyBillOrganisationResponse>>(OrganisationRoutes.MineOnBillOf(Bill))).ShouldBeEmpty();
            foreach (var reader in new[] { other, visitor })
            {
                (await reader.RecordAsync(Bill)).Reasons.ShouldNotContain(r => r.Id == bill.ReasonId);
                var page = WebUtility.HtmlDecode(await reader.Client.GetStringAsync("/seimas/" + Bill));
                page.ShouldNotContain("Komentaras, kurį paslėps moderatorius.");
                page.ShouldNotContain("Argumentas įžeidžia kitus.");
                page.ShouldNotContain("Komentare – asmens duomenys.");
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
