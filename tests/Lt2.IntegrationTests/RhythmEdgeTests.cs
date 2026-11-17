using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts.Follows;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.24 at its edges: one daily email a day and one weekly a week even when <c>Follows:ResponsesAt</c> moves; Monday's
/// email made late after an outage; what waits for a gathered email after the person unfollows, switches the kind off or
/// deletes the account; an MP's answer that changes stance or is withdrawn inside one daily window. The job runs at moments
/// the test chose in a week of its own (<see cref="ReasonsToComeSteps.MondayInWeeks"/> 23).
/// </summary>
public sealed class RhythmEdgeTests(DatabaseFixture database) : ApiTest(database)
{
    private const string VotedBill = AppealSteps.TaxBill;
    private const string ResponseBill = AppealSteps.ThirdBill;

    private static readonly DateOnly Monday = ReasonsToComeSteps.MondayInWeeks(23);

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) => VilniusTime.StartOf(day).AddHours(hour).AddMinutes(minute);

    /// <summary>The vote on the tax bill is held on Tuesday at 09:00, seen by the 10:00 run.</summary>
    private static DateTimeOffset Seen => At(Monday.AddDays(1), 10);

    private async Task RunAtAsync(DateTimeOffset now)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(now, TestContext.Current.CancellationToken);
    }

    private async Task<List<(string Kind, JsonElement Payload)>> FollowMailAsync(string email) =>
        (await Database.InboxAsync(email)).Where(m => m.Kind.StartsWith("email.follow-", StringComparison.Ordinal) || m.Kind == MessageKinds.GatheredNoticesEmail).ToList();

    private async Task<(TestBrowser Browser, string Email)> FollowerAsync(string frequency)
    {
        var (browser, email) = await Factory.MemberAsync();
        await browser.FollowBillAsync(VotedBill);
        await Database.AgeFollowsAsync(email, TimeSpan.FromDays(400));
        using var chosen = await browser.SendAsync(HttpMethod.Put, NoticeFrequencyRoutes.Mine, new NoticeFrequencyRequest { Frequency = frequency });
        chosen.StatusCode.ShouldBe(HttpStatusCode.NoContent, await chosen.Content.ReadAsStringAsync());
        await Database.ExecuteAsync(
            "UPDATE notify.notice_setting SET frequency_since = @at WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("at", Monday.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), ("e", email));
        return (browser, email);
    }

    private async Task<DateTime> StartAsync()
    {
        await Database.ForgetEventsAsync(VotedBill);
        await Database.ForgetRhythmPassesAsync();
        return await Database.MoveVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, Seen.AddHours(-1));
    }

    private async Task EndAsync(DateTime vote)
    {
        await Database.RestoreVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, vote);
        await Database.ForgetEventsAsync(VotedBill);
        await Database.ForgetRhythmPassesAsync();
    }

    [Fact]
    public async Task Moving_the_morning_hour_later_the_same_day_sends_no_second_daily_email()
    {
        var vote = await StartAsync();
        var admin = await AdministratorAsync();
        try
        {
            var (member, email) = await FollowerAsync(NoticeFrequencies.Daily);
            using var _m = member;
            await RunAtAsync(Seen);
            await RunAtAsync(At(Monday.AddDays(2), 8, 7));
            (await FollowMailAsync(email)).Count.ShouldBe(1, "Wednesday's morning email holds the vote");

            // Something new seen after the email, then the hour moves to 10:00: no second email that day, one the next morning.
            await Database.PlanAsync(AppealSteps.TaxBill, Monday.AddDays(20), "adoption");
            await RunAtAsync(At(Monday.AddDays(2), 9));
            await admin.Browser.SetSettingAsync(SettingKeys.FollowsResponsesAt.Name, "10:00");
            await RunAtAsync(At(Monday.AddDays(2), 10, 7));
            (await FollowMailAsync(email)).Count.ShouldBe(1, "at most one daily email a Vilnius day");
            await RunAtAsync(At(Monday.AddDays(3), 10, 7));
            (await FollowMailAsync(email)).Count.ShouldBe(2, "the planned date comes the next morning");
        }
        finally
        {
            await admin.Browser.ResetSettingAsync(SettingKeys.FollowsResponsesAt.Name);
            await Database.UnplanAsync(AppealSteps.TaxBill);
            await EndAsync(vote);
        }
    }

    [Fact]
    public async Task Mondays_email_missed_by_an_outage_is_made_at_the_next_run_once()
    {
        var vote = await StartAsync();
        try
        {
            var (member, email) = await FollowerAsync(NoticeFrequencies.Weekly);
            using var _m = member;
            await RunAtAsync(Seen);

            // No run on the next Monday: the job is back on Wednesday.
            await RunAtAsync(At(Monday.AddDays(9), 14));
            var (kind, gathered) = (await FollowMailAsync(email)).ShouldHaveSingleItem("Monday's email, late");
            kind.ShouldBe(MessageKinds.FollowVotedEmail);
            gathered.GetProperty("votePath").GetString().ShouldEndWith(ReasonsToComeSteps.TaxConsiderationVote.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await RunAtAsync(At(Monday.AddDays(9), 14, 15));
            await RunAtAsync(At(Monday.AddDays(10), 8, 7));
            (await FollowMailAsync(email)).Count.ShouldBe(1, "once");
        }
        finally
        {
            await EndAsync(vote);
        }
    }

    [Fact]
    public async Task What_waits_for_a_daily_email_is_not_sent_after_unfollowing_switching_the_kind_off_or_deleting_the_account()
    {
        var vote = await StartAsync();
        try
        {
            var (unfollows, unfollowsEmail) = await FollowerAsync(NoticeFrequencies.Daily);
            var (switches, switchesEmail) = await FollowerAsync(NoticeFrequencies.Daily);
            var (leaves, leavesEmail) = await FollowerAsync(NoticeFrequencies.Daily);
            var (stays, staysEmail) = await FollowerAsync(NoticeFrequencies.Daily);
            using var _u = unfollows;
            using var _s = switches;
            using var _st = stays;
            await RunAtAsync(Seen);

            using (var unfollowed = await unfollows.SendAsync(HttpMethod.Delete, FollowRoutes.BillOf(VotedBill)))
            {
                unfollowed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            using (var off = await switches.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Voted = false }))
            {
                off.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            await leaves.DeleteAccountAsync();
            leaves.Dispose();

            await RunAtAsync(At(Monday.AddDays(2), 8, 7));
            (await FollowMailAsync(staysEmail)).ShouldHaveSingleItem("the run had the vote to tell");
            (await FollowMailAsync(unfollowsEmail)).ShouldBeEmpty("unfollowed meanwhile");
            (await FollowMailAsync(switchesEmail)).ShouldBeEmpty("the kind switched off meanwhile");
            (await FollowMailAsync(leavesEmail)).ShouldBeEmpty("the account is gone");
        }
        finally
        {
            await EndAsync(vote);
        }
    }

    [Fact]
    public async Task An_answer_that_changes_stance_or_is_withdrawn_inside_one_daily_window_is_told_as_it_stands()
    {
        await Database.FreeBillAsync(ResponseBill);
        await Database.ForgetEventsAsync(ResponseBill);
        await Database.ForgetRhythmPassesAsync();
        var today = ReasonsToComeSteps.VilniusToday();
        using var changes = await Factory.MpAccountAsync(MpSteps.Liepaitis);
        using var withdraws = await Factory.MpAccountAsync(MpSteps.Sermuksnyte);
        var (reader, readerEmail) = await Factory.MemberAsync("Ryto Skaitytoja");
        using var _r = reader;
        try
        {
            await reader.FollowBillAsync(ResponseBill);
            await Database.AgeFollowsAsync(readerEmail, TimeSpan.FromDays(2));
            using (var chosen = await reader.SendAsync(HttpMethod.Put, NoticeFrequencyRoutes.Mine, new NoticeFrequencyRequest { Frequency = NoticeFrequencies.Daily }))
            {
                chosen.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            await Database.ExecuteAsync(
                "UPDATE notify.notice_setting SET frequency_since = @at WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
                ("at", today.AddDays(-3).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)), ("e", readerEmail));

            await changes.Browser.RespondOkAsync(ResponseBill, "for", "Pirmoji pozicija.");
            await withdraws.Browser.RespondOkAsync(ResponseBill, "against", "Atsiimsiu.");
            var now = DateTimeOffset.UtcNow;
            await RunAtAsync(now);

            // Before the morning: one MP changes stance, the other withdraws.
            await changes.Browser.RespondOkAsync(ResponseBill, "against", "Pozicija pasikeitė.");
            using (var withdrawn = await withdraws.Browser.WithdrawResponseAsync(ResponseBill))
            {
                withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await withdrawn.Content.ReadAsStringAsync());
            }

            await RunAtAsync(now.AddMinutes(15));
            (await Database.InboxAsync(readerEmail)).ShouldNotContain(m => m.Kind == MessageKinds.FollowResponsesEmail || m.Kind == MessageKinds.GatheredNoticesEmail);

            var morning = VilniusTime.StartOf(VilniusTime.DateOf(now).AddDays(1)).AddHours(8).AddMinutes(30);
            await RunAtAsync(morning);
            using var told = JsonDocument.Parse(await Database.NoticesAsync(MessageKinds.FollowResponsesEmail, readerEmail));
            var email = told.RootElement.EnumerateArray().ShouldHaveSingleItem("one part for the bill in the morning email");
            var listed = email.GetProperty("responses").EnumerateArray().Select(r => (r.GetProperty("name").GetString(), r.GetProperty("stance").GetString())).ToList();
            listed.ShouldBe([(MpSteps.Liepaitis.Name, "against")], "the stance as it stands; the withdrawn answer is left out");
        }
        finally
        {
            await changes.Browser.DeleteAccountAsync();
            await withdraws.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(ResponseBill);
            await Database.ForgetEventsAsync(ResponseBill);
            await Database.ForgetRhythmPassesAsync();
        }
    }
}
