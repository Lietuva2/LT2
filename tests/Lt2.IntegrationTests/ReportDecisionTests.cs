using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Owner, 2026-11-17 (Digital Services Act art. 16(5)): when a moderator decides on reported content, every member whose report
/// was open gets one email with the decision ("paliktas" with the optional note, "paslėptas" with the statement); the author gets
/// what they got before ("Palikti" sends the author nothing); the email names no moderator. A text its author withdraws tells its
/// reporters so. The test frees its bill.
/// </summary>
public sealed class ReportDecisionTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    /// <summary>The report decisions in the outbox to <paramref name="to"/>, oldest first.</summary>
    private async Task<List<JsonElement>> DecisionsAsync(string to)
    {
        var rows = await Database.ScalarAsync<string>(
            "SELECT coalesce(json_agg(payload ORDER BY created_at, id), '[]'::json)::text FROM notify.outbox WHERE kind = 'email.report-decided' AND lower(payload->>'to') = lower(@to)",
            ("to", to));
        using var document = JsonDocument.Parse(rows!);
        return document.RootElement.EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private Task<long> AuthorNoticesAsync(string to) =>
        Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND lower(payload->>'to') = lower(@to)", ("to", to));

    private static Task<HttpResponseMessage> ReportAsync(TestBrowser browser, string kind, Guid id) =>
        browser.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = kind, TargetId = id, Ground = "insult", Text = "Įžeidžia." });

    [Fact]
    public async Task Each_reporter_is_told_the_decision_once_with_the_explanation_and_never_the_moderator()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (first, firstEmail) = await Factory.MemberAsync("Pirmas Pranešęs");
        using var _f = first;
        var (second, secondEmail) = await Factory.MemberAsync("Antra Pranešusi");
        using var _s = second;
        try
        {
            // "Palikti" on the argument: both reporters are told, with the note; the author nothing.
            var authorBefore = await AuthorNoticesAsync(bill.ArgumentAuthorEmail);
            (await ReportAsync(first, "reason", bill.ReasonId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await ReportAsync(second, "reason", bill.ReasonId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.PublishReason, bill.ReasonId), new ModerationRequest { Statement = "Kritika – ne įžeidimas." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            foreach (var reporter in new[] { firstEmail, secondEmail })
            {
                var kept = (await DecisionsAsync(reporter)).ShouldHaveSingleItem();
                (kept.GetProperty("target").GetString(), kept.GetProperty("outcome").GetString(), kept.GetProperty("statement").GetString(), kept.GetProperty("billNumber").GetString())
                    .ShouldBe(("reason", "kept", "Kritika – ne įžeidimas.", Bill));
                kept.GetProperty("path").GetString()!.ShouldContain(bill.ReasonId.ToString());
                var text = kept.GetRawText();
                text.ShouldNotContain(moderator.Email, Case.Insensitive, "never the moderator");
                text.ShouldNotContain(firstEmail == reporter ? secondEmail : firstEmail, Case.Insensitive, "never the other reporter");
                text.ShouldNotContain(bill.ArgumentAuthorEmail, Case.Insensitive);
            }

            (await AuthorNoticesAsync(bill.ArgumentAuthorEmail)).ShouldBe(authorBefore, "keeping tells the author nothing");

            // Hiding a reported comment: its reporter is told with the statement; its author gets the statement as before.
            var (writer, writerEmail) = await Factory.MemberAsync("Komentaro Autorė");
            using var _w = writer;
            var comment = await writer.CommentOkAsync("reason", bill.ReasonId, "Komentaras, apie kurį praneš.");
            await first.ReportCommentAsync(comment);
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, comment, "Įžeidžiantis tonas.");
            var hidden = (await DecisionsAsync(firstEmail)).Last();
            (hidden.GetProperty("target").GetString(), hidden.GetProperty("outcome").GetString(), hidden.GetProperty("statement").GetString())
                .ShouldBe(("comment", "hidden", "Įžeidžiantis tonas."));
            hidden.GetProperty("path").ValueKind.ShouldBe(JsonValueKind.Null, "no link to a hidden text");
            (await Database.CommentModerationNoticesAsync(writerEmail)).ShouldBe(["comment-hidden"]);
            (await DecisionsAsync(secondEmail)).Count.ShouldBe(1, "the second member did not report the comment");

            // A statement its organisation withdraws: its reporter is told so, no moderator involved.
            (await ReportAsync(second, "statement", bill.StatementId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await bill.Representative.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.Organisations.OrganisationRoutes.StatementOf(Bill, bill.OrganisationSlug)))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var withdrawn = (await DecisionsAsync(secondEmail)).Last();
            (withdrawn.GetProperty("target").GetString(), withdrawn.GetProperty("outcome").GetString()).ShouldBe(("statement", "withdrawn"));
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
