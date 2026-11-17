using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.19.2 (owner, 2026-11-17): a summary is prepared a day before it goes. Preparing fixes its bills and arguments and
/// tells every moderator and administrator; a staff account sees the box on every page from its own <c>GET /me</c>; hiding an
/// argument brings the next one in; a bill held during review drops out; the numbers are read when it goes; "Patvirtinti" sends
/// nothing earlier and takes the box away.
/// </summary>
public sealed class DigestReviewTests(DatabaseFixture database) : ApiTest(database)
{
    private const string First = "Pirmas argumentas už: daugiausia „Man svarbu“.";
    private const string Second = "Antras argumentas už: vienas „Man svarbu“.";
    private const string Third = "Trečias argumentas už: įtraukiamas vietoj paslėpto.";
    private const string Late = "Vėlyvas argumentas už: parašytas po paruošimo.";

    private static async Task<Guid> WriteAsync(TestBrowser writer, string text)
    {
        using var written = await writer.WriteReasonAsync(AppealSteps.ThirdBill, "for", text);
        written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
        return (await written.ReadAsync<ReasonCreatedResponse>()).Id;
    }

    private static async Task MarkAsync(TestBrowser marker, Guid reasonId) =>
        (await marker.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    [Fact]
    public async Task A_prepared_summary_is_reviewed_hiding_brings_the_next_argument_in_a_held_bill_drops_out_and_the_numbers_are_read_when_it_goes()
    {
        await Database.ForgetPreparedAsync();
        var (moderator, admin) = await StaffAsync();
        await admin.Browser.SetSettingAsync("Digest:ThresholdMinimum", 1);
        var monday = ReasonsToComeSteps.MondayInWeeks(46);
        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            var third = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);
            var second = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.SecondBill, "for", threshold: 1);
            var (writer, _) = await Factory.MemberAsync();
            using var _w = writer;
            var first = await WriteAsync(writer, First);
            var secondReason = await WriteAsync(writer, Second);
            await WriteAsync(writer, Third);
            var (marker1, _) = await Factory.MemberAsync();
            using var _m1 = marker1;
            var (marker2, _) = await Factory.MemberAsync();
            using var _m2 = marker2;
            foreach (var marker in new[] { marker1, marker2 })
            {
                (await marker.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            await MarkAsync(marker1, first);
            await MarkAsync(marker2, first);
            await MarkAsync(marker1, secondReason);
            await Database.PlanAsync(AppealSteps.ThirdBill, monday.AddDays(3));
            await Database.PlanAsync(AppealSteps.SecondBill, monday.AddDays(3));

            // Sunday 07:00: prepared; every moderator and administrator is told by email, with no argument's text.
            var since = await Database.ScalarAsync<DateTime>("SELECT clock_timestamp()");
            var prepared = (await Factory.RunDueAsync(MpSendingSteps.At(monday.AddDays(-1), 7, 5))).Single();
            prepared.Status.ShouldBe("prepared");
            var notices = await Database.ScalarAsync<string>(
                "SELECT string_agg((payload->>'bills') || '/' || (payload->>'reasons') || ' ' || payload::text, '|') FROM notify.outbox WHERE kind = 'email.digest-review' AND created_at >= @s AND lower(payload->>'to') = lower(@to)",
                ("s", since), ("to", moderator.Email));
            notices.ShouldNotBeNull("the moderator is told");

            // Other bills left eligible by earlier classes may share the week, so the counts are the run's own.
            var bills = (int)await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_item WHERE run_id = @r", ("r", prepared.Id));
            var reasons = (int)await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_choice WHERE run_id = @r", ("r", prepared.Id));
            bills.ShouldBeGreaterThanOrEqualTo(2);
            reasons.ShouldBeGreaterThanOrEqualTo(4, "two for and one against on XVP-0618, one for on XVP-0533");
            notices.ShouldStartWith($"{bills}/{reasons} ");
            notices.ShouldNotContain(First);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = 'email.digest-review' AND created_at >= @s AND lower(payload->>'to') = lower(@to)",
                ("s", since), ("to", admin.Email))).ShouldBe(1, "and the administrator");

