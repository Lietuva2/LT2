using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Owner, 2026-11-17 ("I can now 'Man svarbu' on my own argument"): the marks choose the arguments sent to MPs, so an
/// argument's author cannot mark it, checked where the mark is written; and a merge does not move a mark onto its giver's own
/// argument.
/// </summary>
public sealed class OwnEndorsementTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    private static async Task<Guid> WriteAsync(TestBrowser writer, string text)
    {
        using var written = await writer.WriteReasonAsync(Bill, "for", text);
        written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
        return (await written.ReadAsync<ReasonCreatedResponse>()).Id;
    }

    private static async Task SideAsync(TestBrowser member)
    {
        using var took = await member.TakeSideAsync(Bill, "for");
        took.StatusCode.ShouldBe(HttpStatusCode.NoContent, await took.Content.ReadAsStringAsync());
    }

    private Task<long> MarksAsync(Guid reason) => Database.ScalarAsync<long>("SELECT count(*) FROM appeals.endorsement WHERE reason_id = @r", ("r", reason))!;

    [Fact]
    public async Task An_author_cannot_mark_their_own_argument_and_another_member_of_the_side_can()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (author, _) = await Factory.MemberAsync("Argumento Autorė");
        var (other, _) = await Factory.MemberAsync("Kita Narė");
        using var _a = author;
        using var _o = other;
        try
        {
            await Database.PrepareBillAsync(author, moderator, Bill, "for");
            var own = (await author.RecordAsync(Bill)).Reasons.ShouldHaveSingleItem().Id;

            using (var refused = await author.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(own)))
            {
                await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "endorsement.own-reason");
            }

            (await MarksAsync(own)).ShouldBe(0);
            await SideAsync(other);
            (await other.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(own))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await MarksAsync(own)).ShouldBe(1, "someone else's mark on the same argument is taken");
            (await other.RecordAsync(Bill)).Reasons.Single(r => r.Id == own).Endorsements.ShouldBe(1);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_merge_drops_the_mark_that_would_land_on_its_givers_own_argument_and_moves_the_others()
    {
        var moderator = (await ModeratorAsync()).Browser;
        var (keptAuthor, _) = await Factory.MemberAsync("Paliekamo Autorė");
        var (duplicateAuthor, _) = await Factory.MemberAsync("Dublikato Autorius");
        var (marker, _) = await Factory.MemberAsync("Žymintis Narys");
        using var _k = keptAuthor;
        using var _d = duplicateAuthor;
        using var _m = marker;
        try
        {
            await Database.PrepareBillAsync(keptAuthor, moderator, Bill, "for");
            var kept = (await keptAuthor.RecordAsync(Bill)).Reasons.ShouldHaveSingleItem().Id;
            var duplicate = await WriteAsync(duplicateAuthor, "Beveik tas pats argumentas už, kitais žodžiais.");

            // The kept argument's author and another member mark the duplicate.
            await SideAsync(marker);
            (await keptAuthor.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(duplicate))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await marker.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(duplicate))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await MarksAsync(duplicate)).ShouldBe(2, "both marks exist before the merge");

            using (var merged = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.MergeReason, duplicate), new MergeReasonRequest { IntoReasonId = kept, Statement = "Tas pats argumentas." }))
            {
                merged.StatusCode.ShouldBe(HttpStatusCode.NoContent, await merged.Content.ReadAsStringAsync());
            }

            (await MarksAsync(kept)).ShouldBe(1, "the other member's mark moved; the author's own would have landed on their own argument");
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM appeals.endorsement e JOIN appeals.reason r ON r.id = e.reason_id WHERE e.reason_id = @r AND e.user_id = r.author_user_id", ("r", kept))).ShouldBe(0);
            (await marker.RecordAsync(Bill)).Reasons.Single(r => r.Id == kept).Endorsements.ShouldBe(1);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }
}
