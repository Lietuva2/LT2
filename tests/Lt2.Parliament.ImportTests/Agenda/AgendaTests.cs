using Lt2.Parliament.Import.Agenda;
using Lt2.Parliament.ImportTests.Import;
using Lt2.Parliament.ImportTests.StandIn;
using Lt2.SeimasStandIn;

namespace Lt2.Parliament.ImportTests.Agenda;

/// <summary>Design 8.4: the upcoming agenda from lrs.lt pages (the stand-in's invented page here).</summary>
public sealed class AgendaParserTests
{
    private static readonly DateOnly Thursday = new(2026, 11, 12);

    private static string Page() => StandInResponses.RenderAgenda(StandInFixture.Responses().AgendaTemplate!, Thursday);

    [Fact]
    public void The_invented_page_gives_each_bill_its_sitting_and_stage()
    {
        var parse = AgendaParser.Parse(Page(), "http://seimas-standin.test/darbotvarke");

        parse.Succeeded.ShouldBeTrue();
        parse.Rows.Select(r => (r.SittingDate, r.BillNumber, r.Stage)).ShouldBe(
        [
            (new DateOnly(2026, 11, 17), "XVP-0533", "consideration"),
            (new DateOnly(2026, 11, 17), "XVP-0777", "presentation"),
            (new DateOnly(2026, 11, 19), "XVP-0412", "adoption"),
        ]);
        parse.Rows[0].SourceUrl.ShouldBe("https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/xvp-0533");
        parse.Rows[1].SourceUrl.ShouldBe("http://seimas-standin.test/darbotvarke");
    }

    [Theory]
    [InlineData("2026 m. gruodžio 1 d.", 2026, 12, 1)]
    [InlineData("2027 m. sausio 14 d. (ketvirtadienis)", 2027, 1, 14)]
    public void A_sitting_date_is_read_as_lrs_lt_writes_it(string text, int year, int month, int day) =>
        AgendaParser.DateOf(text).ShouldBe(new DateOnly(year, month, day));

    [Theory]
    [InlineData("2026 m. vasario 30 d.")]
    [InlineData("2026 m. lapkritis 17 d.")]
    public void A_date_that_cannot_be_one_is_not(string text) => AgendaParser.DateOf(text).ShouldBeNull();

    [Theory]
    [InlineData("Pateikimas", "presentation")]
    [InlineData("pakartotinis svarstymas", "consideration")]
    [InlineData("Priėmimo stadija", "adoption")]
    [InlineData("Informacija", null)]
    public void The_stage_is_read_from_the_row_in_any_form(string text, string? stage) => AgendaParser.StageOf(text).ShouldBe(stage);

    [Theory]
    [InlineData("XVP-2039 Seimo nutarimo projektas Pateikimas, svarstymas, priėmimas", "adoption")]
    [InlineData("XVP-1405 Įstatymo projektas Svarstymas, priėmimas", "adoption")]
    [InlineData("XVP-1406 Įstatymo projektas Pateikimas, svarstymas", "consideration")]
    [InlineData("XVP-1407 Dėl priėmimo į pilietybę tvarkos Pateikimas", "adoption")]
    public void A_combined_stage_is_its_furthest_and_the_first_stage_named_still_counts(string text, string stage) =>
        AgendaParser.StageOf(text).ShouldBe(stage);

    [Fact]
    public void A_page_without_a_single_sitting_date_is_refused_as_a_changed_layout()
    {
        var parse = AgendaParser.Parse("<html><body><table><tr><td>XVP-0412</td><td>Priėmimas</td></tr></table></body></html>", "u");
        parse.Succeeded.ShouldBeFalse();
        parse.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void Every_assumption_the_parser_names_is_in_the_register_and_nothing_else()
    {
        var register = File.ReadLines(Path.Combine(StandInFixture.SeimasDirectory, "..", "Agenda", "ASSUMPTIONS.md"))
            .Where(l => l.StartsWith("| A", StringComparison.Ordinal))
            .Select(l => l.Split('|')[1].Trim())
            .ToList();
        register.ShouldBe(AgendaAssumptions.All, ignoreOrder: true);
    }
}

/// <summary>The watcher against the stand-in and PostgreSQL: dates move, items go, a broken page changes nothing.</summary>
[Collection(ImportDatabaseGroup.Name)]
public sealed class AgendaWatcherTests(ImportDatabase database) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_watcher_keeps_the_page_raw_and_follows_moves_and_removals()
    {
        var responses = StandInFixture.Responses();
        await using var harness = new ImportHarness(database, responses);

