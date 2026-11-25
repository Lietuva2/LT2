namespace Lt2.Parliament.Import;

/// <summary>Design 7.2: when the recurring import should run (the worker asks every 15 minutes), when a missing vote result is asked for again, and when the TAIS projects are.</summary>
internal static class ImportSchedule
{
    /// <summary>A little slack, so a run that started a minute late does not skip a whole period.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Due when no run ever succeeded; on a sitting day every 15 minutes; otherwise hourly. A failed run does not
    /// reset the clock: the next period retries.
    /// </summary>
    public static bool IsDue(DateTimeOffset now, DateTimeOffset? lastSuccessfulStart, DateTimeOffset? lastStart, bool sittingToday)
    {
        if (lastSuccessfulStart is null)
        {
            return lastStart is null || now - lastStart.Value >= TimeSpan.FromMinutes(15) - Slack;
        }

        var period = sittingToday ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1);
        return now - (lastStart ?? lastSuccessfulStart.Value) >= period - Slack;
    }

    /// <summary>
    /// The per-MP result of a recorded vote that open data did not have yet, asked for again after the refetch window:
    /// daily while the vote is less than 60 days old, weekly after that.
    /// </summary>
    public static bool IsMissingResultDue(DateTimeOffset now, DateTimeOffset heldAt, DateTimeOffset lastFetchedAt)
    {
        var interval = now - heldAt < TimeSpan.FromDays(60) ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
        return now - lastFetchedAt >= interval - Slack;
    }

    /// <summary>
    /// The day's TAIS projects (<c>p2b.tais_docs</c>, design 7.2) are asked for at most once an hour: due when the last
    /// answer is an hour old, less the slack, so the hourly run on a day without sittings does not skip every other hour.
    /// </summary>
    public static bool IsProjectsDue(DateTimeOffset now, DateTimeOffset lastFetchedAt) => now - lastFetchedAt >= TimeSpan.FromHours(1) - Slack;

    /// <summary>Whether a request of <paramref name="part"/> is fetched whatever its freshness: its part is forced (owner 2026-11-18).</summary>
    public static bool IsForced(ImportParts force, ImportParts part) => (force & part) != 0;

    /// <summary>The forced parts in the walk's order, for the run's record and the log: "members, sittings, votes", or "none".</summary>
    public static string Describe(ImportParts force)
    {
        var names = new List<string>(3);
        if (IsForced(force, ImportParts.Members))
        {
            names.Add("members");
        }

        if (IsForced(force, ImportParts.Sittings))
        {
            names.Add("sittings");
        }

        if (IsForced(force, ImportParts.Votes))
        {
            names.Add("votes");
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>
    /// The run's <c>import_run.source</c>: the address imported from, followed by the forced parts when an administrator forced
    /// any ("https://apps.lrs.lt/sip/ forced: members, votes"); the scheduled run's is the address alone.
    /// </summary>
    public static string RunSource(string address, ImportParts force) =>
        force == ImportParts.None ? address : $"{address} forced: {Describe(force)}";
}
