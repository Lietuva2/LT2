using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.4, 8.7, 8.9: summaries in dry-run. They are made by the rules, frozen, published without MP-specific figures,
/// and never sent: no MP address reaches the outbox, and no summary holds a person.
/// </summary>
public sealed class DigestTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly TimeZoneInfo Vilnius = Lt2.SharedKernel.VilniusTime.Zone;

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0)
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Vilnius.GetUtcOffset(local));
    }

    private Task PlanAsync(string bill, DateOnly date, string stage = "adoption") =>
        Database.ExecuteAsync(
            "INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, first_seen, last_seen) VALUES (gen_random_uuid(), @b, @d, @s, 'agenda', now(), now())",
            ("b", bill), ("d", date), ("s", stage));

    private async Task<IReadOnlyList<DigestRunInfo>> RunDueAsync(DateTimeOffset now)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDigests>().RunDueAsync(now, CancellationToken.None);
    }

    [Fact]
    public async Task The_weekly_summary_freezes_both_sides_and_seen_arguments_publishes_them_and_sends_nothing()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        await admin.SetSettingAsync("Digest:ThresholdMinimum", 1);
        var monday = ReasonsToComeSteps.MondayInWeeks(2);
        try
        {
            var (initiator, _) = await Factory.MemberAsync("Rūta Mockutė");
            using var _i = initiator;
            var record = await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against", threshold: 2);
            var (member, _) = await Factory.MemberAsync("Giedrė Slapta");
            using var _m = member;
            (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Neperžiūrėtas argumentas dar nepatenka į suvestinę.")).StatusCode.ShouldBe(HttpStatusCode.Created);
            await PlanAsync("XVP-0618", monday.AddDays(3));

            var outboxBefore = await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox");
            var since = await Database.ScalarAsync<DateTime>("SELECT clock_timestamp()");
            (await RunDueAsync(At(monday, 6, 30))).ShouldBeEmpty("before 07:00");
            var made = await RunDueAsync(At(monday, 7, 15));
            var weekly = made.Single();
            weekly.Kind.ShouldBe("weekly");
            weekly.Status.ShouldBe("dry-run");
            weekly.MpCount.ShouldBeGreaterThan(0);
            var item = weekly.Items.Single(i => i.BillNumber == "XVP-0618");
            (item.For, item.ForOlderThan30Days, item.Against, item.AgainstOlderThan30Days).ShouldBe((1, 0, 1, 0));
            item.PlannedDate.ShouldBe(monday.AddDays(3));
            item.Reasons.Select(r => r.Text).ShouldBe([AppealSteps.AgainstReason], "only arguments a moderator has seen");
            (await RunDueAsync(At(monday, 9))).ShouldBeEmpty("one weekly summary a week");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox")).ShouldBe(outboxBefore, "making summaries writes nothing to the outbox");

            // The public copy (page and API) holds the frozen numbers, says it was not sent, and holds no person.
            var date = monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            using var stranger = NewBrowser();
            var api = await stranger.GetJsonAsync<List<DigestRunResponse>>(DigestRoutes.DigestOn(monday));
            api.Single().Items.Single(i => i.BillNumber == "XVP-0618").For.ShouldBe(1);
            using var page = await stranger.Client.GetAsync("/suvestines/" + date);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
            html.ShouldContain("Bandymas: suvestinės sudaromos, bet Seimo nariams nesiunčiamos.");
            html.ShouldContain(AppealSteps.AgainstReason);
            html.ShouldContain("Tai nėra reprezentatyvi apklausa");
            foreach (var person in new[] { "Rūta Mockutė", "Giedrė Slapta" })
            {
                html.ShouldNotContain(person);
                (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_item WHERE reasons::text LIKE @p OR bill_title LIKE @p", ("p", "%" + person + "%"))).ShouldBe(0);
            }

            // The record lists the summary that held it, with its frozen numbers, and links to its item on the public page.
            var held = (await stranger.GetJsonAsync<List<RecordDigestResponse>>(DigestRoutes.OfRecordOn(AppealSteps.ThirdBill))).ShouldHaveSingleItem();
            (held.RunId, held.Kind, held.RunDate, held.Status).ShouldBe((weekly.Id, "weekly", monday, "dry-run"));
            (held.Item.BillNumber, held.Item.For, held.Item.Against).ShouldBe(("XVP-0618", 1, 1));
            held.Item.Reasons.Select(r => r.Text).ShouldBe([AppealSteps.AgainstReason]);
            using var recordPage = await stranger.Client.GetAsync("/seimas/XVP-0618");
            var recordHtml = WebUtility.HtmlDecode(await recordPage.Content.ReadAsStringAsync());
            recordHtml.ShouldContain("Suvestinėse Seimo nariams");
            recordHtml.ShouldNotContain("Šis projektas dar nebuvo nė vienoje suvestinėje.");
            recordHtml.ShouldContain($"href=\"{DigestRoutes.PageAt(monday, weekly.Id, "XVP-0618")}\"");
            html.ShouldContain($"id=\"{DigestRoutes.ItemAnchor(weekly.Id, "XVP-0618")}\"", customMessage: "the link lands on the record's item");
            (await stranger.Client.GetAsync(DigestRoutes.OfRecordOn("XVP-9999"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'notify' AND table_name LIKE 'digest%' AND (column_name LIKE '%user%' OR column_name LIKE '%email%' OR column_name LIKE '%address%')"))
                .ShouldBe(0, "a summary table has no column for a person or an address");

            // Nothing was sent: no summary is in the outbox, and the run wrote nothing to an MP address. (Since design 8.5 an
            // MP's own linked account gets its notices at the official address, so other tests leave such rows behind.)
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM notify.outbox WHERE kind LIKE '%digest%' OR (payload::text ILIKE '%@lrs.lt%' AND created_at >= @since)", ("since", since))).ShouldBe(0);

            // A moderator reads what an MP would read; the public cannot.
            var mps = await moderator.GetJsonAsync<List<DigestMpResponse>>(DigestRoutes.MpsOf(weekly.Id));
            var preview = await moderator.GetJsonAsync<DigestPreviewResponse>(DigestRoutes.PreviewOf(weekly.Id, mps[0].Slug));
            preview.Text.ShouldContain("XVP-0618");
            preview.Text.ShouldContain("Prašo balsuoti už: 1 (iš jų 0 – paskyros senesnės nei 30 d.)");
            preview.Text.ShouldNotContain("Rūta Mockutė");
            (await stranger.Client.GetAsync(DigestRoutes.PreviewOf(weekly.Id, mps[0].Slug))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            // A later change to the record does not change what the summary said.
            (await initiator.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await stranger.GetJsonAsync<List<DigestRunResponse>>(DigestRoutes.DigestOn(monday))).Single().Items.Single(i => i.BillNumber == "XVP-0618").Against.ShouldBe(1);
            (await stranger.GetJsonAsync<List<RecordDigestResponse>>(DigestRoutes.OfRecordOn(AppealSteps.ThirdBill))).Single().Item.Against.ShouldBe(1);
        }
        finally
        {
            await admin.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
    }

    [Fact]
    public async Task Changing_the_notable_change_setting_changes_which_grown_records_a_weekly_summary_holds()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        await admin.SetSettingAsync("Digest:ThresholdMinimum", 1);
        var first = ReasonsToComeSteps.MondayInWeeks(8);
        try
        {
            // Another bill on each week's agenda makes them sitting weeks; XVP-0618 itself is never planned, so it can
            // enter a weekly summary only by being new or by changing notably.
            foreach (var monday in new[] { first, first.AddDays(7), first.AddDays(14) })
            {
                await PlanAsync("XVP-0533", monday.AddDays(3), "consideration");
            }

            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against", threshold: 1);
            var (second, _) = await Factory.MemberAsync();
            using var _s = second;
            (await second.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            static IEnumerable<string> Bills(IReadOnlyList<DigestRunInfo> runs) => runs.SelectMany(r => r.Items).Select(i => i.BillNumber);
            Bills(await RunDueAsync(At(first, 7, 5))).ShouldContain("XVP-0618", "never in a summary yet: 2 participants");

            var (third, _) = await Factory.MemberAsync();
            using var _t = third;
            (await third.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // 2 → 3 participants is a change of 50 %.
            await admin.SetSettingAsync("Digest:NotableChange", 0.6m);
            Bills(await RunDueAsync(At(first.AddDays(7), 7, 5))).ShouldNotContain("XVP-0618", "50 % is under the set 60 %");
            await admin.SetSettingAsync("Digest:NotableChange", 0.5m);
            Bills(await RunDueAsync(At(first.AddDays(14), 7, 5))).ShouldContain("XVP-0618", "50 % reaches the set 50 %");
        }
        finally
        {
            await admin.ResetSettingAsync("Digest:NotableChange");
            await admin.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
    }

    /// <summary>Design 8.1: records are opened without arguments, so a summary meets a side, or a whole record, with none.</summary>
    [Fact]
    public async Task A_summary_copes_with_a_record_without_arguments_and_with_a_side_without_any()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        await admin.SetSettingAsync("Digest:ThresholdMinimum", 1);
        var monday = ReasonsToComeSteps.MondayInWeeks(16);
        try
        {
            // XVP-0618: one participant, no arguments at all. XVP-0533: an argument for, none against.
            var (first, _) = await Factory.MemberAsync();
            using var _f = first;
            await Database.PrepareBillAsync(first, moderator, AppealSteps.ThirdBill, side: null, threshold: 1);
            var (second, _) = await Factory.MemberAsync();
            using var _s = second;
            await Database.PrepareBillAsync(second, moderator, AppealSteps.SecondBill, side: null, threshold: 1);
            var (participant, _) = await Factory.MemberAsync();
            using var _p = participant;
            (await participant.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await participant.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using (var written = await participant.WriteReasonAsync(AppealSteps.SecondBill, "for", "Vienintelis argumentas už."))
            {
                var id = (await written.ReadAsync<ReasonCreatedResponse>()).Id;
                (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.PublishReason, id), new ModerationRequest())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            // Design 8.19.3 (owner, 2026-11-16): a long argument is shortened in the email and its preview, with a link to it
            // on the public copy, which holds the whole snapshot text.
            var longReason = string.Concat(Enumerable.Repeat("Ilgas argumentas apie vietines rinkliavas. ", 25)).Trim();
            using (var written = await participant.WriteReasonAsync(AppealSteps.SecondBill, "for", longReason))
            {
                var id = (await written.ReadAsync<ReasonCreatedResponse>()).Id;
                (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.PublishReason, id), new ModerationRequest())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            await PlanAsync("XVP-0618", monday.AddDays(3));
            await PlanAsync("XVP-0533", monday.AddDays(3), "consideration");
            var weekly = (await RunDueAsync(At(monday, 7, 5))).Single();
            weekly.Items.Single(i => i.BillNumber == "XVP-0618").Reasons.ShouldBeEmpty();
            weekly.Items.Single(i => i.BillNumber == "XVP-0533").Reasons.Select(r => r.Side).ShouldBe(["for", "for"]);
            weekly.Items.Single(i => i.BillNumber == "XVP-0533").Reasons.ShouldContain(r => r.Text == longReason, "the snapshot keeps the whole text");

            var mps = await moderator.GetJsonAsync<List<DigestMpResponse>>(DigestRoutes.MpsOf(weekly.Id));
            var text = (await moderator.GetJsonAsync<DigestPreviewResponse>(DigestRoutes.PreviewOf(weekly.Id, mps[0].Slug))).Text;
            Regex(text, "Argumentų už dar nėra.").ShouldBe(1, "XVP-0618 has none for");
            Regex(text, "Argumentų prieš dar nėra.").ShouldBe(2, "neither record has one against");
            text.ShouldContain("Vienintelis argumentas už.");
            text.ShouldNotContain(longReason, Case.Sensitive, "the email and its preview carry the beginning only");
            var place = weekly.Items.Single(i => i.BillNumber == "XVP-0533").Reasons.Where(r => r.Side == "for").Select((r, i) => (r, i)).Single(x => x.r.Text == longReason).i + 1;
            var anchor = DigestRoutes.ReasonAnchor(weekly.Id, "XVP-0533", "for", place);
            text.ShouldContain("Visas argumentas: ");
            text.ShouldContain("/suvestines/" + monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "#" + anchor);

            using var stranger = NewBrowser();
            var html = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            html.ShouldContain("id=\"" + anchor + "\"", Case.Sensitive, "the link lands on the argument on the public copy");
            html.ShouldContain("Rodyti daugiau");
            Regex(html, "Argumentų dar nėra.").ShouldBe(3, "XVP-0618 on both sides, XVP-0533 against");
        }
        finally
        {
            await admin.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }

        static int Regex(string text, string what) => System.Text.RegularExpressions.Regex.Count(text, System.Text.RegularExpressions.Regex.Escape(what));
    }

    [Fact]
    public async Task A_before_vote_summary_comes_the_day_before_and_the_weekly_ceiling_holds_per_mp()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        await admin.SetSettingAsync("Digest:ThresholdMinimum", 1);
        await admin.SetSettingAsync("Digest:MaxPerWeek", 1);
        var monday = ReasonsToComeSteps.MondayInWeeks(4);
        var thursday = monday.AddDays(3);
        try
        {
            var (first, _) = await Factory.MemberAsync();
            using var _f = first;
            await Database.PrepareBillAsync(first, moderator, AppealSteps.ThirdBill, "against", threshold: 1);
            await PlanAsync("XVP-0618", thursday);
            (await RunDueAsync(At(monday, 7, 5))).Single().Kind.ShouldBe("weekly");

            // Below threshold: not eligible; once eligible and planned for Thursday it enters Wednesday's summary.
            var (second, _) = await Factory.MemberAsync();
            using var _s = second;
            var other = await Database.PrepareBillAsync(second, moderator, AppealSteps.SecondBill, "for", threshold: 1);
            await PlanAsync("XVP-0533", thursday, "consideration");
            (await RunDueAsync(At(thursday.AddDays(-1), 11, 30))).ShouldBeEmpty("before noon");
            var beforeVote = (await RunDueAsync(At(thursday.AddDays(-1), 12, 5))).Single();
            beforeVote.Kind.ShouldBe("before-vote");
            beforeVote.SittingDate.ShouldBe(thursday);
            beforeVote.Items.Select(i => i.BillNumber).ShouldBe(["XVP-0533"], "XVP-0618 was in Monday's summary for the same sitting");

            // The ceiling of one a week: every MP already had Monday's, so this one would reach nobody.
            beforeVote.MpCount.ShouldBe(0);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_delivery WHERE run_id = @r AND status = 'skipped-ceiling'", ("r", beforeVote.Id)))
                .ShouldBeGreaterThan(0);
            (await RunDueAsync(At(thursday.AddDays(-1), 15))).ShouldBeEmpty("once per sitting");
            other.ShouldNotBeNull();
        }
        finally
        {
            await admin.ResetSettingAsync("Digest:MaxPerWeek");
            await admin.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
    }

    [Fact]
    public async Task A_held_record_enters_no_summary_until_released_and_one_urgent_summary_may_exceed_the_ceiling()
    {
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        await admin.SetSettingAsync("Digest:ThresholdMinimum", 1);
        await admin.SetSettingAsync("Digest:MaxPerWeek", 1);
        var monday = ReasonsToComeSteps.MondayInWeeks(6);
        try
        {
            var (initiator, initiatorEmail) = await Factory.MemberAsync();
            using var _i = initiator;
            var record = await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against", threshold: 1);
            await PlanAsync("XVP-0618", monday.AddDays(3));

            using (var hold = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HoldRecord, record.Id), new StatementRequest { Statement = "Tikrinamas šuolis." }))
            {
                hold.StatusCode.ShouldBe(HttpStatusCode.NoContent, await hold.Content.ReadAsStringAsync());
            }

            (await moderator.GetJsonAsync<Lt2.Api.Contracts.Appeals.ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Records.Single(a => a.Id == record.Id).Held.ShouldBeTrue();
            (await RunDueAsync(At(monday, 7, 10))).ShouldBeEmpty("a held record enters no summary");
            using (var urgentHeld = await moderator.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, record.Id), new UrgentDigestRequest { Statement = "Skubiai." }))
            {
                await urgentHeld.ShouldBeProblemAsync(HttpStatusCode.Conflict, "digest.held");
            }

            (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.ReleaseRecord, record.Id), new StatementRequest { Statement = "Patikrinta." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action IN ('moderation.record.hold', 'moderation.record.release') AND target = @t", ("t", "record:" + record.Id))).ShouldBe(2);

            // An urgent summary is made in this real week, which other classes' urgent summaries share (AccountDataTests,
            // PartySupportTests), so the ceiling is set from what the week already holds: one above the most any MP has had.
            // A first urgent summary on another bill then brings every MP who had the most to the ceiling, and this one may
            // still go to them, exceeding it by one.
            var (other, _) = await Factory.MemberAsync();
            using var _o = other;
            var first = await Database.PrepareBillAsync(other, moderator, AppealSteps.SecondBill, "for", threshold: 1);
            var weekStart = ReasonsToComeSteps.MondayInWeeks(0);
            var most = await Database.ScalarAsync<long>(
                "SELECT coalesce(max(n), 0) FROM (SELECT count(*) AS n FROM notify.digest_delivery d JOIN notify.digest_run r ON r.id = d.run_id WHERE d.status = 'dry-run' AND r.run_date >= @from AND r.run_date < @to GROUP BY d.mp_slug) AS per_mp",
                ("from", weekStart), ("to", weekStart.AddDays(7)));
            most.ShouldBeLessThan(7, "the ceiling can be set at most to 7");
            await admin.SetSettingAsync("Digest:MaxPerWeek", (int)most + 1);
            await Database.ExecuteAsync("UPDATE appeals.bill_record SET sending = 'automatic' WHERE id = ANY(@a)", ("a", new[] { record.Id, first.Id }));
            using var reachesTheCeiling = await moderator.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, first.Id), new UrgentDigestRequest { Statement = "Bandymas: pasiekiamos lubos." });
            reachesTheCeiling.StatusCode.ShouldBe(HttpStatusCode.Created, await reachesTheCeiling.Content.ReadAsStringAsync());
            var everyone = (await reachesTheCeiling.ReadAsync<DigestRunResponse>()).MpCount;
            everyone.ShouldBeGreaterThan(0);

            using var urgent = await moderator.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, record.Id), new UrgentDigestRequest { Statement = "Įtraukta į rytojaus darbotvarkę." });
            urgent.StatusCode.ShouldBe(HttpStatusCode.Created, await urgent.Content.ReadAsStringAsync());
            var run = await urgent.ReadAsync<DigestRunResponse>();
            run.Kind.ShouldBe("urgent");
            run.MpCount.ShouldBe(everyone, "an urgent summary may exceed the ceiling by one");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_delivery WHERE run_id = @r AND status = 'skipped-ceiling'", ("r", run.Id))).ShouldBe(0);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'digest.urgent.approved' AND actor_role = 'moderator'")).ShouldBeGreaterThanOrEqualTo(1);
            using var again = await moderator.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, record.Id), new UrgentDigestRequest { Statement = "Dar kartą." });
            await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "digest.urgent-already");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' = @to AND kind = 'email.moderation-notice' AND payload->>'notice' NOT IN ('appeal-published')", ("to", initiatorEmail))).ShouldBe(0, "timing is not the author's content");

            using var member = NewBrowser();
            (await member.Client.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, record.Id), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await admin.ResetSettingAsync("Digest:MaxPerWeek");
            await admin.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
            await Database.FreeBillAsync(AppealSteps.SecondBill);
        }
    }

    /// <summary>
    /// Design 6.9 (owner, 2026-11-15): hiding an argument on a closed record changes neither the frozen counts nor a summary
    /// already made that holds it.
    /// </summary>
    [Fact]
    public async Task Hiding_an_argument_after_the_final_vote_changes_neither_the_frozen_counts_nor_a_summary_already_made()
    {
        const long finalVote = 99199619;
        var (moderator, admin) = ((await ModeratorAsync()).Browser, (await AdministratorAsync()).Browser);
        await admin.SetSettingAsync("Digest:ThresholdMinimum", 1);
        var monday = ReasonsToComeSteps.MondayInWeeks(18);
        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against", threshold: 2);
            var reasonId = (await initiator.RecordAsync(AppealSteps.ThirdBill)).Reasons.Single().Id;
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            (await member.TakeSideAsync(AppealSteps.ThirdBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.SendAsync(HttpMethod.Put, AppealRoutes.EndorsementOf(reasonId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await PlanAsync("XVP-0618", monday.AddDays(3));
            var weekly = (await RunDueAsync(At(monday, 7, 15))).Single();
            weekly.Items.Single(i => i.BillNumber == "XVP-0618").Reasons.Select(r => r.Text).ShouldBe([AppealSteps.AgainstReason]);

            await Database.ExecuteAsync(
                """
                INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
                SELECT @id, agenda_item_id, bill_id, held_at + interval '7 days', 'Priėmimas', type, 'StageResult', 'Adoption', false, true, "for", against, abstain, voted, seats
                FROM parliament.vote WHERE id = 99100102
                """,
                ("id", finalVote));
            try
            {
                await using (var scope = Factory.Services.CreateAsyncScope())
                {
                    (await scope.ServiceProvider.GetRequiredService<Lt2.Appeals.IAppealsMaintenance>().CloseFinishedAsync(CancellationToken.None)).ShouldBe(1);
                }

                using var stranger = NewBrowser();
                var closed = await stranger.RecordAsync(AppealSteps.ThirdBill);
                closed.Status.ShouldBe("closed");
                var summary = await stranger.GetJsonAsync<List<DigestRunResponse>>(DigestRoutes.DigestOn(monday));
                var held = await stranger.GetJsonAsync<List<RecordDigestResponse>>(DigestRoutes.OfRecordOn(AppealSteps.ThirdBill));
                var items = await Database.ScalarAsync<string>("SELECT string_agg(reasons::text || ':' || for_count || '/' || against_count, '|' ORDER BY id) FROM notify.digest_item WHERE bill_number = 'XVP-0618'");

                (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HideReason, reasonId), new StatementRequest { Statement = "Pažeidžia taisykles." }))
                    .StatusCode.ShouldBe(HttpStatusCode.NoContent);

                var after = await stranger.RecordAsync(AppealSteps.ThirdBill);
                after.Reasons.ShouldBeEmpty("the argument is hidden");
                after.Counts.ShouldBe(closed.Counts, "the counts were frozen at the final vote");
                JsonSerializer.Serialize(await stranger.GetJsonAsync<List<DigestRunResponse>>(DigestRoutes.DigestOn(monday)))
                    .ShouldBe(JsonSerializer.Serialize(summary), "the summary already made keeps its frozen text");
                JsonSerializer.Serialize(await stranger.GetJsonAsync<List<RecordDigestResponse>>(DigestRoutes.OfRecordOn(AppealSteps.ThirdBill)))
                    .ShouldBe(JsonSerializer.Serialize(held));
                (await Database.ScalarAsync<string>("SELECT string_agg(reasons::text || ':' || for_count || '/' || against_count, '|' ORDER BY id) FROM notify.digest_item WHERE bill_number = 'XVP-0618'"))
                    .ShouldBe(items);
            }
            finally
            {
                await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @id", ("id", finalVote));
                await Database.FreeBillAsync(AppealSteps.ThirdBill);
            }
        }
        finally
        {
            await admin.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
    }
}