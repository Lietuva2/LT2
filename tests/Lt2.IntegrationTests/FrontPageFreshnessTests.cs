using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.7, "Cached" (owner, 2026-11-15): a side taken, changed or withdrawn marks the front page's copy of the agenda
/// out of date, and the next request reads it again, but a copy is never replaced within two seconds of being made; any
/// other change waits for the minute. The two seconds are counted by a test clock, not by waiting.
/// </summary>
public sealed class FrontPageFreshnessTests(DatabaseFixture database) : IAsyncDisposable
{
    private const string Bill = AppealSteps.SecondBill;

    private readonly ManualClock _clock = new(DateTimeOffset.UtcNow);
    private ClockFactory? _factory;

    private ClockFactory Factory => _factory ??= new ClockFactory(database, _clock);

    [Fact]
    public async Task A_side_makes_the_next_page_after_two_seconds_show_the_new_count_and_a_younger_copy_is_served()
    {
        await database.FreeBillAsync(Bill);
        await database.PlanAsync(Bill, ReasonsToComeSteps.VilniusToday().AddDays(2));
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var visitor = Factory.NewBrowser();
        try
        {
            Forget();
            await visitor.Client.GetStringAsync("/");
            var first = Cached();
            var before = For(first);

            (await member.TakeSideAsync(Bill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await visitor.Client.GetStringAsync("/");
            Cached().ShouldBeSameAs(first, "a copy younger than two seconds is served");

            _clock.Advance(TimeSpan.FromMilliseconds(1900));
            await visitor.Client.GetStringAsync("/");
            Cached().ShouldBeSameAs(first, "still younger than two seconds, and still out of date");

            _clock.Advance(TimeSpan.FromMilliseconds(100));
            var html = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/"));
            var fresh = Cached();
            fresh.ShouldNotBeSameAs(first, "the first request once it is two seconds old reads the agenda again");
            For(fresh).ShouldBe(before + 1, "the count the member is part of");
            html.ShouldContain(Lt2.Web.Client.Display.Number(before + 1));

            // Read again without a change: the copy stays.
            await visitor.Client.GetStringAsync("/");
            Cached().ShouldBeSameAs(fresh);

            // Changing and withdrawing a side mark it too.
            (await member.TakeSideAsync(Bill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            _clock.Advance(TimeSpan.FromSeconds(2));
            await visitor.Client.GetStringAsync("/");
            For(Cached()).ShouldBe(before, "changed to against");
            using (var withdrawn = await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(Bill)))
            {
                withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            var afterChange = Cached();
            _clock.Advance(TimeSpan.FromSeconds(2));
            await visitor.Client.GetStringAsync("/");
            Cached().ShouldNotBeSameAs(afterChange, "a withdrawn side marks it too");
            Item(Cached()).Record!.Counts.Against.ShouldBe(Item(afterChange).Record!.Counts.Against - 1);
        }
        finally
        {
            await database.UnplanAsync(Bill);
            Forget();
        }
    }

    /// <summary>
    /// The agenda page reads the same copy (design 8.11.1) and takes sides in its rows too (owner, 2026-11-19): a side taken
    /// marks the copy out of date for it in the same way, so the page after two seconds shows the count the member is part of.
    /// </summary>
    [Fact]
    public async Task A_side_makes_the_agenda_page_after_two_seconds_show_the_new_count()
    {
        await database.FreeBillAsync(Bill);
        await database.PlanAsync(Bill, ReasonsToComeSteps.VilniusToday().AddDays(2));
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var visitor = Factory.NewBrowser();
        try
        {
            Forget();
            await visitor.Client.GetStringAsync("/darbotvarke");
            var first = Cached();
            var before = For(first);

            (await member.TakeSideAsync(Bill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await visitor.Client.GetStringAsync("/darbotvarke");
            Cached().ShouldBeSameAs(first, "a copy younger than two seconds is served");

            _clock.Advance(TimeSpan.FromSeconds(2));
            var html = WebUtility.HtmlDecode(await member.Client.GetStringAsync("/darbotvarke"));
            Cached().ShouldNotBeSameAs(first, "the agenda page reads the copy again too");
            For(Cached()).ShouldBe(before + 1);
            html.ShouldMatch($"id=\"home-counts-[0-9-]+{Bill}\"[^>]*>\\s*<span class=\"nowrap\"><span class=\"choice choice--for\">už</span> {before + 1} ");
        }
        finally
        {
            await database.UnplanAsync(Bill);
            Forget();
        }
    }

    [Fact]
    public async Task Without_a_side_the_copy_lives_its_minute()
    {
        await database.FreeBillAsync(Bill);
        await database.PlanAsync(Bill, ReasonsToComeSteps.VilniusToday().AddDays(2));
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using var visitor = Factory.NewBrowser();
        try
        {
            (await member.TakeSideAsync(Bill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            Forget();
            await visitor.Client.GetStringAsync("/");
            var copy = Cached();
            var counted = For(copy);

            // Withdrawing the positions consent deletes the side, but it is not a side withdrawn in a row: it waits.
            using (var withdrawn = await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.PositionConsent))
            {
                withdrawn.IsSuccessStatusCode.ShouldBeTrue(await withdrawn.Content.ReadAsStringAsync());
            }

            _clock.Advance(TimeSpan.FromSeconds(30));
            await visitor.Client.GetStringAsync("/");
            Cached().ShouldBeSameAs(copy, "nothing marked it: the minute rule");
            For(Cached()).ShouldBe(counted);
            (await visitor.GetJsonAsync<AgendaResponse>(AgendaRoutes.Agenda)).Days.SelectMany(d => d.Items).Single(i => i.Number == Bill)
                .Record!.Counts.For.ShouldBe(counted - 1, "the API stays live");
        }
        finally
        {
            await database.UnplanAsync(Bill);
            Forget();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private AgendaResponse Cached() => Factory.Services.GetRequiredService<IMemoryCache>().Get<AgendaResponse>(FrontPageAgenda.CacheKey)
        ?? throw new InvalidOperationException("The front page was not built from the cache.");

    private void Forget() => Factory.Services.GetRequiredService<IMemoryCache>().Remove(FrontPageAgenda.CacheKey);

    private static AgendaItemResponse Item(AgendaResponse agenda) => agenda.Days.SelectMany(d => d.Items).First(i => i.Number == Bill);

    private static int For(AgendaResponse agenda) => Item(agenda).Record!.Counts.For;

    /// <summary>The web application whose front-page freshness counts time by <see cref="ManualClock"/>.</summary>
    private sealed class ClockFactory(DatabaseFixture database, TimeProvider clock) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton(new FrontPageFreshness(clock)));
        }
    }

    /// <summary>A clock that moves only when told to.</summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
