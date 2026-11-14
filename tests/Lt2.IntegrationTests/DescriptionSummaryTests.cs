using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Digests;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lt2.IntegrationTests;

/// <summary>
/// A configured AI provider for the summary job that never leaves the process: it records what it was given and answers
/// a fixed summary per bill (or the answer a test sets).
/// </summary>
public sealed class SummaryAssistant : IAiAssistant
{
    public ConcurrentQueue<DraftSource> Asked { get; } = new();

    public ConcurrentDictionary<string, string> Answers { get; } = new(StringComparer.Ordinal);

    public bool IsConfigured => true;

    /// <summary>The fake's summary of a bill; the marker lets tests find it anywhere it might leak.</summary>
    public static string Summary(string bill) => $"Projektas {bill} keičia rinkliavų nustatymo tvarką (DI santraukos bandymas).";

    public Task<Result<string>> DraftDescriptionAsync(DraftSource request, CancellationToken cancellationToken)
    {
        Asked.Enqueue(request);
        return Task.FromResult<Result<string>>(Answers.TryGetValue(request.BillNumber, out var answer) ? answer : Summary(request.BillNumber));
    }
}

/// <summary>A reader of the bills' official texts that serves what a test gives it: null for a bill means "unavailable".</summary>
public sealed class FakeBillTexts : IBillTextSource
{
    public ConcurrentDictionary<string, BillText?> Texts { get; } = new(StringComparer.Ordinal);

    public bool CanRead => true;

    public Task<BillText?> ReadAsync(string billNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Texts.GetValueOrDefault(billNumber));

    public static BillText Note(string bill) =>
        new($"Aiškinamasis raštas ({bill}). Projektu siūloma leisti savivaldybėms nustatyti rinkliavų dydį pagal sąnaudas.", $"https://e-seimas.lrs.lt/bandymas/{bill}");
}

/// <summary>The web application with a fake provider and a fake text source in place of the "not configured" and "unavailable" ones.</summary>
public sealed class SummaryFactory(DatabaseFixture database) : Lt2Factory(database)
{
    public SummaryAssistant Assistant { get; } = new();

    public FakeBillTexts TextSource { get; } = new();

    /// <summary>The texts the fake source serves, by bill.</summary>
    public ConcurrentDictionary<string, BillText?> Texts => TextSource.Texts;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiAssistant>();
            services.AddSingleton<IAiAssistant>(Assistant);
            services.RemoveAll<IBillTextSource>();
            services.AddSingleton<IBillTextSource>(TextSource);
        });
    }
}

/// <summary>
/// Design 8.13 over HTTP and the job: the description and its AI summary. With no provider and no reader nothing offers or
/// promises a summary and every other path works; with fakes (no test calls a real provider or reads e-seimas) the job
/// fills descriptions in the stated order, keeps the daily number, waits seven days after an unavailable or insufficient
/// text, never writes over a person's text or a removed one, and audits without the text. A reported summary reaches the
/// queue and is confirmed, corrected or removed; visitors and members see the label and the report control and nothing of
/// the moderators'; summaries to MPs carry no description.
/// </summary>
public sealed class DescriptionSummaryTests(DatabaseFixture database) : ApiTest(database), IAsyncDisposable
{
    private static readonly string[] Bills = [AppealSteps.TaxBill, AppealSteps.SecondBill, AppealSteps.ThirdBill];
    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");

    private SummaryFactory? _summaries;

    private SummaryFactory Summaries => _summaries ??= new SummaryFactory(Database);

    public async ValueTask DisposeAsync()
    {
        if (_summaries is not null)
        {
            await _summaries.DisposeAsync();
        }
    }

    /// <summary>The three bills without records, plans or earlier tries; the job then sees only what the test sets up.</summary>
    private async Task FreshAsync()
    {
        foreach (var bill in Bills)
        {
            await Database.FreeBillAsync(bill);
        }

        await Database.ExecuteAsync("DELETE FROM appeals.summary_attempt");
        await Database.ExecuteAsync("DELETE FROM parliament.planned_item WHERE bill_number = ANY(@b)", ("b", Bills));
    }

