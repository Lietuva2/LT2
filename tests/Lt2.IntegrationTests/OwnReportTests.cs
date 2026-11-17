using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Owner, 2026-11-17 (design 8.3): nobody reports their own text. The server refuses it (<c>report.own</c>) for every report
/// target with an author: an argument, a comment (in any voice), an organisation's statement (its representatives), an MP's
/// response (the same rule as a statement's shared voice, <c>NewsRules.OwnResponse</c>) and a public profile; someone else's is
/// reported as before. The test frees its bill.
/// </summary>
public sealed class OwnReportTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    private static Task<HttpResponseMessage> ReportAsync(TestBrowser browser, string kind, Guid id) =>
        browser.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = kind, TargetId = id, Ground = "spam" });

    [Fact]
    public async Task Nobody_reports_their_own_argument_comment_statement_or_profile_and_others_still_can()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator, Bill);
        var (other, _) = await Factory.MemberAsync("Kitas Narys");
        using var _o = other;
        try
        {
            var comment = await bill.ArgumentAuthor.CommentOkAsync("statement", bill.StatementId, "Autorės komentaras po pareiškimu.");
            var organisationComment = await bill.Representative.CommentOkAsync("statement", bill.StatementId, "Asociacijos komentaras.");
            foreach (var (author, kind, id) in new[]
                     {
                         (bill.ArgumentAuthor, "reason", bill.ReasonId),
                         (bill.ArgumentAuthor, "comment", comment),
                         (bill.Representative, "statement", bill.StatementId),
                         (bill.Representative, "comment", organisationComment),
                     })
            {
                using (var refused = await ReportAsync(author, kind, id))
                {
                    await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "report.own");
                    (await refused.ProblemAsync()).GetProperty("detail").GetString().ShouldBe("Savo teksto pranešti negalima.");
                }

                (await ReportAsync(other, kind, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent, kind);
            }

            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM appeals.report WHERE target_id = ANY(@ids) AND status = 'open'",
                ("ids", new[] { bill.ReasonId, comment, bill.StatementId, organisationComment }))).ShouldBe(4, "the other member's four, none of the authors'");

            // A public profile: not its own member's.
            var (member, _, profileId) = await Factory.PublicMemberAsync("Narė Su Profiliu");
            using var _m = member;
            using (var own = await member.PostAsync(ProfileRoutes.ReportOf(profileId), new MemberReportRequest { Ground = "spam" }))
            {
                await own.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "report.own");
            }

            (await other.PostAsync(ProfileRoutes.ReportOf(profileId), new MemberReportRequest { Ground = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
