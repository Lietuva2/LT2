using Hangfire;
using Lt2.Jobs;
using Lt2.Notifications;
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
