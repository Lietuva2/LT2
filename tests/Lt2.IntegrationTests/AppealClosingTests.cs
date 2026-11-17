using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>Design 8.1: the record closes when the Seimas takes the final vote; the counts are frozen and the result shown.</summary>
public sealed class AppealClosingTests(DatabaseFixture database) : ApiTest(database)
{
    private const long FinalVote = 99199618;

    [Fact]
    public async Task The_final_vote_closes_the_record_freezes_the_counts_and_the_page_links_to_each_mps_ballot()
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
            html.ShouldContain($"<a href=\"/seimas/balsavimai/{FinalVote}#balsai\" id=\"each-mp\">Kaip balsavo kiekvienas Seimo narys</a>");
            html.ShouldContain("class=\"seatmap\"");
            var result = html[html.IndexOf("id=\"seimas-result\"", StringComparison.Ordinal)..];
            result = result[..result.IndexOf("</section>", StringComparison.Ordinal)];
            result.ShouldNotContain("<ul class=\"rows\"", customMessage: "design 8.17: the list of each MP's ballot is only on the vote's page");
            result.ShouldNotContain("href=\"/p/", customMessage: "no MP's row on the bill's page");
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

    /// <summary>
    /// Design 6.9 (owner, 2026-11-15): reports after the final vote reach the queue. On a closed record a moderator hides and
    /// restores an argument, corrects or removes the description and answers with "Palikti", each with a statement and
    /// audited, an argument's author told; the frozen counts stay. Everything else on a closed record stays refused.
    /// </summary>
    [Fact]
    public async Task After_the_final_vote_reports_reach_the_queue_and_only_the_allowed_actions_answer_them()
    {
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        var mod = moderator.Browser;
        var (author, _) = await Factory.MemberAsync();
        using var _a = author;
        var record = await Database.PrepareBillAsync(author, mod, AppealSteps.ThirdBill, "against");
        var kept = (await author.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
        var (other, otherEmail) = await Factory.MemberAsync("Giedrė Bandomoji");
        using var _o = other;
        (await other.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var hidden = await (await other.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Pajamų mokesčio tarifai turi būti progresiniai.")).ReadAsync<ReasonCreatedResponse>();
        var (marker, _) = await Factory.MemberAsync("Žyminti Narė");
        using var _k = marker;
        (await marker.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await marker.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(hidden.Id))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await AddFinalVoteAsync();
        try
        {
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                (await scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>().CloseFinishedAsync(CancellationToken.None)).ShouldBe(1);
            }

            using var stranger = NewBrowser();
            var frozen = (await stranger.RecordAsync(AppealSteps.ThirdBill)).Counts;
            var (reporter, _) = await Factory.MemberAsync("Pranešėjas Bandomasis");
            using var _r = reporter;
            foreach (var (kind, id) in new[] { ("reason", kept), ("reason", hidden.Id), ("record", record.Id) })
            {
                using var reported = await reporter.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = kind, TargetId = id, Ground = "other" });
                reported.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reported.Content.ReadAsStringAsync());
            }

            var queue = await mod.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue);
            var queuedRecord = queue.Records.Single(r => r.Id == record.Id);
            (queuedRecord.Status, queuedRecord.Reports.Count).ShouldBe(("closed", 1));
            queue.Reasons.Single(r => r.Id == kept).RecordStatus.ShouldBe("closed");
            queue.Reasons.Single(r => r.Id == hidden.Id).RecordStatus.ShouldBe("closed");
            (await mod.ModerationBillAsync(AppealSteps.ThirdBill)).Status.ShouldBe("closed");

