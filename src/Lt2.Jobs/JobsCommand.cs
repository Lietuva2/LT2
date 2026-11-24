using System.Globalization;
using Hangfire;
using Hangfire.Storage;
using Lt2.SharedKernel;

namespace Lt2.Jobs;

/// <summary>
/// Design 6.3: the recurring jobs from the command line of the worker's container, for whoever can already run commands
/// there (and so reach the database and its secrets): <c>dotnet Lt2.Worker.dll jobs list</c> lists them as the dashboard
/// does, <c>jobs trigger {id}</c> is the dashboard's "Trigger now" (the job is enqueued for the worker's Hangfire server).
/// No web address serves it, so the dashboard keeps its rule (an administrator in a staff-passkey session); a trigger is
/// audited as <see cref="AuditAction"/> by the system, with the job's id as the target and no details.
/// </summary>
public static class JobsCommand
{
    public const string Name = "jobs";

    /// <summary>The audit entry of a trigger from the command line.</summary>
    public const string AuditAction = "jobs.command-trigger";

    /// <summary>Exit code: done.</summary>
    public const int Done = 0;

    /// <summary>Exit code: no recurring job has that id.</summary>
    public const int UnknownJob = 1;

    /// <summary>Exit code: the arguments were not understood.</summary>
    public const int Usage = 2;

    /// <summary>Whether the arguments ask for this command: its name first.</summary>
    public static bool Matches(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Count > 0 && string.Equals(args[0], Name, StringComparison.Ordinal);
    }

    public static async Task<int> RunAsync(IReadOnlyList<string> args, JobStorage storage, IAuditLog audit, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(output);

        switch (args.Skip(1).ToArray())
        {
            case ["list"]:
                foreach (var line in List(storage))
                {
                    await output.WriteLineAsync(line);
                }

                return Done;
            case ["trigger", var id]:
                var known = RecurringJobs(storage).Select(j => j.Id).ToList();
                if (!known.Contains(id, StringComparer.Ordinal))
                {
                    await output.WriteLineAsync($"No recurring job \"{id}\". Known: {string.Join(", ", known)}.");
                    return UnknownJob;
                }

                var jobId = new RecurringJobManager(storage).TriggerJob(id);
                await audit.WriteAsync(new AuditEntry(AuditAction, null, "system", id, null), transaction: null, cancellationToken);
                await output.WriteLineAsync($"Triggered {id}: background job {jobId}, enqueued for the worker.");
                return Done;
            default:
                await output.WriteLineAsync("Usage: jobs list | jobs trigger <id>");
                return Usage;
        }
    }

    /// <summary>One line per recurring job, by id, tab-separated: id, schedule ("by hand" for one that never fires), the
    /// next run, the last run and its state, and the error when there is one; times in Vilnius, <c>yyyy-MM-dd HH:mm</c>.</summary>
    public static IEnumerable<string> List(JobStorage storage) =>
        RecurringJobs(storage).Select(j => string.Join('\t',
            j.Id,
            j.Cron == Cron.Never() ? "by hand" : j.Cron,
            "next " + Time(j.NextExecution),
            "last " + Time(j.LastExecution) + (j.LastJobState is { Length: > 0 } state ? " " + state : ""),
            j.Error is { Length: > 0 } error ? "error: " + error : "").TrimEnd('\t'));

    private static List<RecurringJobDto> RecurringJobs(JobStorage storage)
    {
        using var connection = storage.GetConnection();
        return connection.GetRecurringJobs().OrderBy(j => j.Id, StringComparer.Ordinal).ToList();
    }

    private static string Time(DateTime? utc) =>
        utc is { } at
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), VilniusTime.Zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "-";
}
