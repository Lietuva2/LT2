using Lt2.Parliament.Import;

namespace Lt2.Parliament.ImportTests.Import;

/// <summary>Design 7.2: when the import runs, and when a vote result that was missing is asked for again.</summary>
public sealed class ImportScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(10, 23, false)]
    [InlineData(10, 24, true)]
    [InlineData(90, 24, false)]
    [InlineData(90, 24 * 7, true)]
    public void A_missing_result_is_asked_for_daily_while_the_vote_is_recent_and_weekly_after_60_days(int voteAgeDays, int hoursSinceAsked, bool due) =>
        ImportSchedule.IsMissingResultDue(Now, Now.AddDays(-voteAgeDays), Now.AddHours(-hoursSinceAsked)).ShouldBe(due);
}