        (await harness.RunAgendaAsync()).ShouldBe(new AgendaOutcome("succeeded", 3, 3, null));
        (await harness.RunAgendaAsync()).Changes.ShouldBe(0, "nothing moved");
        (await database.ScalarAsync<long>("SELECT count(*) FROM parliament.agenda_snapshot WHERE synthetic")).ShouldBe(2);
        var planned = await harness.PlannedAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31));
        planned.Select(p => p.BillNumber).ShouldBe(["XVP-0533", "XVP-0777", "XVP-0412"]);

        // XVP-0412 moves to the first sitting; XVP-0777 disappears from the page.
        responses.ReplaceAgenda(responses.AgendaTemplate!
            .Replace("<h2>{{sitting:2}} posėdis</h2>", "", StringComparison.Ordinal)
            .Replace("<tr><td>11.30</td><td>XVP-0777 Išgalvotas projektas, kurio nėra atviruosiuose duomenyse</td><td>Pateikimas</td></tr>", "", StringComparison.Ordinal));
        (await harness.RunAgendaAsync()).Changes.ShouldBe(3, "one new row for the moved bill; its old row, and XVP-0777's, gone");
        planned = await harness.PlannedAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31));
        planned.Select(p => (p.BillNumber, p.Date)).ShouldBe([("XVP-0412", new DateOnly(2026, 11, 17)), ("XVP-0533", new DateOnly(2026, 11, 17))]);
    }

    [Fact]
    public async Task A_page_it_cannot_read_is_an_error_and_changes_no_planned_date()
    {
        var responses = StandInFixture.Responses();
        await using var harness = new ImportHarness(database, responses);
        await harness.RunAgendaAsync();

        responses.ReplaceAgenda("<html><body><p>Puslapis pertvarkytas.</p><ul><li>XVP-0412</li></ul></body></html>");
        var refused = await harness.RunAgendaAsync();

        refused.Status.ShouldBe("refused");
        harness.Logs.Errors.ShouldContain(e => e.Contains("Agenda page refused", StringComparison.Ordinal));
        (await harness.PlannedAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31))).Count.ShouldBe(3);
        (await database.ScalarAsync<long>("SELECT count(*) FROM parliament.agenda_snapshot WHERE failure IS NOT NULL")).ShouldBe(1);
    }
}

/// <summary>Design 6.8, 8.4: a run that cannot read the page is recorded as failed and changes nothing.</summary>
[Collection(ImportDatabaseGroup.Name)]
public sealed class AgendaWatcherFailureTests(ImportDatabase database) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task An_agenda_address_off_the_allowlist_is_a_failed_run_with_an_error_line()
    {
        await using var harness = new ImportHarness(database, settings: new Dictionary<string, string?> { ["Agenda:BaseAddress"] = "https://www.lrs.test/" });

        var outcome = await harness.RunAgendaAsync();

        outcome.Status.ShouldBe("failed");
        outcome.Problem.ShouldBe("host not allowed");
        harness.Logs.Errors.ShouldContain(e => e.Contains("Agenda page could not be fetched", StringComparison.Ordinal));
        harness.TakeRequests().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agenda_page_that_never_answers_is_a_failed_run_and_the_planned_dates_stay()
    {
        await using (var first = new ImportHarness(database))
        {
            (await first.RunAgendaAsync()).Status.ShouldBe("succeeded");
        }

        await using var harness = new ImportHarness(database, agendaHandler: new NeverAnswers());

        var outcome = await harness.RunAgendaAsync();

        outcome.Status.ShouldBe("failed");
        harness.Logs.Errors.ShouldContain(e => e.Contains("TimeoutRejectedException", StringComparison.Ordinal));
        (await harness.PlannedAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31))).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_page_with_sitting_dates_but_no_bill_keeps_the_planned_items_and_is_recorded_as_failed()
    {
        var responses = StandInFixture.Responses();
        await using var harness = new ImportHarness(database, responses);
        await harness.RunAgendaAsync();

        responses.ReplaceAgenda("<html><body><h2>{{sitting:1}} posėdis</h2><table><tr><td>10.00</td><td>Klausimai bus paskelbti vėliau</td></tr></table></body></html>");
        var outcome = await harness.RunAgendaAsync();

        outcome.Status.ShouldBe("failed");
        outcome.Problem.ShouldBe(AgendaWatcher.EmptyPage);
        harness.Logs.Errors.ShouldContain(e => e.Contains("Agenda page refused", StringComparison.Ordinal));
        (await harness.PlannedAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31))).Count.ShouldBe(3);
        (await database.ScalarAsync<long>("SELECT count(*) FROM parliament.agenda_snapshot WHERE failure IS NOT NULL")).ShouldBe(1);
    }

    /// <summary>A server that accepts the request and never answers: the resilience pipeline's time-out ends it.</summary>
    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }
}

/// <summary>
/// The register against the real lrs.lt page. Skipped unless <c>LT2_LRS_LIVE=1</c>; <c>LT2_LRS_AGENDA_URL</c> gives the page
/// (the real address is assumption A7). It fetches once and reports what the parser found.
/// </summary>
public sealed class AgendaLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task The_live_agenda_page_reads_with_the_parsers_assumptions()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LT2_LRS_LIVE") == "1", "Set LT2_LRS_LIVE=1 (and LT2_LRS_AGENDA_URL) to check the agenda parser against lrs.lt.");
        var url = Environment.GetEnvironmentVariable("LT2_LRS_AGENDA_URL")
            ?? throw new InvalidOperationException("LT2_LRS_AGENDA_URL is required: the real address is not known yet (A7).");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Lietuva2.0-agenda-check/1.0");
        var html = await http.GetStringAsync(new Uri(url), TestContext.Current.CancellationToken);

        var parse = AgendaParser.Parse(html, url);

        output.WriteLine($"Failure: {parse.Failure ?? "none"}; rows: {parse.Rows.Count}");
        foreach (var row in parse.Rows)
        {
            output.WriteLine($"{row.SittingDate:yyyy-MM-dd} {row.BillNumber} {row.Stage ?? "(no stage)"} {row.SourceUrl}");
        }

        parse.Succeeded.ShouldBeTrue(parse.Failure);
    }
}