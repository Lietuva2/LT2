namespace Lt2.Appeals.Domain;

/// <summary>
/// Surge detection (design 6.6): when a large share of the recent positions on a bill come from accounts created in the
/// same recent days (default: at least 30 % of the last 72 hours' positions and at least 50 accounts; all settings),
/// the appeal page and the summary say so in neutral words and a moderator is asked whether to hold the send.
/// </summary>
internal static class SurgeRules
{
    /// <param name="recentPositions">Positions taken within the window.</param>
    /// <param name="fromNewAccounts">Of those, taken by accounts that became members within the same window.</param>
    public static bool IsSurge(int recentPositions, int fromNewAccounts, decimal minimumShare, int minimumAccounts) =>
        recentPositions > 0
        && fromNewAccounts >= minimumAccounts
        && (decimal)fromNewAccounts / recentPositions >= minimumShare;

    /// <summary>The share shown in the note: whole per cent, rounded down so the note never overstates.</summary>
    public static int SharePercent(int recentPositions, int fromNewAccounts) =>
        recentPositions == 0 ? 0 : (int)Math.Floor(100m * fromNewAccounts / recentPositions);

    /// <summary>The share of a day's new positions taken by accounts younger than 30 days (daily_stat.new_account_share).</summary>
    public static decimal NewAccountShare(int newPositions, int fromYoungAccounts) =>
        newPositions == 0 ? 0 : Math.Round((decimal)fromYoungAccounts / newPositions, 4, MidpointRounding.ToZero);
}

/// <summary>When the daily aggregate (design 5.3 <c>daily_stat</c>) writes a day's row.</summary>
internal static class DailyStatRules
{
    /// <summary>
    /// A day still running is rewritten on every run ("today so far"). A finished day is written once, by the first run
    /// after it ended, and then left alone: positions keep changing after the day, and counting those changes into it would
    /// make its trend wrong. A row without the time it was computed (written before that time was kept) is rewritten.
    /// </summary>
    /// <param name="dayEnd">The instant the day ends (the next Vilnius midnight).</param>
    /// <param name="computedAt">When the day's row was computed, or null when it has none.</param>
    public static bool ShouldWrite(DateTimeOffset dayEnd, DateTimeOffset now, DateTimeOffset? computedAt) =>
        now < dayEnd || computedAt is null || computedAt < dayEnd;
}