            // Refused on a closed record: the threshold, holding the sending, merging.
            foreach (var refused in new[]
            {
                await mod.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordThreshold, record.Id), new ModeratorThresholdRequest { Threshold = 150, Statement = "Bandymas." }),
                await mod.PostAsync(AppealRoutes.With(AppealRoutes.HoldRecord, record.Id), new StatementRequest { Statement = "Bandymas." }),
                await mod.PostAsync(AppealRoutes.With(AppealRoutes.MergeReason, hidden.Id), new MergeReasonRequest { IntoReasonId = kept, Statement = "Bandymas." }),
            })
            {
                using (refused)
                {
                    await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
                }
            }

            // "Palikti" on an argument and on the description, each answering its report.
            (await mod.PostAsync(AppealRoutes.With(AppealRoutes.PublishReason, kept), new ModerationRequest { Statement = "Argumentas taisyklių nepažeidžia." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await mod.PostAsync(AppealRoutes.With(AppealRoutes.KeepRecord, record.Id), new ModerationRequest { Statement = "Aprašymas tikslus." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // Correcting and removing the description.
            using (var corrected = await mod.SendAsync(
                HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.ThirdBill), new ModeratorDescriptionRequest { Description = "Pataisytas aprašymas po galutinio balsavimo.", Statement = "Taisau klaidą." }))
            {
                corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent, await corrected.Content.ReadAsStringAsync());
            }

            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Description.ShouldBe("Pataisytas aprašymas po galutinio balsavimo.");
            using (var removed = await mod.PostAsync(AppealRoutes.RemoveDescriptionOf(AppealSteps.ThirdBill), new StatementRequest { Statement = "Aprašymas klaidino." }))
            {
                removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());
            }

            (await stranger.RecordAsync(AppealSteps.ThirdBill)).Description.ShouldBeEmpty();

            // Hiding and restoring an argument: its author is told each time; the frozen counts stay.
            (await mod.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, hidden.Id), new StatementRequest { Statement = "Pažeidžia taisykles." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var afterHide = await stranger.RecordAsync(AppealSteps.ThirdBill);
            afterHide.Reasons.ShouldNotContain(r => r.Id == hidden.Id);
            afterHide.Counts.ShouldBe(frozen);
            (await mod.PostAsync(AppealRoutes.With(AppealRoutes.RestoreReason, hidden.Id), new StatementRequest { Statement = "Klaida, grąžinu." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var afterRestore = await stranger.RecordAsync(AppealSteps.ThirdBill);
            afterRestore.Reasons.ShouldContain(r => r.Id == hidden.Id);
            afterRestore.Counts.ShouldBe(frozen);
            afterRestore.Status.ShouldBe("closed");
            foreach (var notice in new[] { "reason-hidden", "reason-restored" })
            {
                (await Database.ScalarAsync<long>(
                    "SELECT count(*) FROM notify.outbox WHERE kind = 'email.moderation-notice' AND payload->>'to' = @to AND payload->>'notice' = @n", ("to", otherEmail), ("n", notice)))
                    .ShouldBe(1, notice);
            }

            // "Palikti" on a published argument is recorded as a publish; the first one marked it seen before the vote.
            foreach (var (action, target, count) in new[]
            {
                ("moderation.reason.publish", "reason:" + kept, 2L), ("moderation.record.keep", "record:" + record.Id, 1L), ("moderation.record.undescribe", "record:" + record.Id, 1L),
                ("moderation.reason.hide", "reason:" + hidden.Id, 1L), ("moderation.reason.restore", "reason:" + hidden.Id, 1L),
            })
            {
                (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = @a AND target = @t", ("a", action), ("t", target))).ShouldBe(count, action);
            }

            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.report WHERE status = 'open' AND target_id IN (@a, @b, @c)", ("a", kept), ("b", hidden.Id), ("c", record.Id)))
                .ShouldBe(0, "every report answered");
            var after = await mod.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue);
            after.Records.ShouldNotContain(r => r.Id == record.Id);
            after.Reasons.ShouldNotContain(r => r.RecordId == record.Id);

            using var late = await other.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Dar vienas argumentas.");
            await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.not-open");
        }
        finally
        {
            await RemoveFinalVoteAsync();
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    private Task AddFinalVoteAsync() =>
        Database.ExecuteAsync(
            """
            INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
            SELECT @id, agenda_item_id, bill_id, held_at + interval '7 days', 'Priėmimas', type, 'StageResult', 'Adoption', false, true, "for", against, abstain, voted, seats
            FROM parliament.vote WHERE id = 99100102
            """,
            ("id", FinalVote));

    private Task RemoveFinalVoteAsync() => Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @id", ("id", FinalVote));
}