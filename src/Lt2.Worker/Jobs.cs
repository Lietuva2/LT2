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

/// <summary>Daily housekeeping of the outbox (design 6.8): messages that gave up more than 30 days ago are deleted.</summary>
public sealed class OutboxPurgeJob(INotificationsDispatcher dispatcher, ILogger<OutboxPurgeJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var removed = await dispatcher.PurgeFailedAsync(cancellationToken);
        logger.LogInformation("Outbox housekeeping removed {Rows} failed messages", removed);
    }
}

/// <summary>
/// Design 7.2: the Seimas open-data import. Asked every 15 minutes; runs when due (every 15 minutes on sitting days,
/// hourly otherwise, at once if it never succeeded). A Seimas outage fails the run, which Hangfire shows; the site keeps
/// the last good data and the next run resumes. No Hangfire lock: the import's own advisory lock makes a second run
/// return "skipped" at once, whereas waiting for a Hangfire lock would hold one of the two workers for up to an hour
/// while a long first import runs.
/// </summary>
public sealed class ParliamentImportJob(IParliamentImport import, ILogger<ParliamentImportJob> logger)
{
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
/// The daily aggregate of the bills' records (design 5.3): yesterday's row once the day is over, today's so far. Hourly, so a missed
/// run catches up; a day's row is overwritten by the next run for that day.
/// </summary>
public sealed class AppealsDailyJob(IAppealsMaintenance maintenance, TimeProvider clock, ILogger<AppealsDailyJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var today = Lt2.SharedKernel.VilniusTime.Today(clock);
        var records = await maintenance.AggregateDayAsync(today.AddDays(-1), cancellationToken);
        await maintenance.AggregateDayAsync(today, cancellationToken);
        var closed = await maintenance.CloseFinishedAsync(cancellationToken);
        logger.LogInformation("Daily aggregates written for {Records} records; {Closed} closed after the final vote", records, closed);
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

/// <summary>
/// Summaries (design 8.4, 8.19): every 15 minutes, expires the waiting summaries whose time passed and makes the weekly or
/// before-vote summary when it is due: a dry run while sending is off, waiting for a moderator with <c>approval</c>, sent with
/// <c>automatic</c>. The log line holds counts only, never an MP or an address.
/// </summary>
public sealed class DigestJob(IDigests digests, TimeProvider clock, ILogger<DigestJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var made = await digests.RunDueAsync(clock.GetUtcNow(), cancellationToken);
        foreach (var run in made)
        {
            logger.LogInformation("Summary {Kind} made ({Status}): {Items} bills, {Mps} MPs", run.Kind, run.Status, run.Items.Count, run.MpCount);
        }
    }
}

/// <summary>
/// Design 8.10: once a day, a notice of new feedback to the team address (<c>Feedback:TeamAddress</c>): the count and the
/// staff page, never the text; nothing when nothing is new or no address is set.
/// </summary>
public sealed class FeedbackNoticeJob(Lt2.Identity.IFeedback feedback, IConfiguration configuration, ILogger<FeedbackNoticeJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var count = await feedback.SendNoticeAsync(configuration["Feedback:TeamAddress"], cancellationToken);
        logger.LogInformation("Feedback notice: {Count} new items", count);
    }
}

/// <summary>
/// Following (design 8.11.2): records new planned dates and stage votes of followed bills and writes the follow emails to
/// the outbox. The log line holds counts only.
/// </summary>
public sealed class FollowJob(IFollows follows, TimeProvider clock)
{
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    public Task RunAsync(CancellationToken cancellationToken) => follows.RunAsync(clock.GetUtcNow(), cancellationToken);
}

/// <summary>
/// The summary job (design 8.13): AI summaries for active bills without a description, at most <c>Ai:SummariesPerDay</c> a
/// Vilnius day. Hourly, so bills new in the catalogue are taken the same day; asked once at start-up too. While no official
/// text can be read, no provider is configured or the setting is 0, it makes nothing and says why once, not on every run.
/// </summary>
public sealed class DescriptionSummaryJob(IDescriptionSummaries summaries)
{
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task RunAsync(CancellationToken cancellationToken) => summaries.RunAsync(cancellationToken);
}

/// <summary>Registers the recurring Hangfire jobs at start-up, in Vilnius time (design 3: <c>Europe/Vilnius</c> for schedules).</summary>
internal sealed class RecurringJobs(IRecurringJobManager jobs, IConfiguration configuration) : IHostedService
{
    public const string ParliamentImport = "parliament-import";
    public const string DescriptionSummaries = "description-summaries";

    /// <summary>The follow job's schedule unless the configuration sets <c>Jobs:FollowsCron</c>.</summary>
    public const string FollowsCron = "7,22,37,52 * * * *";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var vilnius = Lt2.SharedKernel.VilniusTime.Zone;
        jobs.AddOrUpdate<IdentityPurgeJob>(
            "identity-purge",
            job => job.RunAsync(CancellationToken.None),
            "15 3 * * *",
            new RecurringJobOptions { TimeZone = vilnius });
        jobs.AddOrUpdate<OutboxPurgeJob>(
            "outbox-purge",
            job => job.RunAsync(CancellationToken.None),
            "25 3 * * *",
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
        jobs.AddOrUpdate<FeedbackNoticeJob>(
            "feedback-notice",
            job => job.RunAsync(CancellationToken.None),
            "0 8 * * *",
            new RecurringJobOptions { TimeZone = vilnius });
        jobs.AddOrUpdate<DigestJob>(
            "digests",
            job => job.RunAsync(CancellationToken.None),
            "*/15 * * * *",
            new RecurringJobOptions { TimeZone = vilnius });

        // Design 8.11.2: every 15 minutes, a few minutes after the import (:00, :15 …) and the agenda watcher (:20). The local
        // stack may set Jobs:FollowsCron (deploy/.env: every minute), so the end-to-end follow email arrives without a wait.
        jobs.AddOrUpdate<FollowJob>(
            "follows",
            job => job.RunAsync(CancellationToken.None),
            configuration["Jobs:FollowsCron"] is { Length: > 0 } followsCron ? followsCron : FollowsCron,
            new RecurringJobOptions { TimeZone = vilnius });

        // Design 8.13: hourly at :40, after the import and the agenda watcher; once at start-up too.
        jobs.AddOrUpdate<DescriptionSummaryJob>(
            DescriptionSummaries,
            job => job.RunAsync(CancellationToken.None),
            "40 * * * *",
            new RecurringJobOptions { TimeZone = vilnius });
        jobs.Trigger(DescriptionSummaries);

        // Ask once at start-up, so a fresh deployment (or the local stack) imports without waiting for the first period.
        jobs.Trigger(ParliamentImport);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
