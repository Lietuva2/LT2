using System.Net;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>Design 8.1: the appeal closes when the Seimas takes the final vote; the counts are frozen and the result shown.</summary>
public sealed class AppealClosingTests(DatabaseFixture database) : ApiTest(database)
{
    private const long FinalVote = 99199618;

    [Fact]
    public async Task The_final_vote_closes_the_appeal_freezes_the_counts_and_the_page_shows_how_each_mp_voted()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.OpenPublishedAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        var (member, memberEmail) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = Factory.Services.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>();
        (await maintenance.CloseFinishedAsync(CancellationToken.None)).ShouldBe(0, "no final vote yet");

        // The imported data gets the adoption-stage result (as the import would store it), with the same MPs' ballots.
        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT @id, agenda_item_id, bill_id, held_at + interval '7 days', 'Priėmimas', type, 'StageResult', 'Adoption', false, true, "for", against, abstain, voted, seats
            FROM parliament.vote WHERE id = 99100102
            """,
            ("id", FinalVote));
        await Database.ExecuteAsync("INSERT INTO parliament.ballot (vote_id, person_id, faction_label, faction_id, choice) SELECT @id, person_id, faction_label, faction_id, choice FROM parliament.ballot WHERE vote_id = 99100102", ("id", FinalVote));
        try
        {
            (await maintenance.CloseFinishedAsync(CancellationToken.None)).ShouldBe(1);
            (await maintenance.CloseFinishedAsync(CancellationToken.None)).ShouldBe(0, "closed once");

            using var stranger = NewBrowser();
            var closed = await stranger.AppealAsync(AppealSteps.ThirdBill);
            closed.Status.ShouldBe("closed");
            closed.ClosingVoteId.ShouldBe(FinalVote);
            closed.Counts.For.ShouldBe(1);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'appeal.closed' AND target = @t", ("t", "appeal:" + appeal.Id))).ShouldBe(1);

            using (var late = await member.TakeSideAsync(AppealSteps.ThirdBill, "against"))
            {
                await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, "appeal.not-open");
            }

            // A position removed later (an account deleted) does not change the frozen numbers.
            await Database.ExecuteAsync("DELETE FROM appeals.position WHERE user_id = @u", ("u", await Database.UserIdAsync(memberEmail)));
            (await stranger.AppealAsync(AppealSteps.ThirdBill)).Counts.For.ShouldBe(1);

            using var page = await stranger.Client.GetAsync("/seimas/XVP-0618/kreipimasis");
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            html.ShouldContain("Seimas balsavo galutinai; skaičiai užfiksuoti");
            html.ShouldContain("Seimo sprendimas");
            html.ShouldContain("Kaip balsavo kiekvienas Seimo narys");
            html.ShouldContain("Jonas Petraitis");
            html.ShouldContain("class=\"seatmap\"");
            html.ShouldNotContain("id=\"side-for\"", customMessage: "no side can be taken any more");
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM parliament.ballot WHERE vote_id = @id", ("id", FinalVote));
            await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @id", ("id", FinalVote));
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }
}