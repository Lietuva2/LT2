using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
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
            var appeal = await Database.OpenPublishedAsync(initiator, moderator, AppealSteps.ThirdBill, "against", threshold: 2);
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
        }
        finally
        {
            await SetAsync(admin, "Digest:ThresholdMinimum", 50);
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number IN ('XVP-0618', 'XVP-0533')");
        }
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
            await Database.OpenPublishedAsync(first, moderator, AppealSteps.ThirdBill, "against", threshold: 1);
            await PlanAsync("XVP-0618", thursday);
            (await RunDueAsync(At(monday, 7, 5))).Single().Kind.ShouldBe("weekly");

            // Below threshold: not eligible; once eligible and planned for Thursday it enters Wednesday's summary.
            var (second, _) = await Factory.MemberAsync();
            using var _s = second;
            var other = await Database.OpenPublishedAsync(second, moderator, AppealSteps.SecondBill, "for", threshold: 1);
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
}
