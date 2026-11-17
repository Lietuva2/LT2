using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Follows;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.11.2 over HTTP and through the worker's job: following bills and MPs, the account page's list and switches,
/// and the two follow emails, sent once per event to those who followed before it, through the outbox.
/// </summary>
public sealed class FollowTests(DatabaseFixture database) : ApiTest(database)
{
    private async Task<FollowRunOutcome> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_member_follows_and_unfollows_bills_and_MPs_and_sees_only_their_own()
    {
        using var visitor = NewBrowser();
        using (var refused = await visitor.SendAsync(HttpMethod.Put, FollowRoutes.BillOf(AppealSteps.TaxBill)))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var (member, _) = await Factory.MemberAsync(consent: false);
        using var _m = member;
        await member.FollowBillAsync(AppealSteps.TaxBill);
        await member.FollowBillAsync(AppealSteps.TaxBill);
        await member.FollowMpAsync(ReasonsToComeSteps.Petraitis);
        var mine = await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine);
        mine.Bills.Single().Number.ShouldBe(AppealSteps.TaxBill);
        mine.Bills.Single().Title.ShouldBe("Gyventojų pajamų mokesčio įstatymo pataisa");
        mine.Mps.Single().LastName.ShouldBe("Petraitis");

        using (var unknownBill = await member.SendAsync(HttpMethod.Put, FollowRoutes.BillOf("XVP-9999")))
        {
            await unknownBill.ShouldBeProblemAsync(HttpStatusCode.NotFound, "bill.not-found");
        }

        using (var unknownMp = await member.SendAsync(HttpMethod.Put, FollowRoutes.MpOf("nera-tokio")))
        {
            await unknownMp.ShouldBeProblemAsync(HttpStatusCode.NotFound, "mp.not-found");
        }