    private static async Task<SummaryRun> RunAsync(Lt2Factory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDescriptionSummaries>().RunAsync(CancellationToken.None);
    }

    private static async Task SetAsync(TestBrowser admin, string key, object value)
    {
        using var response = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(key), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(value) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private Task PlanAsync(string bill, DateOnly date) =>
        Database.ExecuteAsync(
            "INSERT INTO parliament.planned_item (id, bill_number, planned_date, stage, source, first_seen, last_seen) VALUES (gen_random_uuid(), @b, @d, 'adoption', 'agenda', now(), now())",
            ("b", bill), ("d", date));

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Vilnius).DateTime);

    /// <summary>Pretends the job's tries happened <paramref name="days"/> days earlier (its daily number and waits move with them).</summary>
    private Task AgeAttemptsAsync(int days) =>
        Database.ExecuteAsync(
            "UPDATE appeals.summary_attempt SET tried_at = tried_at - make_interval(days => @d), retry_after = retry_after - make_interval(days => @d)", ("d", days));

    [Fact]
    public async Task Without_a_reader_or_a_provider_nothing_offers_or_promises_a_summary_and_every_other_path_works()
    {
        await FreshAsync();
        var run = await RunAsync(Factory);
        run.ShouldBe(new SummaryRun("no-reader", 0, 0));
        (await RunAsync(Factory)).ShouldBe(new SummaryRun("no-reader", 0, 0), "and so again on the next run");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.appeal WHERE bill_number = ANY(@b)", ("b", Bills))).ShouldBe(0, "nothing was made");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.summary_attempt")).ShouldBe(0);

        using var visitor = NewBrowser();
        (await visitor.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules)).AiSummaries.ShouldBeFalse();
        var method = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/metodika"));
        method.ShouldContain("id=\"aprasymai\"");
        method.ShouldContain("Sukurta DI · žmonių neperžiūrėta", customMessage: "the labels are explained");
        method.ShouldNotContain("id=\"ai-summaries\"", customMessage: "nothing promises a summary");
        method.ShouldContain("src/Modules/Lt2.Appeals/Ai/description-draft.prompt.md", customMessage: "the page names both public prompts (8.13)");
        method.ShouldContain("src/Modules/Lt2.Appeals/Ai/description-summary.prompt.md", customMessage: "the page names both public prompts (8.13)");
        var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
        page.ShouldContain("Šio projekto aprašymo dar nėra.");
        page.ShouldNotContain("santrauk", Case.Insensitive);

        // A moderator still writes a description, and a member still takes a side.
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        var (member, _) = await Factory.MemberAsync("Giedrė Slapta");
        using var _m = member;
        var prepared = await Database.PrepareBillAsync(member, moderator.Browser, AppealSteps.SecondBill, "for");
        (await visitor.AppealAsync(AppealSteps.SecondBill)).DescriptionSource.ShouldBe("moderator");
        prepared.Number.ShouldBe(AppealSteps.SecondBill);
    }

    [Fact]
    public async Task With_a_provider_and_texts_the_job_fills_descriptions_planned_first_then_by_participants_within_the_daily_number()
    {
        await FreshAsync();
        using var admin = await StaffMembers.ActiveAsync(Summaries, Database, "administrator");
        await SetAsync(admin.Browser, "Ai:SummariesPerDay", 2);
        try
        {
            foreach (var bill in Bills)
            {
                Summaries.Texts[bill] = FakeBillTexts.Note(bill);
            }

            // XVP-0533 is planned soonest, XVP-0618 later; XVP-0412 is not planned but has a participant.
            await PlanAsync(AppealSteps.SecondBill, Today.AddDays(3));
            await PlanAsync(AppealSteps.ThirdBill, Today.AddDays(10));
            var (member, _) = await Summaries.MemberAsync("Giedrė Slapta");
            using var _m = member;
            (await member.TakeSideAsync(AppealSteps.TaxBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using var visitor = Summaries.NewBrowser();
            (await visitor.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules)).AiSummaries.ShouldBeTrue();
            WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/metodika")).ShouldContain("id=\"ai-summaries\"");

            var asked = Summaries.Assistant.Asked.Count;
            (await RunAsync(Summaries)).ShouldBe(new SummaryRun("done", 2, 2));
            Summaries.Assistant.Asked.Skip(asked).Select(a => a.BillNumber).ShouldBe([AppealSteps.SecondBill, AppealSteps.ThirdBill], "planned first, soonest first");

            // Only the number, the title and the official text went to the provider, with the job's own prompt (decided
            // by the owner on 2026-11-14), not the moderator's draft prompt.
            var sent = Summaries.Assistant.Asked.Skip(asked).First();
            sent.ShouldBe(new DraftSource(AppealSteps.SecondBill, "Vietinių rinkliavų įstatymo pataisa", FakeBillTexts.Note(AppealSteps.SecondBill).Text, DraftPurpose.Summary));
            Summaries.Assistant.Asked.Skip(asked).ShouldAllBe(a => a.Purpose == DraftPurpose.Summary);

            var made = await visitor.AppealAsync(AppealSteps.SecondBill);
            made.Description.ShouldBe(SummaryAssistant.Summary(AppealSteps.SecondBill));
            made.DescriptionSource.ShouldBe("ai");
            made.DescriptionUrl.ShouldBe(FakeBillTexts.Note(AppealSteps.SecondBill).Address);
            made.DescriptionAt.ShouldNotBeNull().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
            (await visitor.GetJsonAsync<List<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(AppealSteps.SecondBill)))
                .ShouldHaveSingleItem().NewText.ShouldBe(SummaryAssistant.Summary(AppealSteps.SecondBill));
            (await visitor.AppealAsync(AppealSteps.TaxBill)).Description.ShouldBeEmpty("the daily number was reached");

            // Audited with the system as the actor: the bill and the model, never the text.
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'description.summary.made' AND target = @t AND actor_user_id IS NULL AND actor_role = 'system' AND details->>'model' = 'claude-sonnet-5-5'",
                ("t", "bill:" + AppealSteps.SecondBill))).ShouldBe(1);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'description.summary.made' AND coalesce(details::text, '') LIKE '%santraukos bandymas%'")).ShouldBe(0);

            // The same day nothing more is asked; the next day the bill with the participant gets its summary.
            (await RunAsync(Summaries)).ShouldBe(new SummaryRun("done", 0, 0));
            await AgeAttemptsAsync(1);
            (await RunAsync(Summaries)).ShouldBe(new SummaryRun("done", 1, 1));
            (await visitor.AppealAsync(AppealSteps.TaxBill)).DescriptionSource.ShouldBe("ai");
            (await visitor.AppealAsync(AppealSteps.TaxBill)).Counts.For.ShouldBe(1, "the side taken before stays on the record the summary joined");

            // 0 switches the job off.
            await SetAsync(admin.Browser, "Ai:SummariesPerDay", 0);
            await FreshAsync();
            (await RunAsync(Summaries)).ShouldBe(new SummaryRun("off", 0, 0));
            (await visitor.GetJsonAsync<AppealRulesResponse>(AppealRoutes.Rules)).AiSummaries.ShouldBeFalse();
        }
        finally
        {
            await SetAsync(admin.Browser, "Ai:SummariesPerDay", 30);
            Summaries.Texts.Clear();
            await FreshAsync();
        }
    }

    [Fact]
    public async Task An_unavailable_or_insufficient_text_is_tried_again_only_after_seven_days_and_nothing_is_stored()
    {
        await FreshAsync();
        try
        {
            Summaries.Texts[AppealSteps.SecondBill] = null;
            Summaries.Texts[AppealSteps.ThirdBill] = FakeBillTexts.Note(AppealSteps.ThirdBill);
            Summaries.Assistant.Answers[AppealSteps.ThirdBill] = "„Iš pateikto teksto neaišku, ką projektas keičia.“";
            Summaries.Texts[AppealSteps.TaxBill] = new BillText(new string('a', 20_001), "https://e-seimas.lrs.lt/bandymas/ilgas");

            var asked = Summaries.Assistant.Asked.Count;
            (await RunAsync(Summaries)).ShouldBe(new SummaryRun("done", 1, 0));
            Summaries.Assistant.Asked.Skip(asked).Select(a => a.BillNumber).ShouldBe([AppealSteps.ThirdBill], "an unavailable or over-long text is never sent");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.appeal WHERE bill_number = ANY(@b)", ("b", Bills))).ShouldBe(0, "nothing stored, no record made");
            (await Database.ScalarAsync<string>("SELECT outcome FROM appeals.summary_attempt WHERE bill_number = @b", ("b", AppealSteps.SecondBill))).ShouldBe("unavailable");
            (await Database.ScalarAsync<string>("SELECT outcome FROM appeals.summary_attempt WHERE bill_number = @b", ("b", AppealSteps.ThirdBill))).ShouldBe("insufficient");
            (await Database.ScalarAsync<string>("SELECT outcome FROM appeals.summary_attempt WHERE bill_number = @b", ("b", AppealSteps.TaxBill))).ShouldBe("unavailable");

            // Six days later: still waiting. Seven days: tried again.
            Summaries.Texts[AppealSteps.SecondBill] = FakeBillTexts.Note(AppealSteps.SecondBill);
            await AgeAttemptsAsync(6);
            (await RunAsync(Summaries)).ShouldBe(new SummaryRun("done", 0, 0));
            await AgeAttemptsAsync(1);
            (await RunAsync(Summaries)).ShouldBe(new SummaryRun("done", 2, 1));
            (await Database.ScalarAsync<string>("SELECT description_source FROM appeals.appeal WHERE bill_number = @b", ("b", AppealSteps.SecondBill))).ShouldBe("ai");
        }
        finally
        {
            Summaries.Texts.Clear();
            Summaries.Assistant.Answers.Clear();
            await FreshAsync();
        }
    }

    [Fact]
    public async Task The_job_never_writes_over_a_persons_text_or_a_removed_one()
    {
        await FreshAsync();
        using var moderator = await StaffMembers.ActiveAsync(Summaries, Database, "moderator");
        try
        {
            foreach (var bill in Bills)
            {
                Summaries.Texts[bill] = FakeBillTexts.Note(bill);
            }

            // A moderator's own text on XVP-0533; an AI summary on XVP-0618 that a moderator then removes.
            using (var written = await moderator.Browser.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill),
                new ModeratorDescriptionRequest { Description = AppealSteps.Description, Statement = "Bandymas." }))
            {
                written.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            Summaries.Texts[AppealSteps.TaxBill] = null;
            (await RunAsync(Summaries)).Made.ShouldBe(1);
            (await moderator.Browser.AppealAsync(AppealSteps.ThirdBill)).DescriptionSource.ShouldBe("ai");
            using (var removed = await moderator.Browser.PostAsync(AppealRoutes.RemoveDescriptionOf(AppealSteps.ThirdBill), new StatementRequest { Statement = "Santrauka klaidinga." }))
            {
                removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());
            }

            // A week later, with every text readable, the job writes neither.
            Summaries.Texts[AppealSteps.TaxBill] = FakeBillTexts.Note(AppealSteps.TaxBill);
            await AgeAttemptsAsync(8);
            var asked = Summaries.Assistant.Asked.Count;
            (await RunAsync(Summaries)).ShouldBe(new SummaryRun("done", 1, 1));
            Summaries.Assistant.Asked.Skip(asked).Select(a => a.BillNumber).ShouldBe([AppealSteps.TaxBill]);
            var second = await moderator.Browser.AppealAsync(AppealSteps.SecondBill);
            (second.Description, second.DescriptionSource).ShouldBe((AppealSteps.Description, "moderator"));
            var third = await moderator.Browser.AppealAsync(AppealSteps.ThirdBill);
            (third.Description, third.DescriptionSource).ShouldBe(("", (string?)null));
        }
        finally
        {
            Summaries.Texts.Clear();
            await FreshAsync();
        }
    }

    [Fact]
    public async Task A_reported_summary_reaches_the_queue_and_is_confirmed_corrected_or_removed()
    {
        await FreshAsync();
        using var moderator = await StaffMembers.ActiveAsync(Summaries, Database, "moderator");
        var (member, _) = await Summaries.MemberAsync("Rūta Mockutė");
        using var _m = member;
        try
        {
            foreach (var bill in Bills)
            {
                Summaries.Texts[bill] = FakeBillTexts.Note(bill);
            }

            (await RunAsync(Summaries)).Made.ShouldBe(3);
            var records = new Dictionary<string, Guid>();
            foreach (var bill in Bills)
            {
                records[bill] = (await member.AppealAsync(bill)).Id!.Value;
            }

            // A member reports an error in the description; an argument cannot be reported as inaccurate.
            using (var reported = await member.PostAsync(AppealRoutes.Reports,
                new ReportRequest { TargetKind = "appeal", TargetId = records[AppealSteps.SecondBill], Ground = "inaccurate", Text = "Rinkliavų dydį nustato ne savivaldybės." }))
            {
                reported.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reported.Content.ReadAsStringAsync());
            }

            using (var wrongGround = await member.PostAsync(AppealRoutes.Reports,
                new ReportRequest { TargetKind = "reason", TargetId = Guid.NewGuid(), Ground = "inaccurate" }))
            {
                wrongGround.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            var queue = await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue);
            var item = queue.Appeals.Single(a => a.Id == records[AppealSteps.SecondBill]);
            (item.Description, item.DescriptionSource).ShouldBe((SummaryAssistant.Summary(AppealSteps.SecondBill), "ai"));
            item.Reports.ShouldHaveSingleItem().Ground.ShouldBe("inaccurate");

            // Only a moderator acts; a member is refused.
            using (var byMember = await member.PostAsync(AppealRoutes.ConfirmDescriptionOf(AppealSteps.SecondBill), new ModerationRequest()))
            {
                byMember.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            }

            // Confirm: the text stays, the label becomes "Sukurta DI · taisė žmonės", the report closes, audited.
            using (var confirmed = await moderator.Browser.PostAsync(AppealRoutes.ConfirmDescriptionOf(AppealSteps.SecondBill), new ModerationRequest()))
            {
                confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync());
            }

            var confirmedNow = await member.AppealAsync(AppealSteps.SecondBill);
            (confirmedNow.Description, confirmedNow.DescriptionSource).ShouldBe((SummaryAssistant.Summary(AppealSteps.SecondBill), "ai-draft"));
            confirmedNow.DescriptionUrl.ShouldBe(FakeBillTexts.Note(AppealSteps.SecondBill).Address, "still the text it was made from");
            (await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.ShouldNotContain(a => a.Id == records[AppealSteps.SecondBill]);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'moderation.appeal.confirm' AND target = @t", ("t", "appeal:" + records[AppealSteps.SecondBill]))).ShouldBe(1);
            using (var again = await moderator.Browser.PostAsync(AppealRoutes.ConfirmDescriptionOf(AppealSteps.SecondBill), new ModerationRequest()))
            {
                await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
            }

            // Correct: a moderator's correction of an AI summary is "Sukurta DI · taisė žmonės" too. The form starts from the
            // address the job stored, so leaving the field alone keeps it (decided by the owner on 2026-11-14).
            var prefilled = (await moderator.Browser.ModerationBillAsync(AppealSteps.ThirdBill)).DescriptionUrl;
            prefilled.ShouldBe(FakeBillTexts.Note(AppealSteps.ThirdBill).Address);
            using (var corrected = await moderator.Browser.SendAsync(HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.ThirdBill),
                new ModeratorDescriptionRequest { Description = "Projektas keičia atliekų tvarkymo rinkliavos nustatymą.", Statement = "Pataisyta pagal aiškinamąjį raštą.", DescriptionUrl = prefilled }))
            {
                corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent, await corrected.Content.ReadAsStringAsync());
            }

            var correctedNow = await member.AppealAsync(AppealSteps.ThirdBill);
            correctedNow.DescriptionSource.ShouldBe("ai-draft");
            correctedNow.DescriptionUrl.ShouldBe(FakeBillTexts.Note(AppealSteps.ThirdBill).Address, "kept by leaving the field as it was filled");

            // Remove: a statement is required; the bill then says it has none and the history shows the removal.
            using (var noStatement = await moderator.Browser.PostAsync(AppealRoutes.RemoveDescriptionOf(AppealSteps.TaxBill), new StatementRequest()))
            {
                noStatement.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            using (var removed = await moderator.Browser.PostAsync(AppealRoutes.RemoveDescriptionOf(AppealSteps.TaxBill), new StatementRequest { Statement = "Santrauka klaidinga." }))
            {
                removed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync());
            }

            var removedNow = await member.AppealAsync(AppealSteps.TaxBill);
            (removedNow.Description, removedNow.DescriptionSource, removedNow.DescriptionUrl, removedNow.DescriptionAt).ShouldBe(("", (string?)null, (string?)null, (DateTimeOffset?)null));
            var history = await member.GetJsonAsync<List<DescriptionRevisionResponse>>(AppealRoutes.HistoryOf(AppealSteps.TaxBill));
            (history[^1].OldText, history[^1].NewText).ShouldBe((SummaryAssistant.Summary(AppealSteps.TaxBill), (string?)null));
            WebUtility.HtmlDecode(await member.Client.GetStringAsync($"/seimas/{AppealSteps.TaxBill}/istorija")).ShouldContain("aprašymas pašalintas");
            using (var nothingLeft = await moderator.Browser.PostAsync(AppealRoutes.RemoveDescriptionOf(AppealSteps.TaxBill), new StatementRequest { Statement = "Dar kartą." }))
            {
                await nothingLeft.ShouldBeProblemAsync(HttpStatusCode.Conflict, "moderation.wrong-state");
            }

            using (var unknown = await moderator.Browser.PostAsync(AppealRoutes.RemoveDescriptionOf("XVP-9999"), new StatementRequest { Statement = "Bandymas." }))
            {
                unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            }
        }
        finally
        {
            Summaries.Texts.Clear();
            await FreshAsync();
        }
    }

    [Fact]
    public async Task A_visitor_and_a_member_see_the_label_the_source_and_the_report_control_and_nothing_of_the_moderators()
    {
        await FreshAsync();
        try
        {
            Summaries.Texts[AppealSteps.SecondBill] = FakeBillTexts.Note(AppealSteps.SecondBill);
            (await RunAsync(Summaries)).Made.ShouldBe(1);

            using var visitor = Summaries.NewBrowser();
            var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
            page.ShouldContain(SummaryAssistant.Summary(AppealSteps.SecondBill));
            page.ShouldContain("Sukurta DI · žmonių neperžiūrėta");
            page.ShouldContain($"href=\"{FakeBillTexts.Note(AppealSteps.SecondBill).Address}\"");
            page.ShouldContain("id=\"description-made\"");
            page.ShouldContain("Pranešti apie klaidą");
            page.ShouldNotContain("id=\"moderation-panel\"");
            page.ShouldNotContain("Patvirtinti aprašymą");

            var (member, _) = await Summaries.MemberAsync("Giedrė Slapta");
            using var _m = member;
            var mine = WebUtility.HtmlDecode(await member.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
            mine.ShouldContain("Sukurta DI · žmonių neperžiūrėta");
            mine.ShouldContain("id=\"report-appeal\"");
            mine.ShouldNotContain("id=\"moderation-panel\"");
            mine.ShouldNotContain("Patvirtinti aprašymą");
            mine.ShouldNotContain("Pašalinti aprašymą");
            (await member.Client.GetAsync(AppealRoutes.ModerationBillOf(AppealSteps.SecondBill))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await visitor.Client.GetAsync(AppealRoutes.ModerationBillOf(AppealSteps.SecondBill))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            using (var byVisitor = await visitor.PostAsync(AppealRoutes.RemoveDescriptionOf(AppealSteps.SecondBill), new StatementRequest { Statement = "Bandymas." }))
            {
                byVisitor.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }
        }
        finally
        {
            Summaries.Texts.Clear();
            await FreshAsync();
        }
    }

    [Fact]
    public async Task Summaries_to_MPs_and_their_public_copies_carry_no_description()
    {
        await FreshAsync();
        using var admin = await StaffMembers.ActiveAsync(Summaries, Database, "administrator");
        using var moderator = await StaffMembers.ActiveAsync(Summaries, Database, "moderator");
        await SetAsync(admin.Browser, "Digest:ThresholdMinimum", 1);
        var today = Today;
        var monday = today.AddDays(7 * 11 - (((int)today.DayOfWeek + 6) % 7));
        try
        {
            Summaries.Texts[AppealSteps.ThirdBill] = FakeBillTexts.Note(AppealSteps.ThirdBill);
            (await RunAsync(Summaries)).Made.ShouldBe(1);
            var record = (await moderator.Browser.AppealAsync(AppealSteps.ThirdBill)).Id!.Value;
            using (var set = await moderator.Browser.SendAsync(HttpMethod.Put, AppealRoutes.With(AppealRoutes.AppealThreshold, record), new ModeratorThresholdRequest { Threshold = 1, Statement = "Bandymas." }))
            {
                set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());
            }

            var (member, _) = await Summaries.MemberAsync("Giedrė Slapta");
            using var _m = member;
            (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await PlanAsync(AppealSteps.ThirdBill, monday.AddDays(2));

            IReadOnlyList<DigestRunInfo> made;
            await using (var scope = Summaries.Services.CreateAsyncScope())
            {
                var local = monday.ToDateTime(new TimeOnly(7, 15));
                made = await scope.ServiceProvider.GetRequiredService<IDigests>().RunDueAsync(new DateTimeOffset(local, Vilnius.GetUtcOffset(local)), CancellationToken.None);
            }

            var weekly = made.Single(r => r.Kind == "weekly");
            weekly.Items.ShouldContain(i => i.BillNumber == AppealSteps.ThirdBill, "the bill is in the summary");

            // Neither the frozen snapshot, nor the public copy (API and page), nor what an MP would read holds the description.
            const string Marker = "santraukos bandymas";
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.digest_item d WHERE row_to_json(d)::text ILIKE @m", ("m", "%" + Marker + "%"))).ShouldBe(0);
            using var stranger = Summaries.NewBrowser();
            using (var api = await stranger.Client.GetAsync(DigestRoutes.DigestOn(monday)))
            {
                (await api.Content.ReadAsStringAsync()).ShouldNotContain(Marker, Case.Insensitive);
            }

            var date = monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var html = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/suvestines/" + date));
            html.ShouldContain(AppealSteps.ThirdBill);
            html.ShouldNotContain(Marker, Case.Insensitive);
            var mps = await moderator.Browser.GetJsonAsync<List<DigestMpResponse>>(DigestRoutes.MpsOf(weekly.Id));
            var preview = await moderator.Browser.GetJsonAsync<DigestPreviewResponse>(DigestRoutes.PreviewOf(weekly.Id, mps[0].Slug));
            preview.Text.ShouldContain(AppealSteps.ThirdBill);
            preview.Text.ShouldNotContain(Marker, Case.Insensitive);
        }
        finally
        {
            await SetAsync(admin.Browser, "Digest:ThresholdMinimum", 50);
            Summaries.Texts.Clear();
            await FreshAsync();
        }
    }
}
