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
/// their order, the agenda, the front page, the daily statistics and the bills the summaries to MPs hold are the same with
/// and without statements. A summary names organisations under a bill (design 8.14.4) by name only, never a statement's text.
/// </summary>
public sealed class OrganisationCountTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly TimeZoneInfo Vilnius = Lt2.SharedKernel.VilniusTime.Zone;

    /// <summary>JSON with Lithuanian letters as they are, so a name or the marker can be looked for in it.</summary>
    private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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
            AppealRoutes.RecordOf(AppealSteps.SecondBill), AppealRoutes.Appeals, AppealRoutes.AppealsList(null, false, false, null, "participants"),
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

    /// <summary>
    /// Design 8.14.4, 8.14.9 (owner, 2026-11-20): a summary names, under the bill, the confirmed organisations whose statement
    /// was public when it was prepared, by name with a link to the statement, never its text; not an unconfirmed one, a blocked
    /// one, one a moderator keeps out of summaries (also after the summary was prepared) or one whose statement became public
    /// after it was prepared. The moderators' screen shows the line as it would go; the snapshot, its public answer and page
    /// and the screen afterwards show it as it went.
    /// </summary>
    [Fact]
    public async Task A_summary_names_the_confirmed_organisations_public_when_it_was_prepared_by_name_only()
    {
        using var admin = await StaffMembers.SharedAsync(Factory, Database, "administrator");
        using var moderator = await StaffMembers.SharedAsync(Factory, Database, "moderator");
        await admin.Browser.SetSettingAsync("Digest:ThresholdMinimum", 1);
        await Database.FreeBillAsync(AppealSteps.ThirdBill);
        await Database.ClearStatementsAsync(AppealSteps.ThirdBill);
        var monday = ReasonsToComeSteps.MondayInWeeks(13);
        const string Marker = "organizacijosžymabandymui";
        var names = new Dictionary<string, string>
        {
            ["confirmed"] = OrganisationSteps.NewName("Patvirtinta suvestinių asociacija"),
            ["unconfirmed"] = OrganisationSteps.NewName("Nepatvirtinta suvestinių asociacija"),
            ["blocked"] = OrganisationSteps.NewName("Užblokuota suvestinių asociacija"),
            ["kept-out"] = OrganisationSteps.NewName("Neįtraukiama suvestinių asociacija"),
            ["late"] = OrganisationSteps.NewName("Vėlesnė suvestinių asociacija"),
        };
        try
        {
            var record = await moderator.Browser.RecordIdAsync(AppealSteps.ThirdBill);
            using (var set = await moderator.Browser.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.RecordThreshold, record), new ModeratorThresholdRequest { Threshold = 1, Statement = "Bandymas." }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
            }

            var (participant, _) = await Factory.MemberAsync();
            using var _p = participant;
            (await participant.TakeSideAsync(AppealSteps.ThirdBill, "for")).Dispose();

            // Five organisations (at most three per account), each with a statement on the bill whose text and link carry the marker.
            var (rep1, _) = await Factory.MemberAsync();
            using var _r1 = rep1;
            var (rep2, _) = await Factory.MemberAsync();
            using var _r2 = rep2;
            var ids = new Dictionary<string, Guid>();
            var statements = new Dictionary<string, Guid>();
            foreach (var (key, rep, stance) in new[] { ("confirmed", rep1, "for"), ("unconfirmed", rep1, "for"), ("blocked", rep1, "against"), ("kept-out", rep2, "against"), ("late", rep2, "remarks") })
            {
                var created = await rep.RegisterOrganisationAsync(names[key]);
                ids[key] = created.Id;
                if (key != "unconfirmed")
                {
                    await moderator.Browser.ActAsync(OrganisationRoutes.Confirm, created.Id, "Patikrinta svetainė.");
                }

                var saved = await rep.PublishStatementAsync(AppealSteps.ThirdBill, created.Slug, stance, "Pozicija " + Marker + ".", "https://" + Marker + ".pavyzdys.test", stance == "remarks" ? null : 100);
                await moderator.Browser.ActAsync(OrganisationRoutes.PublishStatement, saved.Id, null);
                statements[key] = saved.Id;
            }

            await moderator.Browser.ActAsync(OrganisationRoutes.Block, ids["blocked"], "Apsimeta kita organizacija.");
            await Database.ExecuteAsync(
                "INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, first_seen, last_seen) VALUES (gen_random_uuid(), @b, @d, 'adoption', 'agenda', now(), now())",
                ("b", AppealSteps.ThirdBill), ("d", monday.AddDays(2)));

            async Task<IReadOnlyList<DigestRunInfo>> RunAtAsync(DateOnly day)
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var local = day.ToDateTime(new TimeOnly(7, 15));
                return await scope.ServiceProvider.GetRequiredService<IDigests>().RunDueAsync(new DateTimeOffset(local, Vilnius.GetUtcOffset(local)), CancellationToken.None);
            }

            // Prepared on Sunday, a day before it goes (design 8.19.2); the "late" statement first became public after that.
            var prepared = (await RunAtAsync(monday.AddDays(-1))).Single(r => r.Kind == "weekly");
            prepared.Status.ShouldBe("prepared");
            await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET first_public_at = @at WHERE id = @s", ("at", prepared.CreatedAt.AddHours(1)), ("s", statements["late"]));

            async Task<DigestReviewItemResponse> ScreenItemAsync() =>
                (await moderator.Browser.GetJsonAsync<DigestModerationResponse>(DigestRoutes.Moderation)).Runs.Single(r => r.Id == prepared.Id)
                    .Items.Single(i => i.BillNumber == AppealSteps.ThirdBill);

            var review = await ScreenItemAsync();
            review.Organisations.ShouldNotBeNull().Select(o => o.Name).ShouldBe([names["confirmed"], names["kept-out"]], "for, then against");
            review.Organisations!.Select(o => o.StatementId).ShouldBe([statements["confirmed"], statements["kept-out"]]);
            System.Text.Json.JsonSerializer.Serialize(review, Readable).ShouldNotContain(Marker, Case.Insensitive);

            // A moderator keeps one out while the summary waits: it drops out of this summary too.
            await moderator.Browser.ActAsync(OrganisationRoutes.KeepOutOfSummaries, ids["kept-out"], "Organizacijos pavadinimas klaidina.");
            (await ScreenItemAsync()).Organisations!.Select(o => o.Name).ShouldBe([names["confirmed"]]);

            var gone = (await RunAtAsync(monday)).Single(r => r.Id == prepared.Id);
            gone.Status.ShouldBe("dry-run");
            var item = gone.Items.Single(i => i.BillNumber == AppealSteps.ThirdBill);
            item.Organisations.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(
                new DigestOrganisation("for", ids["confirmed"], item.Organisations![0].Slug, names["confirmed"], statements["confirmed"]));

            // The snapshot holds the name, never the statement's text or link, nor the organisations left out.
            var row = await Database.ScalarAsync<string>(
                "SELECT row_to_json(d)::text FROM notify.digest_item d WHERE d.run_id = @r AND d.bill_number = @b", ("r", prepared.Id), ("b", AppealSteps.ThirdBill));
            row.ShouldNotBeNull().ShouldContain(names["confirmed"]);
            row.ShouldNotContain(Marker, Case.Insensitive);
            foreach (var left in new[] { "unconfirmed", "blocked", "kept-out", "late" })
            {
                row.ShouldNotContain(names[left], Case.Insensitive);
            }

            using var stranger = NewBrowser();
            var date = monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var api = await stranger.Client.GetStringAsync(DigestRoutes.DigestOn(monday));
            var html = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + date));
            var screen = System.Text.Json.JsonSerializer.Serialize(await ScreenItemAsync(), Readable);
            html.ShouldContain(AppealSteps.ThirdBill);
            System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").ShouldContain("Organizacijos, prašančios balsuoti už: " + names["confirmed"] + ".");
            html.ShouldContain("/seimas/" + AppealSteps.ThirdBill + "#statement-" + statements["confirmed"]);
            foreach (var shown in new[] { api, html, screen })
            {
                shown.ShouldContain(names["confirmed"]);
                shown.ShouldNotContain(Marker, Case.Insensitive);
                foreach (var left in new[] { "unconfirmed", "blocked", "kept-out", "late" })
                {
                    shown.ShouldNotContain(names[left], Case.Insensitive);
                }
            }
        }
        finally
        {
            await admin.Browser.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number = @b AND source = 'agenda' AND planned_date = @d", ("b", AppealSteps.ThirdBill), ("d", monday.AddDays(2)));
            await Database.ExecuteAsync("UPDATE appeals.organisation_statement SET status = 'withdrawn' WHERE bill_number = @b", ("b", AppealSteps.ThirdBill));
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    /// <summary>A page without the markers Blazor writes for its islands, which carry a new id on every request.</summary>
    /// <summary>
    /// A page or answer without Blazor's markers and without a bill's last activity (owner, 2026-11-17: a statement made public is
    /// the bill's activity, design 8.12.5; only the "naujausia" order reads it, and these comparisons are of the others).
    /// </summary>
    private static string Stable(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(body, "<!--Blazor[^>]*-->", ""),
            "\"lastActivityAt\":(\"[^\"]*\"|null)",
            "\"lastActivityAt\":…");

    private async Task<string> CandidatesAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var candidates = await scope.ServiceProvider.GetRequiredService<IDigestSource>().CandidatesAsync(CancellationToken.None);
        return JsonSerializer.Serialize(candidates.Where(c => c.BillNumber == AppealSteps.SecondBill).ToList());
    }

    private async Task<string> DailyAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var day = ReasonsToComeSteps.VilniusToday();
        await scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>().AggregateDayAsync(day, CancellationToken.None);
        return await Database.ScalarAsync<string>(
            // The statistics only: when the row was computed is bookkeeping and moves with every run.
            "SELECT (to_jsonb(d) - 'computed_at')::text FROM appeals.daily_stat d JOIN appeals.bill_record a ON a.id = d.record_id WHERE a.bill_number = @b AND d.day = @d",
            ("b", AppealSteps.SecondBill), ("d", day)) ?? "";
    }
}
