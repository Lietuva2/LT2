using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Organisations;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.14.3, 8.14.14: a statement is not a position. The bill's counts, threshold progress, activity, the lists and
/// their order, the agenda, the front page, the daily statistics and everything the summaries to MPs read are the same with
/// and without statements; and a summary made while a bill has statements carries none of them (snapshot, public copy,
/// what an MP would read).
/// </summary>
public sealed class OrganisationCountTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    [Fact]
    public async Task Counts_lists_agenda_front_page_daily_statistics_and_summary_candidates_are_the_same_with_and_without_statements()
    {
        await Database.FreeBillAsync(AppealSteps.SecondBill);
        await Database.ClearStatementsAsync(AppealSteps.SecondBill);
        var (participant, _) = await Factory.MemberAsync();
        using var _p = participant;
        (await participant.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var visitor = NewBrowser();
        var routes = new[]
        {
            AppealRoutes.AppealOf(AppealSteps.SecondBill), AppealRoutes.Appeals, AppealRoutes.AppealsList(null, false, false, null, "participants"),
            AppealRoutes.AppealsList(null, false, false, null, "arguments"), AgendaRoutes.Agenda, "/",
        };
        var before = new Dictionary<string, string>();
        foreach (var route in routes)
        {
            before[route] = Stable(await visitor.Client.GetStringAsync(route));
        }

        var candidatesBefore = await CandidatesAsync();
        var dailyBefore = await DailyAsync();

        // Two organisations on opposite sides, each with a goal, and one with remarks.
        var (rep, _) = await Factory.MemberAsync();
        using var _r = rep;
        foreach (var (stance, goal) in new (string, int?)[] { ("for", 1000), ("against", 500), ("remarks", null) })
        {
            var created = await rep.RegisterOrganisationAsync();
            await rep.PublishStatementAsync(AppealSteps.SecondBill, created.Slug, stance, "Organizacijos pozicija, kuri neskaičiuojama.", goal: goal);
        }

        foreach (var route in routes)
        {
            Stable(await visitor.Client.GetStringAsync(route)).ShouldBe(before[route], route);
        }

        (await CandidatesAsync()).ShouldBe(candidatesBefore, "what the summaries read");
        (await DailyAsync()).ShouldBe(dailyBefore, "the daily statistics");
        await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET status = 'withdrawn' WHERE bill_number = @b", ("b", AppealSteps.SecondBill));
    }

    [Fact]
    public async Task A_summary_made_while_a_bill_has_statements_carries_none_of_them()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        await SetAsync(admin.Browser, "Digest:ThresholdMinimum", 1);
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        await Database.ClearStatementsAsync(AppealSteps.ThirdBill);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Vilnius).DateTime);
        var monday = today.AddDays(7 * 13 - (((int)today.DayOfWeek + 6) % 7));
        const string Marker = "organizacijosžymabandymui";
        var name = OrganisationSteps.NewName("Suvestinių bandymo asociacija");
        try
        {
            var record = await moderator.Browser.RecordIdAsync(AppealSteps.ThirdBill);
            using (var set = await moderator.Browser.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.AppealThreshold, record), new ModeratorThresholdRequest { Threshold = 1, Statement = "Bandymas." }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
            }

            var (participant, _) = await Factory.MemberAsync();
            using var _p = participant;
            (await participant.TakeSideAsync(AppealSteps.ThirdBill, "for")).Dispose();
            var (rep, _) = await Factory.MemberAsync();
            using var _r = rep;
            var created = await rep.RegisterOrganisationAsync(name);
            var saved = await rep.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, "for", "Pozicija " + Marker + ".", "https://" + Marker + ".pavyzdys.test", 100);
            await moderator.Browser.ActAsync(OrganisationRoutes.PublishStatement, saved.Id, null);
            await Database.ExecuteAsync(
                "INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, first_seen, last_seen) VALUES (gen_random_uuid(), @b, @d, 'adoption', 'agenda', now(), now())",
                ("b", AppealSteps.ThirdBill), ("d", monday.AddDays(2)));

            IReadOnlyList<DigestRunInfo> made;
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var local = monday.ToDateTime(new TimeOnly(7, 15));
                made = await scope.ServiceProvider.GetRequiredService<IDigests>().RunDueAsync(new DateTimeOffset(local, Vilnius.GetUtcOffset(local)), CancellationToken.None);
            }

            var weekly = made.Single(r => r.Kind == "weekly");
            weekly.Items.ShouldContain(i => i.BillNumber == AppealSteps.ThirdBill);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM notify.digest_item d WHERE row_to_json(d)::text ILIKE @m OR row_to_json(d)::text ILIKE @n", ("m", "%" + Marker + "%"), ("n", "%" + name + "%"))).ShouldBe(0);
            using var stranger = NewBrowser();
            var date = monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var api = await stranger.Client.GetStringAsync(DigestRoutes.DigestOn(monday));
            api.ShouldNotContain(Marker, Case.Insensitive);
            api.ShouldNotContain(name, Case.Insensitive);
            var html = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + date));
            html.ShouldContain(AppealSteps.ThirdBill);
            html.ShouldNotContain(Marker, Case.Insensitive);
            html.ShouldNotContain(name, Case.Insensitive);
            var mps = await moderator.Browser.GetJsonAsync<List<DigestMpResponse>>(DigestRoutes.MpsOf(weekly.Id));
            var preview = await moderator.Browser.GetJsonAsync<DigestPreviewResponse>(DigestRoutes.PreviewOf(weekly.Id, mps[0].Slug));
            preview.Text.ShouldContain(AppealSteps.ThirdBill);
            preview.Text.ShouldNotContain(Marker, Case.Insensitive);
            preview.Text.ShouldNotContain(name, Case.Insensitive);
            preview.Text.ShouldNotContain("organizacij", Case.Insensitive);
        }
        finally
        {
            await SetAsync(admin.Browser, "Digest:ThresholdMinimum", 50);
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number = @b AND source = 'agenda' AND planned_date = @d", ("b", AppealSteps.ThirdBill), ("d", monday.AddDays(2)));
            await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET status = 'withdrawn' WHERE bill_number = @b", ("b", AppealSteps.ThirdBill));
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    /// <summary>A page without the markers Blazor writes for its islands, which carry a new id on every request.</summary>
    private static string Stable(string body) => System.Text.RegularExpressions.Regex.Replace(body, "<!--Blazor[^>]*-->", "");

    private async Task<string> CandidatesAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var candidates = await scope.ServiceProvider.GetRequiredService<IDigestSource>().CandidatesAsync(CancellationToken.None);
        return JsonSerializer.Serialize(candidates.Where(c => c.BillNumber == AppealSteps.SecondBill).ToList());
    }

    private async Task<string> DailyAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Vilnius).DateTime);
        await scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>().AggregateDayAsync(day, CancellationToken.None);
        return await Database.ScalarAsync<string>(
            "SELECT row_to_json(d)::text FROM appeals.daily_stat d JOIN appeals.appeal a ON a.id = d.appeal_id WHERE a.bill_number = @b AND d.day = @d",
            ("b", AppealSteps.SecondBill), ("d", day)) ?? "";
    }

    private static async Task SetAsync(TestBrowser admin, string key, object value)
    {
        using var response = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(key), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(value) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
