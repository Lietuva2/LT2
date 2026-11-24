using System.Net;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Jobs;
using Lt2.Parliament;
using Lt2.Web.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 7.2 (owner 2026-11-18): the import's Hangfire jobs as the worker performs them, over the real Hangfire storage and
/// with the import itself replaced: the scheduled job says on the dashboard's job page why it imported nothing, and each
/// manual job forces its part, records its outcome, and fails when it could not run.
/// </summary>
public sealed class ParliamentImportJobTests(DatabaseFixture database) : ApiTest(database), IDisposable
{
    private readonly List<string> _jobs = [];
    private readonly FakeCards _cards = new();

    /// <summary>The jobs a test stored are deleted (Hangfire's Deleted state), so nothing of them is left to run.</summary>
    public void Dispose()
    {
        var client = Factory.Services.GetRequiredService<IBackgroundJobClient>();
        foreach (var id in _jobs)
        {
            client.Delete(id);
        }
    }

    [Fact]
    public async Task The_scheduled_import_triggered_when_not_due_says_so_in_the_log_and_on_the_jobs_page()
    {
        using var admin = await AdministratorAsync();
        var import = new FakeImport { Due = new ImportDue(false, new DateTimeOffset(2026, 11, 18, 12, 30, 0, TimeSpan.Zero)) };
        var logs = new Logs<ParliamentImportJob>();

        var id = await PerformAsync<ParliamentImportJob>(
            job => job.RunAsync(null, CancellationToken.None),
            context => new ParliamentImportJob(import, _cards, logs).RunAsync(context, CancellationToken.None));

        import.Runs.ShouldBeEmpty();
        logs.Lines.ShouldBe(["Information: Parliament import not due (last start 2026-11-18 14:30); skipped"]);
        (await ParameterAsync(id, ParliamentImportJob.SkippedParameter)).ShouldStartWith("\"Not due (last start 2026-11-18 14:30).");
        var page = WebUtility.HtmlDecode(await admin.Browser.Client.GetStringAsync(JobsDashboard.Path + "/jobs/details/" + id));
        page.ShouldContain("Not due (last start 2026-11-18 14:30)");
        page.ShouldContain("Seimas import (scheduled: runs only when due)");
    }

    [Fact]
    public async Task The_scheduled_import_when_due_runs_without_forcing_anything()
    {
        var import = new FakeImport { Due = new ImportDue(true, null) };

        var id = await PerformAsync<ParliamentImportJob>(
            job => job.RunAsync(null, CancellationToken.None),
            context => new ParliamentImportJob(import, _cards, new Logs<ParliamentImportJob>()).RunAsync(context, CancellationToken.None));

        import.Runs.ShouldBe([ImportParts.None]);
        (await ParameterAsync(id, ParliamentImportJob.SkippedParameter)).ShouldBeNull();
    }

    [Theory]
    [InlineData("MembersAsync", ImportParts.Members)]
    [InlineData("SittingsAsync", ImportParts.Sittings)]
    [InlineData("VotesAsync", ImportParts.Votes)]
    [InlineData("EverythingAsync", ImportParts.All)]
    public async Task Each_manual_job_forces_its_part_whatever_the_schedule_and_records_the_outcome(string method, ImportParts part)
    {
        var import = new FakeImport { Due = new ImportDue(false, DateTimeOffset.UtcNow), Outcome = new ImportOutcome(Guid.NewGuid(), "succeeded", 20, 3, false, null) };
        var job = new ParliamentImportPartsJob(import, _cards, new Logs<ParliamentImportPartsJob>());

        var id = await PerformAsync<ParliamentImportPartsJob>(
            j => j.MembersAsync(null, CancellationToken.None),
            context => method switch
            {
                "MembersAsync" => job.MembersAsync(context, CancellationToken.None),
                "SittingsAsync" => job.SittingsAsync(context, CancellationToken.None),
                "VotesAsync" => job.VotesAsync(context, CancellationToken.None),
                _ => job.EverythingAsync(context, CancellationToken.None),
            });

        import.Runs.ShouldBe([part]);
        (await ParameterAsync(id, ParliamentImportPartsJob.OutcomeParameter)).ShouldBe("\"succeeded: 20 requests, 3 changes\"");
    }

    [Fact]
    public async Task A_manual_job_that_finds_another_import_running_fails_so_the_dashboard_lists_it()
    {
        var import = new FakeImport { Outcome = new ImportOutcome(null, "skipped", 0, 0, false, null) };
        var logs = new Logs<ParliamentImportPartsJob>();
        InvalidOperationException? failure = null;

        var id = await PerformAsync<ParliamentImportPartsJob>(
            j => j.EverythingAsync(null, CancellationToken.None),
            async context => failure = await Should.ThrowAsync<InvalidOperationException>(
                () => new ParliamentImportPartsJob(import, _cards, logs).EverythingAsync(context, CancellationToken.None)));

        failure!.Message.ShouldContain("Another Seimas import is running");
        logs.Lines.ShouldBe(["Warning: Parliament import by hand (All) did not run: another import is running"]);
        (await ParameterAsync(id, ParliamentImportPartsJob.OutcomeParameter)).ShouldBe("\"skipped: 0 requests, 0 changes\"");
    }

