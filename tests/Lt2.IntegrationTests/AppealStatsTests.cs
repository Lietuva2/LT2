using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>Design 5.3 and 6.6: the daily aggregate, and the surge note on the page and in the moderators' queue.</summary>
public sealed class AppealStatsTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task The_daily_aggregate_holds_counts_per_side_older_accounts_and_the_share_of_new_accounts_and_nobody()
    {
        var (moderator, _) = await StaffAsync();
        var (initiator, initiatorEmail) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        await Database.AgeAccountAsync(initiatorEmail, 60);
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = Factory.Services.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>();
        var today = ReasonsToComeSteps.VilniusToday();
        (await maintenance.AggregateDayAsync(today, CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
        await maintenance.AggregateDayAsync(today, CancellationToken.None);

        (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.daily_stat WHERE appeal_id = @a", ("a", appeal.Id))).ShouldBe(1, "running a day again overwrites it");
        (await Database.ScalarAsync<int>("SELECT for_count FROM appeals.daily_stat WHERE appeal_id = @a", ("a", appeal.Id))).ShouldBe(1);
        (await Database.ScalarAsync<int>("SELECT against_count FROM appeals.daily_stat WHERE appeal_id = @a", ("a", appeal.Id))).ShouldBe(1);
        (await Database.ScalarAsync<int>("SELECT against_older_than30days FROM appeals.daily_stat WHERE appeal_id = @a", ("a", appeal.Id))).ShouldBe(1);
        (await Database.ScalarAsync<decimal>("SELECT new_account_share FROM appeals.daily_stat WHERE appeal_id = @a", ("a", appeal.Id))).ShouldBe(0.5m);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'appeals' AND table_name = 'daily_stat' AND column_name LIKE '%user%'")).ShouldBe(0);
    }

    /// <summary>Design 5.3: a finished day's row is written once after the day; later changes of side do not reach it.</summary>
    [Fact]
    public async Task A_finished_days_aggregate_is_written_once_and_later_changes_of_side_do_not_reach_it()
    {
        var (moderator, _) = await StaffAsync();
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var appeal = await Database.PrepareBillAsync(member, moderator.Browser, AppealSteps.ThirdBill, "against");

        // The record and the side as if from two days ago.
        await Database.ExecuteAsync("UPDATE appeals.appeal SET published_at = published_at - interval '2 days' WHERE id = @a", ("a", appeal.Id));
        await Database.ExecuteAsync("UPDATE appeals.position SET created_at = created_at - interval '2 days' WHERE appeal_id = @a", ("a", appeal.Id));
        await using var scope = Factory.Services.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IAppealsMaintenance>();
        var yesterday = Lt2.SharedKernel.VilniusTime.Today(TimeProvider.System).AddDays(-1);
        (await maintenance.AggregateDayAsync(yesterday, CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1, "the first run after the day writes it");
        (await Database.ScalarAsync<int>("SELECT against_count FROM appeals.daily_stat WHERE appeal_id = @a AND day = @d", ("a", appeal.Id), ("d", yesterday))).ShouldBe(1);

        // Today the member changes side; the hourly job runs yesterday again.
        (await member.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await maintenance.AggregateDayAsync(yesterday, CancellationToken.None);
        (await Database.ScalarAsync<int>("SELECT against_count FROM appeals.daily_stat WHERE appeal_id = @a AND day = @d", ("a", appeal.Id), ("d", yesterday)))
            .ShouldBe(1, "yesterday's row is final");
        (await Database.ScalarAsync<int>("SELECT for_count FROM appeals.daily_stat WHERE appeal_id = @a AND day = @d", ("a", appeal.Id), ("d", yesterday))).ShouldBe(0);
    }

    [Fact]
    public async Task A_surge_is_noted_in_neutral_words_on_the_page_and_put_before_a_moderator()
    {
        var (moderator, administrator) = await StaffAsync();
        var (initiator, initiatorEmail) = await Factory.MemberAsync();
        using var _i = initiator;
        var appeal = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against");
        await Database.AgeAccountAsync(initiatorEmail, 60);
        await Database.ExecuteAsync("UPDATE appeals.position SET created_at = created_at - interval '10 days' WHERE appeal_id = @a", ("a", appeal.Id));
        for (var i = 0; i < 2; i++)
        {
            var (fresh, _) = await Factory.MemberAsync();
            (await fresh.TakeSideAsync(AppealSteps.ThirdBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            fresh.Dispose();
        }

        using var stranger = NewBrowser();
        (await stranger.AppealAsync(AppealSteps.ThirdBill)).Surge.ShouldBeNull("2 new accounts are fewer than 50");

        await administrator.Browser.SetSettingAsync("Surge:MinimumAccounts", 2);
        try
        {
            var surged = await stranger.AppealAsync(AppealSteps.ThirdBill);
            surged.Surge.ShouldBe(new SurgeResponse(100, 72));
            using var page = await stranger.Client.GetAsync("/seimas/XVP-0618");
            WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync())
                .ShouldContain("Per paskutines 72 valandas 100 % pozicijų pateikė paskyros, sukurtos per tą patį laiką. Skaičius „senesnės nei 30 d.“ jų neapima.");
            var queued = (await moderator.Browser.GetJsonAsync<ModerationQueueResponse>(AppealRoutes.ModerationQueue)).Appeals.Single(a => a.Id == appeal.Id);
            queued.Surge.ShouldBe(new SurgeResponse(100, 72));

            await administrator.Browser.SetSettingAsync("Surge:MinimumShare", 1.0);
            (await stranger.AppealAsync(AppealSteps.ThirdBill)).Surge.ShouldNotBeNull("2 of 2 is 100 %");
        }
        finally
        {
            await administrator.Browser.ResetSettingAsync("Surge:MinimumAccounts");
            await administrator.Browser.ResetSettingAsync("Surge:MinimumShare");
        }

        (await stranger.AppealAsync(AppealSteps.ThirdBill)).Surge.ShouldBeNull();
    }
}
