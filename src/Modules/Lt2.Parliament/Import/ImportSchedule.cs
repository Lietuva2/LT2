namespace Lt2.Parliament.Import;

/// <summary>Design 7.2: when the recurring import should run. The worker asks every 15 minutes.</summary>
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
}