            // The box on every page: GET /me carries it for a staff account only, never for a member.
            var me = await moderator.Browser.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root);
            me.DigestReview.ShouldNotBeNull();
            (me.DigestReview.DueAt, me.DigestReview.Bills, me.DigestReview.Reasons).ShouldBe((MpSendingSteps.At(monday, 7).ToUniversalTime(), bills, reasons));
            using (var raw = await writer.Client.GetAsync(ApiRoutes.Me.Root))
            {
                raw.StatusCode.ShouldBe(HttpStatusCode.OK);
                var member = JsonDocument.Parse(await raw.Content.ReadAsStringAsync()).RootElement;
                (member.TryGetProperty("digestReview", out var review) ? review.ValueKind : JsonValueKind.Null).ShouldBe(JsonValueKind.Null, "a member is told nothing");
                member.GetProperty("staffAccount").GetBoolean().ShouldBeFalse();
            }

            // The screen: the two most endorsed for, with their ids for "Paslėpti".
            async Task<DigestReviewItemResponse> ItemAsync(string bill) =>
                (await moderator.Browser.GetJsonAsync<DigestModerationResponse>(DigestRoutes.Moderation)).Runs.Single(r => r.Id == prepared.Id).Items.Single(i => i.BillNumber == bill);
            (await ItemAsync(AppealSteps.ThirdBill)).Reasons.Where(r => r.Side == "for").Select(r => r.Text).ShouldBe([First, Second]);

            // Hiding the first, with the normal action: hidden on the bill's page, and the next of its side comes in, marked.
            (await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, first), new StatementRequest { Statement = "Pažeidžia taisykles." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await writer.RecordAsync(AppealSteps.ThirdBill)).Reasons.ShouldNotContain(r => r.Id == first);
            var reviewed = (await ItemAsync(AppealSteps.ThirdBill)).Reasons.Where(r => r.Side == "for").ToList();
            reviewed.Select(r => (r.Text, r.Replacement)).ShouldBe([(Second, false), (Third, true)]);

            // After the preparation: an argument written later never comes in, a side taken later is counted when it goes.
            var late = await WriteAsync(writer, Late);
            await MarkAsync(marker2, late);
            var (newcomer, _) = await Factory.MemberAsync();
            using var _n = newcomer;
            (await newcomer.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await MarkAsync(newcomer, late);

            // A bill held during review drops out when it goes.
            (await moderator.Browser.PostAsync(AppealRoutes.With(AppealRoutes.HoldRecord, second.Id), new StatementRequest { Statement = "Tikrinama." }))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await ItemAsync(AppealSteps.SecondBill)).Held.ShouldBeTrue();

            // "Patvirtinti": the box goes for every staff account; it still waits for its time.
            (await moderator.ApproveDigestAsync(prepared.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await admin.Browser.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root)).DigestReview.ShouldBeNull("approved: nothing left to review");
            (await Factory.RunDueAsync(MpSendingSteps.At(monday, 6, 55))).ShouldBeEmpty();

            var gone = (await Factory.RunDueAsync(MpSendingSteps.At(monday, 7, 5))).Single();
            gone.Status.ShouldBe("dry-run");
            gone.Items.Select(i => i.BillNumber).ShouldNotContain(AppealSteps.SecondBill, "the held bill dropped out");
            var item = gone.Items.Single(i => i.BillNumber == AppealSteps.ThirdBill);
            (item.For, item.Against).ShouldBe((3, 1), "read when it went: the newcomer's side counts");
            item.Reasons.Select(r => r.Text).ShouldBe([Second, Third, AppealSteps.AgainstReason], "the chosen list, in its order, without the later argument");
            item.Reasons.Single(r => r.Text == Second).Endorsements.ShouldBe(1);
            using var stranger = NewBrowser();
            var copy = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync(DigestRoutes.PageOf(monday)));
            copy.ShouldContain(Third);
            copy.ShouldNotContain(First);
            copy.ShouldNotContain(Late);
            (await moderator.Browser.GetJsonAsync<MeResponse>(ApiRoutes.Me.Root)).DigestReview.ShouldBeNull("gone");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'digest.prepared' AND target = @t AND details::text NOT LIKE '%argument%'", ("t", "digest:" + prepared.Id)))
                .ShouldBe(1);
        }
        finally
        {
            await Database.ForgetPreparedAsync();
            await admin.Browser.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
            await Database.FreeBillAsync(AppealSteps.SecondBill);
        }
    }
}
