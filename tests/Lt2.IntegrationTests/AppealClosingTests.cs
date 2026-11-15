using System.Net;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>Design 8.1: the record closes when the Seimas takes the final vote; the counts are frozen and the result shown.</summary>
public sealed class AppealClosingTests(DatabaseFixture database) : ApiTest(database)
{
    private const long FinalVote = 99199618;

    [Fact]
    public async Task The_final_vote_closes_the_record_freezes_the_counts_and_the_page_shows_how_each_mp_voted()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (initiator, _) = await Factory.MemberAsync();
        using var _i = initiator;
        var record = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
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
            var closed = await stranger.RecordAsync(AppealSteps.ThirdBill);
            closed.Status.ShouldBe("closed");
            closed.ClosingVoteId.ShouldBe(FinalVote);
            closed.Counts.For.ShouldBe(1);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'record.closed' AND target = @t", ("t", "record:" + record.Id))).ShouldBe(1);

            using (var late = await member.TakeSideAsync(AppealSteps.ThirdBill, "against"))
            {
                await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.not-open");
            }

            // A position removed later (an account deleted) does not change the frozen numbers.
            await Database.ExecuteAsync("DELETE FROM appeals.position WHERE user_id = @u", ("u", await Database.UserIdAsync(memberEmail)));
            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Counts.For.ShouldBe(1);

            using var page = await stranger.Client.GetAsync("/seimas/XVP-0618");
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

    /// <summary>
    /// Design 6.8: a finished bill is finished for everyone at the same moment. Once the final vote is recorded, taking part
    /// is refused as an organisation's statement already is, before the hourly job has closed the record.
    /// </summary>
    [Fact]
    public async Task Taking_part_ends_when_the_final_vote_is_recorded_before_the_job_closes_the_record()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        await Database.PrepareBillAsync(author, moderator.Browser, AppealSteps.ThirdBill, "against");
        var reasonId = (await author.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await Database.ExecuteAsync(
            """
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT @id, agenda_item_id, bill_id, held_at + interval '7 days', 'Priėmimas', type, 'StageResult', 'Adoption', false, true, "for", against, abstain, voted, seats
            FROM parliament.vote WHERE id = 99100102
            """,
            ("id", FinalVote));
        try
        {
            using var stranger = NewBrowser();
            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Status.ShouldBe("open", "the job has not run");

            foreach (var refused in new[]
            {
                await member.TakeSideAsync(AppealSteps.ThirdBill, "for"),
                await member.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.AppealRoutes.PositionOf(AppealSteps.ThirdBill)),
                await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Per vėlu, Seimas jau nubalsavo."),
                await member.SendAsync(HttpMethod.Put, Lt2.Api.Contracts.AppealRoutes.EndorsementOf(reasonId)),
                await author.SendAsync(HttpMethod.Delete, Lt2.Api.Contracts.AppealRoutes.ReasonOf(reasonId)),
            })
            {
                using (refused)
                {
                    await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.not-open");
                }
            }

            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Counts.Against.ShouldBe(2, "nothing changed after the final vote");
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @id", ("id", FinalVote));
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }
}