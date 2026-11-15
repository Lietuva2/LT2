using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.2 (decided 2026-11-13): an author withdraws their own argument while the record is open; the per-person
/// limits are runtime settings, off by default, and a limit switched on later removes nothing and counts only shown or
/// waiting arguments.
/// </summary>
public sealed class ReasonWithdrawalTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly TimeZoneInfo Vilnius = Lt2.SharedKernel.VilniusTime.Zone;

    private static Task<HttpResponseMessage> WithdrawAsync(TestBrowser browser, Guid reasonId) =>
        browser.SendAsync(HttpMethod.Delete, AppealRoutes.ReasonOf(reasonId));

    [Fact]
    public async Task An_author_withdraws_their_own_argument_with_its_marks_nobody_else_can_and_a_made_summary_keeps_its_text()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        await admin.SetSettingAsync("Digest:ThresholdMinimum", 1);
        var monday = ReasonsToComeSteps.MondayInWeeks(14);
        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            var record = await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against", threshold: 1);
            var (author, _) = await Factory.MemberAsync("Autorė Atsiimanti", consent: false);
            using var _a = author;
            const string Text = "Šį argumentą autorė vėliau atsiims.";
            using (var written = await author.WriteReasonAsync(AppealSteps.ThirdBill, "for", Text))
            {
                written.StatusCode.ShouldBe(HttpStatusCode.Created);
            }

            var reasonId = (await author.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single(r => r.Text == Text).Id;
            var (marker, _) = await Factory.MemberAsync();
            using var _m = marker;
            (await marker.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await marker.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await marker.PostAsync(AppealRoutes.Reports, new ReportRequest { TargetKind = "reason", TargetId = reasonId, Ground = "spam" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // A summary made before the withdrawal (a dry run in a week of its own) holds the seen argument.
            await Database.ExecuteAsync("UPDATE appeals.reason SET seen_at = now() WHERE record_id = @a", ("a", record.Id));
            await Database.ExecuteAsync(
                "INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, first_seen, last_seen) VALUES (gen_random_uuid(), 'XVP-0618', @d, 'adoption', 'agenda', now(), now())",
                ("d", monday.AddDays(3)));
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var local = monday.ToDateTime(new TimeOnly(7, 5));
                var made = await scope.ServiceProvider.GetRequiredService<IDigests>().RunDueAsync(new DateTimeOffset(local, Vilnius.GetUtcOffset(local)), CancellationToken.None);
                made.SelectMany(r => r.Items).Single(i => i.BillNumber == "XVP-0618").Reasons.Select(r => r.Text).ShouldContain(Text);
            }

            // Nobody else: another member, the initiator, a moderator; a stranger is not signed in.
            foreach (var other in new[] { marker, initiator })
            {
                using var refused = await WithdrawAsync(other, reasonId);
                await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "reason.author-only");
            }

            (await WithdrawAsync(moderator, reasonId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "a moderator hides, never withdraws for the author");

            using (var stranger = NewBrowser())
            {
                (await stranger.Client.DeleteAsync(AppealRoutes.ReasonOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            (await WithdrawAsync(author, reasonId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            (await marker.RecordAsync(AppealSteps.ThirdBill)).Reasons.ShouldNotContain(r => r.Id == reasonId);
            (await marker.MineAsync(AppealSteps.ThirdBill)).Endorsed.ShouldBeEmpty("the marks go with it");
            (await author.MineAsync(AppealSteps.ThirdBill)).MyReasons.ShouldBeEmpty();
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.reason WHERE id = @r", ("r", reasonId))).ShouldBe(0);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.endorsement WHERE reason_id = @r", ("r", reasonId))).ShouldBe(0);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.report WHERE target_id = @r AND status = 'open'", ("r", reasonId))).ShouldBe(0, "its open reports are closed");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_item WHERE reasons::text LIKE @t", ("t", "%" + Text + "%"))).ShouldBe(1, "a summary already made keeps its frozen text");

            // Audited, without the text or the side.
            var details = await Database.ScalarAsync<string>(
                "SELECT details::text FROM audit.event WHERE action = 'reason.withdrawn' AND target = @t AND actor_role = 'member'", ("t", "reason:" + reasonId));
            details.ShouldNotBeNull("the withdrawal is audited");
            details.ShouldContain("XVP-0618");
            details.ShouldNotContain("atsiims");
            details.ShouldNotContain("\"for\"");
            details.ShouldNotContain("side");

            (await WithdrawAsync(author, reasonId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            // On a closed record an argument stays (proposed in design 8.2): the initiator's first one.
            var first = (await initiator.MineAsync(AppealSteps.ThirdBill)).MyReasons.Single().Id;
            await Database.ExecuteAsync("UPDATE appeals.bill_record SET status = 'closed', closed_at = now() WHERE id = @a", ("a", record.Id));
            using var closed = await WithdrawAsync(initiator, first);
            await closed.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.not-open");
        }
        finally
        {
            await admin.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number = 'XVP-0618'");
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    [Fact]
    public async Task A_limit_switched_on_later_removes_nothing_and_counts_only_shown_or_waiting_arguments()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against");
            var (writer, _) = await Factory.MemberAsync();
            using var _w = writer;

            // No limit by default: three arguments, and no counter.
            foreach (var text in new[] { "Pirmas argumentas apie mokesčius.", "Antras argumentas apie biudžetą.", "Trečias argumentas apie įmones." })
            {
                (await writer.WriteReasonAsync(AppealSteps.ThirdBill, "for", text)).StatusCode.ShouldBe(HttpStatusCode.Created);
            }

            (await writer.MineAsync(AppealSteps.ThirdBill)).ReasonsLeft.ShouldBeNull();

            await admin.SetSettingAsync("Appeals:ReasonsPerPerson", 2);
            (await writer.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules)).ReasonsPerPerson.ShouldBe(2);
            var mine = await writer.MineAsync(AppealSteps.ThirdBill);
            mine.ReasonsLeft.ShouldBe(0);
            mine.MyReasons.Count.ShouldBe(3, "nothing already written is removed");
            using (var over = await writer.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Ketvirtas argumentas."))
            {
                await over.ShouldBeProblemAsync(HttpStatusCode.Conflict, "reason.limit");
            }

            // A hidden argument no longer counts: 2 left counted, still at the limit.
            var ids = mine.MyReasons.Select(r => r.Id).ToList();
            (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, ids[0]), new StatementRequest { Statement = "Kartojasi." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await writer.MineAsync(AppealSteps.ThirdBill)).ReasonsLeft.ShouldBe(0);

            // A withdrawn one is gone: 1 counted, so one more may be written.
            (await WithdrawAsync(writer, ids[1])).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await writer.MineAsync(AppealSteps.ThirdBill)).ReasonsLeft.ShouldBe(1);
            (await writer.WriteReasonAsync(AppealSteps.ThirdBill, "against", "Naujas argumentas po atsiėmimo.")).StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        finally
        {
            await admin.ResetSettingAsync("Appeals:ReasonsPerPerson");
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }
}
