using Lt2.Parliament.Import;

namespace Lt2.Parliament.ImportTests.Import;

/// <summary>Design 7.2: when the import runs, when a vote result that was missing is asked for again, and when the TAIS projects are.</summary>
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

    [Theory]
    [InlineData(0, false)]
    [InlineData(57, false)]
    [InlineData(58, true)]
    [InlineData(60, true)]
    [InlineData(24 * 60, true)]
    public void The_tais_projects_are_asked_for_at_most_hourly_with_the_runs_slack(int minutesSinceAsked, bool due) =>
        ImportSchedule.IsProjectsDue(Now, Now.AddMinutes(-minutesSinceAsked)).ShouldBe(due);

    [Theory]
    [InlineData(ImportParts.None, ImportParts.Members, false)]
    [InlineData(ImportParts.Members, ImportParts.Members, true)]
    [InlineData(ImportParts.Members, ImportParts.Votes, false)]
    [InlineData(ImportParts.Sittings | ImportParts.Votes, ImportParts.Votes, true)]
    [InlineData(ImportParts.All, ImportParts.Sittings, true)]
    [InlineData(ImportParts.All, ImportParts.None, false)]
    public void A_request_is_forced_only_when_its_part_is(ImportParts force, ImportParts part, bool forced) =>
        ImportSchedule.IsForced(force, part).ShouldBe(forced);

    [Theory]
    [InlineData(ImportParts.None, "https://apps.lrs.lt/sip/")]
    [InlineData(ImportParts.Votes, "https://apps.lrs.lt/sip/ forced: votes")]
    [InlineData(ImportParts.Votes | ImportParts.Members, "https://apps.lrs.lt/sip/ forced: members, votes")]
    [InlineData(ImportParts.All, "https://apps.lrs.lt/sip/ forced: members, sittings, votes")]
    public void A_run_records_its_forced_parts_after_the_address_in_the_walks_order(ImportParts force, string source) =>
        ImportSchedule.RunSource("https://apps.lrs.lt/sip/", force).ShouldBe(source);
}