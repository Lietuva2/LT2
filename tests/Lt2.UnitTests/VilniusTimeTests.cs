using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary><c>AGENTS.md</c>: times are UTC in the database; days, schedules and display are Europe/Vilnius.</summary>
public sealed class VilniusTimeTests
{
    [Fact]
    public void A_day_starts_at_midnight_in_Vilnius()
    {
        VilniusTime.StartOf(new DateOnly(2026, 11, 15)).ShouldBe(new DateTimeOffset(2026, 11, 14, 22, 0, 0, TimeSpan.Zero), "winter time is UTC+2");
        VilniusTime.StartOf(new DateOnly(2026, 7, 1)).ShouldBe(new DateTimeOffset(2026, 6, 30, 21, 0, 0, TimeSpan.Zero), "summer time is UTC+3");
    }

    [Fact]
    public void The_day_of_an_instant_is_the_day_in_Vilnius()
    {
        VilniusTime.DateOf(new DateTimeOffset(2026, 11, 14, 21, 30, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 14));
        VilniusTime.DateOf(new DateTimeOffset(2026, 11, 14, 22, 30, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 15));
    }

    [Fact]
    public void Today_is_read_from_the_clock() =>
        VilniusTime.Today(new FixedClock(new DateTimeOffset(2026, 11, 14, 22, 30, 0, TimeSpan.Zero))).ShouldBe(new DateOnly(2026, 11, 15));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
