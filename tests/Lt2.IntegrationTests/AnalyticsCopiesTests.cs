using Lt2.Api.Contracts.Analytics;
using Lt2.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.26.6: the kept public copy of the analytics. Made once however many ask; an old copy is served as it is while
/// one refresh runs in the background; a member's copy is theirs until the data version changes or it is dropped.
/// </summary>
public sealed class AnalyticsCopiesTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static AnalyticsResponse Answer(int n) =>
        new(n.ToString(System.Globalization.CultureInfo.InvariantCulture), [], null, false, 20, DateTimeOffset.UnixEpoch,
            new AnalyticsFiguresResponse(0, 0, 0, 0, 0, null, 0, 0), new AnalyticsBillQuery(), 0, 0, [], []);

    private static AnalyticsCopies Copies(Clock clock) =>
        new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<AnalyticsCopies>.Instance);

    [Fact]
    public async Task An_old_copy_is_served_while_one_refresh_runs_in_the_background_and_then_the_new_one()
    {
        var clock = new Clock();
        using var copies = Copies(clock);
        var made = 0;
        var refreshMayFinish = new TaskCompletionSource();
        Task<AnalyticsResponse> Make(IServiceProvider _) => Interlocked.Increment(ref made) is var n && n == 2
            ? refreshMayFinish.Task.ContinueWith(_ => Answer(2), TaskScheduler.Default)
            : Task.FromResult(Answer(n));

        (await copies.PublicAsync("term", Make)).TermName.ShouldBe("1");
        (await copies.PublicAsync("term", Make)).TermName.ShouldBe("1", "kept");
        made.ShouldBe(1);

        clock.Now += AnalyticsCopies.PublicLifetime;
        (await copies.PublicAsync("term", Make)).TermName.ShouldBe("1", "the old copy while the refresh runs");
        var started = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref made) < 2 && DateTime.UtcNow < started)
        {
            await Task.Yield();
        }

        (await copies.PublicAsync("term", Make)).TermName.ShouldBe("1");
        (await copies.PublicAsync("term", Make)).TermName.ShouldBe("1");
        made.ShouldBe(2, "one refresh, however many ask");

        refreshMayFinish.SetResult();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await copies.PublicAsync("term", Make)).TermName != "2" && DateTime.UtcNow < deadline)
        {
            await Task.Yield();
        }

        (await copies.PublicAsync("term", Make)).TermName.ShouldBe("2");
        made.ShouldBe(2);
    }

    [Fact]
    public async Task A_members_copy_is_made_again_when_the_data_version_changes_and_dropped_when_forgotten()
    {
        using var copies = Copies(new Clock());
        var member = Guid.CreateVersion7();
        var made = 0;
        Task<MyAgreementResponse> Make() => Task.FromResult(new MyAgreementResponse(Interlocked.Increment(ref made), 0, 20, [], []));

        (await copies.PersonalAsync(member, "v1", Make)).Bills.ShouldBe(1);
        (await copies.PersonalAsync(member, "v1", Make)).Bills.ShouldBe(1, "kept for the member");
        (await copies.PersonalAsync(Guid.CreateVersion7(), "v1", Make)).Bills.ShouldBe(2, "another member's is their own");
        (await copies.PersonalAsync(member, "v2", Make)).Bills.ShouldBe(3, "the data changed");
        copies.Forget(member);
        (await copies.PersonalAsync(member, "v2", Make)).Bills.ShouldBe(4, "dropped at sign-out, deletion, consent withdrawal");
    }
}
