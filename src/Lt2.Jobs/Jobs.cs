using System.Globalization;
using Hangfire;
using Hangfire.Server;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lt2.Jobs;

// The worker runs these jobs (its RecurringJobs registers them); the web host's dashboard only loads their types to trigger
// one by hand. Every job's arguments are the cancellation token and, for the import's jobs, Hangfire's PerformContext (both
// stored by Hangfire as null, and filled in when the job runs), so the stored job and the dashboard hold no person's data;
// each job's log lines and parameters hold counts and times only.

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
/// Daily housekeeping of the outbox (design 6.8): messages that gave up more than 30 days ago are deleted; and of the follow
/// job's records (design 8.24): those no pass can still need.
/// </summary>
public sealed class OutboxPurgeJob(INotificationsDispatcher dispatcher, IFollows follows, TimeProvider clock, ILogger<OutboxPurgeJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var removed = await dispatcher.PurgeFailedAsync(cancellationToken);
        logger.LogInformation("Outbox housekeeping removed {Rows} failed messages", removed);
        var events = await follows.PurgeEventsAsync(clock.GetUtcNow(), cancellationToken);
        logger.LogInformation("Follow housekeeping removed {Rows} old records", events);
    }
}

/// <summary>
/// Design 7.2: the Seimas open-data import. Asked every 15 minutes; runs when due (every 15 minutes on sitting days,
/// hourly otherwise, at once if it never succeeded). A Seimas outage fails the run, which Hangfire shows; the site keeps
/// the last good data and the next run resumes. No Hangfire lock: the import's own advisory lock makes a second run
/// return "skipped" at once, whereas waiting for a Hangfire lock would hold one of the two workers for up to an hour
/// while a long first import runs.
/// <para>
/// A run that does not import (not due, or another import running) says so (owner 2026-11-18): a log line at information
/// level and the job parameter <see cref="SkippedParameter"/>, which the dashboard shows on the job's page, so "Trigger now"
/// on this job is never silent. To import at once whatever the schedule, an administrator triggers one of
/// <see cref="ParliamentImportPartsJob"/>'s jobs.
/// </para>
/// </summary>
public sealed class ParliamentImportJob(IParliamentImport import, ILogger<ParliamentImportJob> logger)
{
    /// <summary>The job parameter that says why a run imported nothing.</summary>
    public const string SkippedParameter = "Skipped";

    [JobDisplayName("Seimas import (scheduled: runs only when due)")]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RunAsync(PerformContext? context, CancellationToken cancellationToken)
    {
        var due = await import.DueAsync(cancellationToken);
        if (!due.IsDue)
        {
            var lastStart = ParliamentImportPartsJob.Vilnius(due.LastStart);
            logger.LogInformation("Parliament import not due (last start {LastStart}); skipped", lastStart);
            context?.SetJobParameter(SkippedParameter, $"Not due (last start {lastStart}). To import now, trigger parliament-members, parliament-sittings, parliament-votes or parliament-everything.");
            return;
        }

        var outcome = await import.RunAsync(ImportParts.None, cancellationToken);
        if (outcome.Status == "skipped")
        {
            context?.SetJobParameter(SkippedParameter, "Another Seimas import is running.");
        }
        else if (outcome.Status == "failed")
        {
            // Logged as an error for the alerting in design 9.4; not retried at once (the next period retries).
            logger.LogError("Parliament import failed: {Error}", outcome.Error);
        }
    }
}

/// <summary>
/// Design 7.2 (owner 2026-11-18): the Seimas import's manual jobs. Registered as recurring jobs that never fire
/// (<c>Cron.Never()</c>), so the dashboard lists them with "Trigger now" and nothing else starts them. Each forces its part
/// of the walk (<see cref="ImportParts"/>), fetching its requests again however fresh, while the rest of the walk keeps its
/// own rules; the scheduled <see cref="ParliamentImportJob"/> is unchanged. Votes match their ballots to the MPs already
/// imported (a row matching none is skipped with a warning), so the MPs come first: <see cref="Everything"/> walks members,
/// then sittings, then votes.
/// <para>
/// A run that cannot start because another import holds the lock, or that the Seimas service fails, fails the job (an
/// error the dashboard lists under "Failed"); its outcome is the job parameter <see cref="OutcomeParameter"/> either way.
/// </para>
/// </summary>
public sealed class ParliamentImportPartsJob(IParliamentImport import, ILogger<ParliamentImportPartsJob> logger)
{
    public const string Members = "parliament-members";
    public const string Sittings = "parliament-sittings";
    public const string Votes = "parliament-votes";
    public const string Everything = "parliament-everything";

    /// <summary>The job parameter with the run's outcome: its status, requests and changes.</summary>
    public const string OutcomeParameter = "Outcome";

