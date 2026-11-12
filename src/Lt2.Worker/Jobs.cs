using Hangfire;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Microsoft.Extensions.Options;

namespace Lt2.Worker;

internal sealed class OutboxLoopOptions
{
    public const string Section = "Outbox";

    /// <summary>How long to wait when the outbox is empty. Sign-in codes should arrive within seconds.</summary>
    public double PollSeconds { get; set; } = 1;
}

/// <summary>
/// Sends outbox messages continuously. A loop rather than a Hangfire job: Hangfire's smallest schedule is a minute,
/// too slow for sign-in codes. Several worker instances are safe because rows are taken with SKIP LOCKED.
/// </summary>
internal sealed class OutboxDispatchLoop(
    INotificationsDispatcher dispatcher,
    IOptions<OutboxLoopOptions> options,
    ILogger<OutboxDispatchLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idle = TimeSpan.FromSeconds(options.Value.PollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var handled = await dispatcher.DispatchDueAsync(stoppingToken);
                if (handled == 0)
                {
                    await Task.Delay(idle, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The loop must survive a lost database connection; it retries.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                logger.LogError(ex, "Outbox dispatch failed; retrying shortly");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

/// <summary>Daily housekeeping: deletes old sign-in codes and expired sessions.</summary>
public sealed class IdentityPurgeJob(IIdentityMaintenance maintenance, ILogger<IdentityPurgeJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var removed = await maintenance.PurgeExpiredAsync(cancellationToken);
        logger.LogInformation("Identity housekeeping removed {Rows} rows", removed);
    }
}

/// <summary>
/// Design 7.2: the Seimas open-data import. Asked every 15 minutes; runs when due (every 15 minutes on sitting days,
/// hourly otherwise, at once if it never succeeded). A Seimas outage fails the run, which Hangfire shows; the site keeps
/// the last good data and the next run resumes.
/// </summary>
public sealed class ParliamentImportJob(IParliamentImport import, ILogger<ParliamentImportJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!await import.IsDueAsync(cancellationToken))
        {
            return;
        }

        var outcome = await import.RunAsync(cancellationToken);
        if (outcome.Status == "failed")
        {
            // Logged as an error for the alerting in design 9.4; not retried at once (the next period retries).
            logger.LogError("Parliament import failed: {Error}", outcome.Error);
        }
    }
}

/// <summary>
/// The daily aggregate of appeals (design 5.3): yesterday's row once the day is over, today's so far. Hourly, so a missed
/// run catches up; a day's row is overwritten by the next run for that day.
/// </summary>
public sealed class AppealsDailyJob(IAppealsMaintenance maintenance, TimeProvider clock, ILogger<AppealsDailyJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius")).DateTime);
        var appeals = await maintenance.AggregateDayAsync(today.AddDays(-1), cancellationToken);
        await maintenance.AggregateDayAsync(today, cancellationToken);
        var closed = await maintenance.CloseFinishedAsync(cancellationToken);
        logger.LogInformation("Daily appeal aggregates written for {Appeals} appeals; {Closed} closed after the final vote", appeals, closed);
    }
}

/// <summary>
/// The agenda watcher (design 8.4): reads the upcoming plenary agenda hourly when <c>Agenda:BaseAddress</c> is set. A page
/// it cannot read is an error line (the alert of design 8.9) and changes nothing.
/// </summary>
public sealed class AgendaWatchJob(IAgendaWatch watch, ILogger<AgendaWatchJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var outcome = await watch.RunAsync(cancellationToken);
        if (outcome.Status is "refused" or "failed")
        {
            logger.LogError("Agenda watcher {Status}: {Problem}", outcome.Status, outcome.Problem);
        }
    }
}

/// <summary>Registers the recurring Hangfire jobs at start-up, in Vilnius time (design 3: <c>Europe/Vilnius</c> for schedules).</summary>
internal sealed class RecurringJobs(IRecurringJobManager jobs) : IHostedService
{
    public const string ParliamentImport = "parliament-import";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");
        jobs.AddOrUpdate<IdentityPurgeJob>(
            "identity-purge",
            job => job.RunAsync(CancellationToken.None),
            "15 3 * * *",
            new RecurringJobOptions { TimeZone = vilnius });
        jobs.AddOrUpdate<ParliamentImportJob>(
            ParliamentImport,
            job => job.RunAsync(CancellationToken.None),
            "*/15 * * * *",
            new RecurringJobOptions { TimeZone = vilnius });

        jobs.AddOrUpdate<AppealsDailyJob>(
            "appeals-daily",
            job => job.RunAsync(CancellationToken.None),
            "5 * * * *",
            new RecurringJobOptions { TimeZone = vilnius });

        jobs.AddOrUpdate<AgendaWatchJob>(
            "agenda-watch",
            job => job.RunAsync(CancellationToken.None),
            "20 * * * *",
            new RecurringJobOptions { TimeZone = vilnius });
        jobs.Trigger("agenda-watch");

        // Ask once at start-up, so a fresh deployment (or the local stack) imports without waiting for the first period.
        jobs.Trigger(ParliamentImport);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
