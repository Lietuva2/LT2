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
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static StaffMember? _moderator;
    private static StaffMember? _administrator;
    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    private async Task<(TestBrowser Moderator, TestBrowser Administrator)> StaffAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _moderator ??= await StaffMembers.ActiveAsync(Factory, Database, "moderator");
            _administrator ??= await StaffMembers.ActiveAsync(Factory, Database, "administrator");
            return (_moderator.Browser, _administrator.Browser);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task SetAsync(TestBrowser admin, string key, object value)
    {
        using var response = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(key), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(value) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The Monday of a week some weeks ahead, so each test has its own weeks.</summary>
    private static DateOnly MondayIn(int weeks)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Vilnius).DateTime);
        return today.AddDays(7 * weeks - (((int)today.DayOfWeek + 6) % 7));
    }

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
        var (moderator, admin) = await StaffAsync();
        await SetAsync(admin, "Digest:ThresholdMinimum", 1);
        var monday = MondayIn(2);
        try
        {
            var (initiator, _) = await Factory.MemberAsync("Rūta Mockutė");
            using var _i = initiator;
            var appeal = await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against", threshold: 2);
            var (member, _) = await Factory.MemberAsync("Giedrė Slapta");
            using var _m = member;
            (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.WriteReasonAsync(AppealSteps.ThirdBill, "for", "Neperžiūrėtas argumentas dar nepatenka į suvestinę.")).StatusCode.ShouldBe(HttpStatusCode.Created);
            await PlanAsync("XVP-0618", monday.AddDays(3));

            var outboxBefore = await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox");
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

            // The appeal lists the summary that held it, with its frozen numbers, and links to its item on the public page.
            var held = (await stranger.GetJsonAsync<List<AppealDigestResponse>>(DigestRoutes.OfAppealOn(AppealSteps.ThirdBill))).ShouldHaveSingleItem();
            (held.RunId, held.Kind, held.RunDate, held.Status).ShouldBe((weekly.Id, "weekly", monday, "dry-run"));
            (held.Item.BillNumber, held.Item.For, held.Item.Against).ShouldBe(("XVP-0618", 1, 1));
            held.Item.Reasons.Select(r => r.Text).ShouldBe([AppealSteps.AgainstReason]);
            using var appealPage = await stranger.Client.GetAsync("/seimas/XVP-0618");
            var appealHtml = WebUtility.HtmlDecode(await appealPage.Content.ReadAsStringAsync());
            appealHtml.ShouldContain("Suvestinėse Seimo nariams");
            appealHtml.ShouldNotContain("Šis projektas dar nebuvo nė vienoje suvestinėje.");
            appealHtml.ShouldContain($"href=\"{DigestRoutes.PageAt(monday, weekly.Id, "XVP-0618")}\"");
            html.ShouldContain($"id=\"{DigestRoutes.ItemAnchor(weekly.Id, "XVP-0618")}\"", customMessage: "the link lands on the appeal's item");
            (await stranger.Client.GetAsync(DigestRoutes.OfAppealOn("XVP-9999"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'notify' AND table_name LIKE 'digest%' AND (column_name LIKE '%user%' OR column_name LIKE '%email%' OR column_name LIKE '%address%')"))
                .ShouldBe(0, "a summary table has no column for a person or an address");

            // Nothing was sent: no outbox row was written, and no MP address is anywhere in the outbox.
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload::text ILIKE '%@lrs.lt%' OR kind LIKE '%digest%'")).ShouldBe(0);

            // A moderator reads what an MP would read; the public cannot.
            var mps = await moderator.GetJsonAsync<List<DigestMpResponse>>(DigestRoutes.MpsOf(weekly.Id));
            var preview = await moderator.GetJsonAsync<DigestPreviewResponse>(DigestRoutes.PreviewOf(weekly.Id, mps[0].Slug));
            preview.Text.ShouldContain("XVP-0618");
            preview.Text.ShouldContain("Prašo balsuoti už: 1 (iš jų 0 – paskyros senesnės nei 30 d.)");
            preview.Text.ShouldNotContain("Rūta Mockutė");
            (await stranger.Client.GetAsync(DigestRoutes.PreviewOf(weekly.Id, mps[0].Slug))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            // A later change to the appeal does not change what the summary said.
            (await initiator.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await stranger.GetJsonAsync<List<DigestRunResponse>>(DigestRoutes.DigestOn(monday))).Single().Items.Single(i => i.BillNumber == "XVP-0618").Against.ShouldBe(1);
            (await stranger.GetJsonAsync<List<AppealDigestResponse>>(DigestRoutes.OfAppealOn(AppealSteps.ThirdBill))).Single().Item.Against.ShouldBe(1);
        }
        finally
        {
            await SetAsync(admin, "Digest:ThresholdMinimum", 50);
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
    }

    [Fact]
    public async Task Changing_the_notable_change_setting_changes_which_grown_appeals_a_weekly_summary_holds()
    {
        var (moderator, admin) = await StaffAsync();
        await SetAsync(admin, "Digest:ThresholdMinimum", 1);
        var first = MondayIn(8);
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
            await SetAsync(admin, "Digest:NotableChange", 0.6m);
            Bills(await RunDueAsync(At(first.AddDays(7), 7, 5))).ShouldNotContain("XVP-0618", "50 % is under the set 60 %");
            await SetAsync(admin, "Digest:NotableChange", 0.5m);
            Bills(await RunDueAsync(At(first.AddDays(14), 7, 5))).ShouldContain("XVP-0618", "50 % reaches the set 50 %");
        }
        finally
        {
            await SetAsync(admin, "Digest:NotableChange", 0.10m);
            await SetAsync(admin, "Digest:ThresholdMinimum", 50);
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
    }

    /// <summary>Design 8.1: appeals are opened without arguments, so a summary meets a side, or a whole appeal, with none.</summary>
    [Fact]
    public async Task A_summary_copes_with_an_appeal_without_arguments_and_with_a_side_without_any()
    {
        var (moderator, admin) = await StaffAsync();
        await SetAsync(admin, "Digest:ThresholdMinimum", 1);
        var monday = MondayIn(16);
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

            await PlanAsync("XVP-0618", monday.AddDays(3));
            await PlanAsync("XVP-0533", monday.AddDays(3), "consideration");
            var weekly = (await RunDueAsync(At(monday, 7, 5))).Single();
            weekly.Items.Single(i => i.BillNumber == "XVP-0618").Reasons.ShouldBeEmpty();
            weekly.Items.Single(i => i.BillNumber == "XVP-0533").Reasons.Select(r => r.Side).ShouldBe(["for"]);

            var mps = await moderator.GetJsonAsync<List<DigestMpResponse>>(DigestRoutes.MpsOf(weekly.Id));
            var text = (await moderator.GetJsonAsync<DigestPreviewResponse>(DigestRoutes.PreviewOf(weekly.Id, mps[0].Slug))).Text;
            Regex(text, "Argumentų už dar nėra.").ShouldBe(1, "XVP-0618 has none for");
            Regex(text, "Argumentų prieš dar nėra.").ShouldBe(2, "neither appeal has one against");
            text.ShouldContain("Vienintelis argumentas už.");

            using var stranger = NewBrowser();
            var html = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            Regex(html, "Argumentų dar nėra.").ShouldBe(3, "XVP-0618 on both sides, XVP-0533 against");
        }
        finally
        {
            await SetAsync(admin, "Digest:ThresholdMinimum", 50);
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }

        static int Regex(string text, string what) => System.Text.RegularExpressions.Regex.Count(text, System.Text.RegularExpressions.Regex.Escape(what));
    }

    [Fact]
    public async Task A_before_vote_summary_comes_the_day_before_and_the_weekly_ceiling_holds_per_mp()
    {
        var (moderator, admin) = await StaffAsync();
        await SetAsync(admin, "Digest:ThresholdMinimum", 1);
        await SetAsync(admin, "Digest:MaxPerWeek", 1);
        var monday = MondayIn(4);
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
            await SetAsync(admin, "Digest:MaxPerWeek", 2);
            await SetAsync(admin, "Digest:ThresholdMinimum", 50);
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
    }

    [Fact]
    public async Task A_held_appeal_enters_no_summary_until_released_and_one_urgent_summary_may_exceed_the_ceiling()
    {
        var (moderator, admin) = await StaffAsync();
        await SetAsync(admin, "Digest:ThresholdMinimum", 1);
        await SetAsync(admin, "Digest:MaxPerWeek", 1);
        var monday = MondayIn(6);
        try
        {
            var (initiator, initiatorEmail) = await Factory.MemberAsync();
            using var _i = initiator;
            var appeal = await Database.PrepareBillAsync(initiator, moderator, AppealSteps.ThirdBill, "against", threshold: 1);
            await PlanAsync("XVP-0618", monday.AddDays(3));

            using (var hold = await moderator.PostAsync(AppealRoutes.With(AppealRoutes.HoldAppeal, appeal.Id), new StatementRequest { Statement = "Tikrinamas šuolis." }))
            {
                hold.StatusCode.ShouldBe(HttpStatusCode.NoContent, await hold.Content.ReadAsStringAsync());
            }

            (await moderator.GetJsonAsync<Lt2.Api.Contracts.Appeals.ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.Single(a => a.Id == appeal.Id).Held.ShouldBeTrue();
            (await RunDueAsync(At(monday, 7, 10))).ShouldBeEmpty("a held appeal enters no summary");
            using (var urgentHeld = await moderator.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, appeal.Id), new UrgentDigestRequest { Statement = "Skubiai." }))
            {
                await urgentHeld.ShouldBeProblemAsync(HttpStatusCode.Conflict, "digest.held");
            }

            (await moderator.PostAsync(AppealRoutes.With(AppealRoutes.ReleaseAppeal, appeal.Id), new StatementRequest { Statement = "Patikrinta." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action IN ('moderation.appeal.hold', 'moderation.appeal.release') AND target = @t", ("t", "appeal:" + appeal.Id))).ShouldBe(2);

            // This week already had its summary for everyone (ceiling 1); an urgent one may still go, once per bill per week.
            await Database.ExecuteAsync("UPDATE appeals.appeal SET sending = 'automatic' WHERE id = @a", ("a", appeal.Id));
            using var urgent = await moderator.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, appeal.Id), new UrgentDigestRequest { Statement = "Įtraukta į rytojaus darbotvarkę." });
            urgent.StatusCode.ShouldBe(HttpStatusCode.Created, await urgent.Content.ReadAsStringAsync());
            var run = await urgent.ReadAsync<DigestRunResponse>();
            run.Kind.ShouldBe("urgent");
            run.MpCount.ShouldBeGreaterThan(0);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'digest.urgent.approved' AND actor_role = 'moderator'")).ShouldBeGreaterThanOrEqualTo(1);
            using var again = await moderator.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, appeal.Id), new UrgentDigestRequest { Statement = "Dar kartą." });
            await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "digest.urgent-already");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload->>'to' = @to AND kind = 'email.moderation-notice' AND payload->>'notice' NOT IN ('appeal-published')", ("to", initiatorEmail))).ShouldBe(0, "timing is not the author's content");

            using var member = NewBrowser();
            (await member.Client.PostAsync(AppealRoutes.With(DigestRoutes.Urgent, appeal.Id), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await SetAsync(admin, "Digest:MaxPerWeek", 2);
            await SetAsync(admin, "Digest:ThresholdMinimum", 50);
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
    }
}