    /// <summary>Registers the four jobs with a schedule that never fires: only "Trigger now" on the dashboard runs them.</summary>
    public static void Register(IRecurringJobManager jobs, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var options = new RecurringJobOptions { TimeZone = timeZone };
        jobs.AddOrUpdate<ParliamentImportPartsJob>(Members, job => job.MembersAsync(null, CancellationToken.None), Cron.Never(), options);
        jobs.AddOrUpdate<ParliamentImportPartsJob>(Sittings, job => job.SittingsAsync(null, CancellationToken.None), Cron.Never(), options);
        jobs.AddOrUpdate<ParliamentImportPartsJob>(Votes, job => job.VotesAsync(null, CancellationToken.None), Cron.Never(), options);
        jobs.AddOrUpdate<ParliamentImportPartsJob>(Everything, job => job.EverythingAsync(null, CancellationToken.None), Cron.Never(), options);
    }

    [JobDisplayName("Seimas import by hand: MPs (terms, MP list with memberships and contacts, sessions)")]
    [AutomaticRetry(Attempts = 0)]
    public Task MembersAsync(PerformContext? context, CancellationToken cancellationToken) => RunAsync(ImportParts.Members, context, cancellationToken);

    [JobDisplayName("Seimas import by hand: sittings (every sitting list, agenda and proceedings)")]
    [AutomaticRetry(Attempts = 0)]
    public Task SittingsAsync(PerformContext? context, CancellationToken cancellationToken) => RunAsync(ImportParts.Sittings, context, cancellationToken);

    [JobDisplayName("Seimas import by hand: votes (every per-MP result; run after the MPs are imported)")]
    [AutomaticRetry(Attempts = 0)]
    public Task VotesAsync(PerformContext? context, CancellationToken cancellationToken) => RunAsync(ImportParts.Votes, context, cancellationToken);

    [JobDisplayName("Seimas import by hand: everything (MPs, then sittings, then votes)")]
    [AutomaticRetry(Attempts = 0)]
    public Task EverythingAsync(PerformContext? context, CancellationToken cancellationToken) => RunAsync(ImportParts.All, context, cancellationToken);

    /// <summary>An instant as Vilnius time, <c>yyyy-MM-dd HH:mm</c>, or "never".</summary>
    internal static string Vilnius(DateTimeOffset? instant) => instant is { } at
        ? TimeZoneInfo.ConvertTime(at, Lt2.SharedKernel.VilniusTime.Zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : "never";

    private async Task RunAsync(ImportParts force, PerformContext? context, CancellationToken cancellationToken)
    {
        var outcome = await import.RunAsync(force, cancellationToken);
        context?.SetJobParameter(OutcomeParameter, $"{outcome.Status}: {outcome.Requests} requests, {outcome.Changes} changes");
        switch (outcome.Status)
        {
            case "skipped":
                logger.LogWarning("Parliament import by hand ({Force}) did not run: another import is running", force);
                throw new InvalidOperationException("Another Seimas import is running, so this one did not run. Trigger it again when that one has finished.");
            case "failed":
                // An error for the alerting in design 9.4, as the scheduled run's; what was fetched is kept.
                logger.LogError("Parliament import by hand ({Force}) failed: {Error}", force, outcome.Error);
                throw new InvalidOperationException($"The Seimas import failed ({outcome.Error}). What was fetched is kept; trigger it again later.");
            default:
                logger.LogInformation(
                    "Parliament import by hand ({Force}) {Status}: {Requests} requests, {Changes} changes", force, outcome.Status, outcome.Requests, outcome.Changes);
                break;
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
/// Summaries (design 8.4, 8.19; owner 2026-11-17): every 15 minutes, prepares the weekly or before-vote summary once its
/// preparation time (<c>Digest:ReviewHours</c> before it goes) has come, telling the moderators, and lets go every prepared one
/// whose time has come: a dry run while sending is off, sent with <c>automatic</c>. The log line holds counts only, never an MP
/// or an address.
/// </summary>
public sealed class DigestJob(IDigests digests, TimeProvider clock, ILogger<DigestJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var made = await digests.RunDueAsync(clock.GetUtcNow(), cancellationToken);
        foreach (var run in made)
        {
            logger.LogInformation("Summary {Kind} {Status}: {Items} bills, {Mps} MPs", run.Kind, run.Status, run.Items.Count, run.MpCount);
        }
    }
}

/// <summary>
/// Design 8.10: once a day, a notice of new feedback to the team address (<c>Feedback:TeamAddress</c>): the count and the
/// staff page, never the text; nothing when nothing is new or no address is set.
/// </summary>
public sealed class FeedbackNoticeJob(IFeedback feedback, IConfiguration configuration, ILogger<FeedbackNoticeJob> logger)
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
