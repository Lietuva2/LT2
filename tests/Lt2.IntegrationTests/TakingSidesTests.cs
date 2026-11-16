using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.11.2, "Taking a side follows the bill", over HTTP: a member's first side on a bill they do not follow follows it
/// in the same request; following already stays one follow; changing the side adds nothing; withdrawing the side keeps the
/// follow and "Nebesekti" keeps the side; the side is saved even when following fails. The follow row holds nothing about
/// the side.
/// </summary>
public sealed class TakingSidesTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    [Fact]
    public async Task A_first_side_follows_the_bill_in_the_same_request()
    {
        await Database.FreeBillAsync(Bill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        (await FollowRowsAsync(email)).ShouldBe(0, "not followed before");

        using (var took = await member.TakeSideAsync(Bill, "for"))
        {
            took.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await FollowRowsAsync(email)).ShouldBe(1);
        (await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine)).Bills.Select(b => b.Number).ShouldBe([Bill]);
        var mine = await member.GetJsonAsync<MyBillsResponse>(ApiRoutes.Me.Bills + "?" + new MyBillsQuery { Numbers = [Bill] }.ToQueryString());
        mine.Bills.ShouldBe([new MyBillResponse(Bill, "for", true)]);

        // The follow row holds nothing about the side: exactly these columns.
        var columns = await Database.ScalarAsync<string>(
            "SELECT string_agg(column_name, ',' ORDER BY column_name) FROM information_schema.columns WHERE table_schema = 'notify' AND table_name = 'follow'");
        columns.ShouldBe("created_at,key,kind,user_id");
    }

    [Fact]
    public async Task Following_already_stays_one_follow()
    {
        await Database.FreeBillAsync(Bill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        await member.FollowBillAsync(Bill);
        var followedAt = await FollowedAtAsync(email);

        using (var took = await member.TakeSideAsync(Bill, "against"))
        {
            took.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await FollowRowsAsync(email)).ShouldBe(1);
        (await FollowedAtAsync(email)).ShouldBe(followedAt, "the earlier follow is kept as it was");
    }

    [Fact]
    public async Task Changing_the_side_adds_nothing_and_withdrawing_it_keeps_the_follow()
    {
        await Database.FreeBillAsync(Bill);
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(Bill, "for")).Dispose();
        (await FollowRowsAsync(email)).ShouldBe(1);

        // "Nebesekti" keeps the side; changing the side then follows nothing again.
        using (var unfollow = await member.SendAsync(HttpMethod.Delete, FollowRoutes.BillOf(Bill)))
        {
            unfollow.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await member.GetJsonAsync<MyBillRecordResponse>(AppealRoutes.MineOf(Bill))).Side.ShouldBe("for", "unfollowing keeps the side");
        using (var changed = await member.TakeSideAsync(Bill, "against"))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await FollowRowsAsync(email)).ShouldBe(0, "a changed side is not a first side");

        // Following again, then withdrawing the side: the follow stays.
        await member.FollowBillAsync(Bill);
        using (var withdrawn = await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(Bill)))
        {
            withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await FollowRowsAsync(email)).ShouldBe(1);
        (await member.GetJsonAsync<MyBillRecordResponse>(AppealRoutes.MineOf(Bill))).Side.ShouldBeNull();
    }

    [Fact]
    public async Task A_refused_side_follows_nothing()
    {
        await Database.FreeBillAsync(Bill);
        var (member, email) = await Factory.MemberAsync(consent: false);
        using var _m = member;

        using (var refused = await member.TakeSideAsync(Bill, "for"))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "consent.required");
        }

        (await FollowRowsAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task The_side_is_saved_when_following_fails()
    {
        await Database.FreeBillAsync(Bill);
        await using var factory = new FailingFollowsFactory(Database);
        var (member, email) = await factory.MemberAsync();
        using var _m = member;

        using (var took = await member.TakeSideAsync(Bill, "for"))
        {
            took.StatusCode.ShouldBe(HttpStatusCode.NoContent, "the side is never refused because of the follow");
        }

        (await member.GetJsonAsync<MyBillRecordResponse>(AppealRoutes.MineOf(Bill))).Side.ShouldBe("for");
        (await FollowRowsAsync(email)).ShouldBe(0);
    }

    private Task<long> FollowRowsAsync(string email) => Database.ScalarAsync<long>(
        "SELECT count(*) FROM notify.follow WHERE kind = 'bill' AND key = @b AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
        ("b", Bill), ("e", email));

    private Task<DateTime> FollowedAtAsync(string email) => Database.ScalarAsync<DateTime>(
        "SELECT created_at FROM notify.follow WHERE kind = 'bill' AND key = @b AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
        ("b", Bill), ("e", email));

    /// <summary>The web application with following broken: every follow fails with an exception.</summary>
    private sealed class FailingFollowsFactory(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddScoped<IFollows, FailingFollows>());
        }
    }

    private sealed class FailingFollows : IFollows
    {
        private static InvalidOperationException Broken() => new("Following is broken in this test.");

        public Task<MyFollows> MineAsync(Guid userId, CancellationToken cancellationToken) => throw Broken();

        public Task<IReadOnlySet<string>> FollowedBillNumbersAsync(Guid userId, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> FollowBillAsync(Guid userId, string number, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> UnfollowBillAsync(Guid userId, string number, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> FollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> UnfollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> FollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> UnfollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> FollowMemberAsync(Guid userId, string profileId, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> UnfollowMemberAsync(Guid userId, string profileId, CancellationToken cancellationToken) => throw Broken();

        public Task<NoticeSettings> NoticesAsync(Guid userId, CancellationToken cancellationToken) => throw Broken();

        public Task<Result<Unit>> SetNoticesAsync(Guid userId, NoticeSettings settings, CancellationToken cancellationToken) => throw Broken();

        public Task<FollowRunOutcome> RunAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw Broken();
    }
}
