using Hangfire;
using Lt2.Identity;
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

/// <summary>Daily housekeeping: deletes old sign-in codes and expired sessions.</summary>
public sealed class IdentityPurgeJob(IIdentityMaintenance maintenance, ILogger<IdentityPurgeJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var removed = await maintenance.PurgeExpiredAsync(cancellationToken);
        logger.LogInformation("Identity housekeeping removed {Rows} rows", removed);
    }
}

/// <summary>Registers the recurring Hangfire jobs at start-up, in Vilnius time (design 3: <c>Europe/Vilnius</c> for schedules).</summary>
internal sealed class RecurringJobs(IRecurringJobManager jobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");
        jobs.AddOrUpdate<IdentityPurgeJob>(
            "identity-purge",
            job => job.RunAsync(CancellationToken.None),
            "15 3 * * *",
            new RecurringJobOptions { TimeZone = vilnius });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
