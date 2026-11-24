using System.Text.RegularExpressions;
using Hangfire;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Jobs;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.3: the worker's <c>jobs</c> command lists the recurring jobs and triggers one as the dashboard's "Trigger now"
/// does, enqueued for the worker and audited by the system with the job's id. It runs here against the same Hangfire
/// storage and audit log, through the web host's services; each test registers a job id of its own.
/// </summary>
public sealed partial class JobsCommandTests(DatabaseFixture database) : ApiTest(database)
{
    [GeneratedRegex(@"background job (\d+)")]
    private static partial Regex BackgroundJob();

    [Fact]
    public async Task The_list_shows_each_job_with_its_schedule_and_a_job_never_scheduled_as_by_hand()
    {
        var (jobs, manual) = Register();
        var scheduled = "jobs-command-scheduled-" + Guid.NewGuid().ToString("N");
        jobs.AddOrUpdate<ParliamentImportJob>(scheduled, job => job.RunAsync(null, CancellationToken.None), "*/15 * * * *");
        try
        {
            var (code, output) = await RunAsync("jobs", "list");

            code.ShouldBe(JobsCommand.Done);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            lines.Single(l => l.StartsWith(manual + "\t", StringComparison.Ordinal)).ShouldStartWith(manual + "\tby hand\tnext -\tlast -");
            lines.Single(l => l.StartsWith(scheduled + "\t", StringComparison.Ordinal)).ShouldMatch(@"\t\*/15 \* \* \* \*\tnext \d{4}-\d{2}-\d{2} \d{2}:\d{2}\tlast -$");
        }
        finally
        {
            jobs.RemoveIfExists(manual);
            jobs.RemoveIfExists(scheduled);
        }
    }

    [Fact]
    public async Task A_trigger_enqueues_the_job_for_the_worker_and_is_audited_by_the_system_with_the_job_id()
    {
        var (jobs, id) = Register();
        try
        {
            var (code, output) = await RunAsync("jobs", "trigger", id);

            code.ShouldBe(JobsCommand.Done, output);
            var jobId = BackgroundJob().Match(output).Groups[1].Value;
            (await Database.ScalarAsync<string>("SELECT statename FROM hangfire.job WHERE id = @id", ("id", long.Parse(jobId, System.Globalization.CultureInfo.InvariantCulture)))).ShouldBe("Enqueued");
            (await Database.ScalarAsync<string>(
                "SELECT concat_ws('|', coalesce(actor_user_id::text, '-'), actor_role, coalesce(details::text, '-'), (ip_hash IS NULL)::text) FROM audit.event WHERE action = @a AND target = @t",
                ("a", JobsCommand.AuditAction), ("t", id))).ShouldBe("-|system|-|true");
        }
        finally
        {
            jobs.RemoveIfExists(id);
        }
    }

    [Fact]
    public async Task An_unknown_job_or_unclear_arguments_change_nothing()
    {
        var unknown = "jobs-command-unknown-" + Guid.NewGuid().ToString("N");
        var audited = await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = @a", ("a", JobsCommand.AuditAction));

        var (code, output) = await RunAsync("jobs", "trigger", unknown);
        code.ShouldBe(JobsCommand.UnknownJob);
        output.ShouldContain("No recurring job \"" + unknown + "\"");

        foreach (var args in new[] { new[] { "jobs" }, ["jobs", "trigger"], ["jobs", "run", "x"], ["jobs", "trigger", "a", "b"] })
        {
            (await RunAsync(args)).Code.ShouldBe(JobsCommand.Usage, string.Join(' ', args));
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = @a", ("a", JobsCommand.AuditAction))).ShouldBe(audited);
    }

    [Fact]
    public void Only_arguments_naming_the_command_first_run_it()
    {
        JobsCommand.Matches(["jobs", "list"]).ShouldBeTrue();
        JobsCommand.Matches([]).ShouldBeFalse();
        JobsCommand.Matches(["--urls", "jobs"]).ShouldBeFalse();
        JobsCommand.Matches(["Jobs"]).ShouldBeFalse();
    }

    /// <summary>A job of this test's own that never fires, as the import's manual jobs are registered.</summary>
    private (IRecurringJobManager Jobs, string Id) Register()
    {
        var jobs = Factory.Services.GetRequiredService<IRecurringJobManager>();
        var id = "jobs-command-" + Guid.NewGuid().ToString("N");
        jobs.AddOrUpdate<ParliamentImportPartsJob>(id, job => job.MembersAsync(null, CancellationToken.None), Cron.Never());
        return (jobs, id);
    }

    private async Task<(int Code, string Output)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        var code = await JobsCommand.RunAsync(
            args,
            Factory.Services.GetRequiredService<JobStorage>(),
            Factory.Services.GetRequiredService<IAuditLog>(),
            output,
            TestContext.Current.CancellationToken);
        return (code, output.ToString());
    }
}