        (await member.SendAsync(HttpMethod.Delete, FollowRoutes.BillOf(AppealSteps.TaxBill))).Dispose();
        (await member.SendAsync(HttpMethod.Delete, FollowRoutes.MpOf(ReasonsToComeSteps.Petraitis))).Dispose();
        mine = await member.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine);
        mine.Bills.ShouldBeEmpty();
        mine.Mps.ShouldBeEmpty();

        using var raw = await member.Client.GetAsync(FollowRoutes.Mine);
        raw.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task A_follower_gets_one_planned_email_and_nobody_who_followed_later_gets_it()
    {
        await Database.ForgetEventsAsync(AppealSteps.SecondBill);
        var (early, earlyEmail) = await Factory.MemberAsync();
        using var _e = early;
        await early.FollowBillAsync(AppealSteps.SecondBill);
        await Database.AgeFollowsAsync(earlyEmail, TimeSpan.FromHours(1));
        var date = ReasonsToComeSteps.VilniusToday().AddDays(5);
        await Database.PlanAsync(AppealSteps.SecondBill, date, "adoption");
        try
        {
            await RunJobAsync();
            (await Database.EmailsAsync("email.follow-planned", earlyEmail)).ShouldBe(1);
            using (var payload = JsonDocument.Parse((await Database.LatestEmailAsync("email.follow-planned", earlyEmail))!))
            {
                payload.RootElement.GetProperty("billNumber").GetString().ShouldBe(AppealSteps.SecondBill);
                payload.RootElement.GetProperty("date").GetString().ShouldBe(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                payload.RootElement.GetProperty("stage").GetString().ShouldBe("adoption");
                payload.RootElement.GetProperty("accountPath").GetString().ShouldBe("/paskyra/sekimas#laisku-daznis");
            }

            // Recorded once: a second run sends nothing more.
            await RunJobAsync();
            (await Database.EmailsAsync("email.follow-planned", earlyEmail)).ShouldBe(1);

            // Someone who follows now gets nothing about a date seen before they followed.
            var (late, lateEmail) = await Factory.MemberAsync();
            using var _l = late;
            await late.FollowBillAsync(AppealSteps.SecondBill);
            await RunJobAsync();
            (await Database.EmailsAsync("email.follow-planned", lateEmail)).ShouldBe(0);

            // The date moves again the same Vilnius day: recorded, and it waits for the next day.
            await Database.PlanAsync(AppealSteps.SecondBill, date.AddDays(2), "adoption");
            await RunJobAsync();
            (await Database.EmailsAsync("email.follow-planned", earlyEmail)).ShouldBe(1);
            (await Database.ScalarAsync<string>(
                "SELECT outcome FROM notify.bill_event WHERE bill_number = @b AND kind = 'planned' AND key = @k",
                ("b", AppealSteps.SecondBill), ("k", date.AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))).ShouldBe("waiting");

            var mine = await early.GetJsonAsync<MyFollowsResponse>(FollowRoutes.Mine);
            mine.Bills.Single(b => b.Number == AppealSteps.SecondBill).Planned!.Date.ShouldBe(date.AddDays(2));
        }
        finally
        {
            await Database.UnplanAsync(AppealSteps.SecondBill);
        }
    }

    [Fact]
    public async Task A_follower_gets_one_voted_email_with_how_the_MPs_they_follow_voted_and_no_side()
    {
        await Database.ForgetEventsAsync(AppealSteps.TaxBill);
        var (follower, email) = await Factory.MemberAsync();
        using var _f = follower;
        await follower.FollowBillAsync(AppealSteps.TaxBill);
        await follower.FollowMpAsync(ReasonsToComeSteps.Petraitis);
        await Database.AgeFollowsAsync(email, TimeSpan.FromDays(10));
        var (late, lateEmail) = await Factory.MemberAsync();
        using var _l = late;
        await late.FollowBillAsync(AppealSteps.TaxBill);
        await Database.AgeFollowsAsync(lateEmail, TimeSpan.FromMinutes(30));
        var (quiet, quietEmail) = await Factory.MemberAsync();
        using var _q = quiet;
        await quiet.FollowBillAsync(AppealSteps.TaxBill);
        await Database.AgeFollowsAsync(quietEmail, TimeSpan.FromDays(10));
        (await quiet.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Planned = true, Voted = false })).Dispose();
        (await quiet.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).ShouldBe(new NoticeSettingsResponse(true, false));

        var old = await Database.MoveVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, DateTimeOffset.UtcNow.AddHours(-1));
        try
        {
            await RunJobAsync();
            (await Database.EmailsAsync("email.follow-voted", email)).ShouldBe(1);
            var payload = (await Database.LatestEmailAsync("email.follow-voted", email))!;
            using (var json = JsonDocument.Parse(payload))
            {
                var root = json.RootElement;
                root.GetProperty("stage").GetString().ShouldBe("consideration");
                root.GetProperty("for").GetInt32().ShouldBe(74);
                root.GetProperty("votePath").GetString().ShouldBe("/seimas/balsavimai/" + ReasonsToComeSteps.TaxConsiderationVote);
                var mp = root.GetProperty("mps").EnumerateArray().Single();
                mp.GetProperty("name").GetString().ShouldBe("Jonas Petraitis");
                mp.GetProperty("choice").GetString().ShouldBe("against");
                root.TryGetProperty("side", out _).ShouldBeFalse();
                root.TryGetProperty("position", out _).ShouldBeFalse();
            }

            (await Database.EmailsAsync("email.follow-voted", lateEmail)).ShouldBe(0, "followed after the vote was held");
            (await Database.EmailsAsync("email.follow-voted", quietEmail)).ShouldBe(0, "the voted email is switched off");

            await RunJobAsync();
            (await Database.EmailsAsync("email.follow-voted", email)).ShouldBe(1, "one email per event");
        }
        finally
        {
            await Database.RestoreVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, old);
        }
    }

    [Fact]
    public async Task A_vote_older_than_three_days_when_first_seen_is_recorded_and_never_sent()
    {
        await Database.ForgetEventsAsync(AppealSteps.TaxBill);
        var (follower, email) = await Factory.MemberAsync();
        using var _f = follower;
        await follower.FollowBillAsync(AppealSteps.TaxBill);
        await Database.AgeFollowsAsync(email, TimeSpan.FromDays(30));
        var old = await Database.MoveVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, DateTimeOffset.UtcNow.AddDays(-5));
        try
        {
            await RunJobAsync();
            (await Database.EmailsAsync("email.follow-voted", email)).ShouldBe(0);
            (await Database.ScalarAsync<string>(
                "SELECT outcome FROM notify.bill_event WHERE bill_number = @b AND kind = 'voted' AND key = @k",
                ("b", AppealSteps.TaxBill), ("k", ReasonsToComeSteps.TaxConsiderationVote.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ShouldBe("skipped");
        }
        finally
        {
            await Database.RestoreVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, old);
        }
    }

    [Fact]
    public async Task Follows_are_in_the_export_and_go_with_the_account()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        await member.FollowBillAsync(AppealSteps.ThirdBill);
        await member.FollowMpAsync(ReasonsToComeSteps.Petraitis);
        (await member.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Planned = false, Voted = true })).Dispose();
        using (var export = await member.Client.GetAsync(ApiRoutes.Me.Export))
        {
            using var json = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
            var follows = json.RootElement.GetProperty("follows");
            follows.GetProperty("follows").GetArrayLength().ShouldBe(2);
            follows.GetProperty("emails").GetProperty("planned").GetBoolean().ShouldBeFalse();
        }

        var userId = await Database.UserIdAsync(email);
        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).Dispose();
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.follow WHERE user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.notice_setting WHERE user_id = @u", ("u", userId))).ShouldBe(0);
    }

    [Fact]
    public async Task The_account_page_lists_the_follows_and_the_bill_page_offers_to_follow()
    {
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        await member.FollowBillAsync(AppealSteps.ThirdBill);
        var account = WebUtility.HtmlDecode(await member.Client.GetStringAsync("/paskyra/sekimas"));
        account.ShouldContain("id=\"sekami\"");
        account.ShouldContain(AppealSteps.ThirdBill);
        account.ShouldContain("Kai Seimas balsuoja dėl sekamo projekto");

        var bill = WebUtility.HtmlDecode(await member.Client.GetStringAsync("/seimas/" + AppealSteps.ThirdBill));
        bill.ShouldContain("id=\"follow-toggle-bill\"");
        bill.ShouldContain("Nebesekti");

        using var visitor = NewBrowser();
        var forVisitor = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync("/p/" + ReasonsToComeSteps.Petraitis));
        forVisitor.ShouldContain("id=\"follow-mp\"");
        forVisitor.ShouldNotContain("Nebesekti");
    }
}