    [Fact]
    public async Task A_manual_job_whose_import_the_service_failed_fails_with_the_reason()
    {
        var import = new FakeImport { Outcome = new ImportOutcome(Guid.NewGuid(), "failed", 4, 0, false, "The Seimas service did not answer") };
        var logs = new Logs<ParliamentImportPartsJob>();

        await PerformAsync<ParliamentImportPartsJob>(
            j => j.VotesAsync(null, CancellationToken.None),
            async context => (await Should.ThrowAsync<InvalidOperationException>(
                () => new ParliamentImportPartsJob(import, _cards, logs).VotesAsync(context, CancellationToken.None))).Message.ShouldContain("The Seimas service did not answer"));

        logs.Lines.ShouldBe(["Error: Parliament import by hand (Votes) failed: The Seimas service did not answer"]);
    }

    [Fact]
    public async Task The_scheduled_import_refreshes_the_search_copy_of_the_bills_when_it_ran_and_when_it_was_not_due()
    {
        await PerformAsync<ParliamentImportJob>(
            job => job.RunAsync(null, CancellationToken.None),
            context => new ParliamentImportJob(new FakeImport { Due = new ImportDue(true, null) }, _cards, new Logs<ParliamentImportJob>()).RunAsync(context, CancellationToken.None));
        _cards.Refreshes.ShouldBe(1, "after an import");

        await PerformAsync<ParliamentImportJob>(
            job => job.RunAsync(null, CancellationToken.None),
            context => new ParliamentImportJob(new FakeImport { Due = new ImportDue(false, DateTimeOffset.UtcNow) }, _cards, new Logs<ParliamentImportJob>()).RunAsync(context, CancellationToken.None));
        _cards.Refreshes.ShouldBe(2, "when not due, as the worker's start-up trigger finds it");
    }

    [Fact]
    public async Task A_refresh_of_the_search_copy_that_fails_is_logged_and_does_not_fail_the_import()
    {
        _cards.Fail = true;
        var logs = new Logs<ParliamentImportJob>();

        var id = await PerformAsync<ParliamentImportJob>(
            job => job.RunAsync(null, CancellationToken.None),
            context => new ParliamentImportJob(new FakeImport { Due = new ImportDue(true, null) }, _cards, logs).RunAsync(context, CancellationToken.None));

        id.ShouldNotBeNull();
        logs.Lines.ShouldBe(["Error: The search copy of the bills could not be refreshed after the import"]);
    }

    [Fact]
    public async Task A_manual_import_refreshes_the_search_copy_but_one_that_found_another_running_does_not()
    {
        var job = new ParliamentImportPartsJob(new FakeImport { Outcome = new ImportOutcome(Guid.NewGuid(), "succeeded", 1, 1, false, null) }, _cards, new Logs<ParliamentImportPartsJob>());
        await PerformAsync<ParliamentImportPartsJob>(j => j.MembersAsync(null, CancellationToken.None), context => job.MembersAsync(context, CancellationToken.None));
        _cards.Refreshes.ShouldBe(1);

        var skipped = new ParliamentImportPartsJob(new FakeImport { Outcome = new ImportOutcome(null, "skipped", 0, 0, false, null) }, _cards, new Logs<ParliamentImportPartsJob>());
        await PerformAsync<ParliamentImportPartsJob>(
            j => j.MembersAsync(null, CancellationToken.None),
            async context => await Should.ThrowAsync<InvalidOperationException>(() => skipped.MembersAsync(context, CancellationToken.None)));
        _cards.Refreshes.ShouldBe(1);
    }

    /// <summary>
    /// Stores a job as the worker would hold it (scheduled far ahead, so nothing picks it up; deleted after the test) and
    /// performs <paramref name="perform"/> with that job's context; returns its id for its parameters.
    /// </summary>
    private async Task<string> PerformAsync<T>(System.Linq.Expressions.Expression<Func<T, Task>> stored, Func<PerformContext, Task> perform)
    {
        var storage = Factory.Services.GetRequiredService<JobStorage>();
        var client = Factory.Services.GetRequiredService<IBackgroundJobClient>();
        var id = client.Create(stored, new ScheduledState(TimeSpan.FromDays(365)));
        _jobs.Add(id);
        using var connection = storage.GetConnection();
        var context = new PerformContext(storage, connection, new BackgroundJob(id, Job.FromExpression(stored), DateTime.UtcNow), new JobCancellationToken(false));
        await perform(context);
        return id;
    }

    private Task<string?> ParameterAsync(string jobId, string name) =>
        Database.ScalarAsync<string>("SELECT value FROM hangfire.jobparameter WHERE jobid = @j AND name = @n", ("j", long.Parse(jobId, System.Globalization.CultureInfo.InvariantCulture)), ("n", name));

    private sealed class FakeImport : IParliamentImport
    {
        public ImportDue Due { get; init; } = new(true, null);

        public ImportOutcome Outcome { get; init; } = new(Guid.NewGuid(), "succeeded", 0, 0, false, null);

        public List<ImportParts> Runs { get; } = [];

        public Task<ImportOutcome> RunAsync(ImportParts force, CancellationToken cancellationToken)
        {
            Runs.Add(force);
            return Task.FromResult(Outcome);
        }

        public Task<ImportDue> DueAsync(CancellationToken cancellationToken) => Task.FromResult(Due);
    }

    private sealed class FakeCards : IBillCards
    {
        public int Refreshes { get; private set; }

        public bool Fail { get; set; }

        public Task RefreshAsync(CancellationToken cancellationToken)
        {
            Refreshes++;
            return Fail ? throw new InvalidOperationException("the database is away") : Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<string>?> SearchAsync(string? query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>?>(null);
    }

    /// <summary>The job's log lines at information level and above, as "Level: message".</summary>
    private sealed class Logs<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Lines.Add($"{logLevel}: {formatter(state, exception)}");
            }
        }
    }
}
