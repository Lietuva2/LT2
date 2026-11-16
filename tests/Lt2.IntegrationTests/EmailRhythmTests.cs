using System.Globalization;
using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Follows;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.24 over HTTP and through the worker's job: what a member with each choice receives and when. A followed bill gets a
/// planned date and another a stage vote in the same run; "Iš karto" gathers them into one email of that run, "Kartą per dieną"
/// into the next morning's, "Kartą per savaitę ir artėjant balsavimui" into Monday's, with a planned sitting that cannot wait sent
/// at once and alone. The job runs at times the test chose in a week of its own (<see cref="ReasonsToComeSteps.MondayInWeeks"/>
/// 21), with <c>Follows:ResponsesAt</c> at its default (08:00); the vote is moved into that week and put back.
/// </summary>
public sealed class EmailRhythmTests(DatabaseFixture database) : ApiTest(database)
{
    private const string PlannedBill = AppealSteps.SecondBill;
    private const string VotedBill = AppealSteps.TaxBill;
    private const string OtherBill = AppealSteps.ThirdBill;

    /// <summary>The Monday of the test's own week.</summary>
    private static readonly DateOnly Monday = ReasonsToComeSteps.MondayInWeeks(21);

    /// <summary>When the events happen: Tuesday at 10:00 Vilnius time.</summary>
    private static DateTimeOffset Seen => At(Monday.AddDays(1), 10);

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) => VilniusTime.StartOf(day).AddHours(hour).AddMinutes(minute);

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task RunAtAsync(DateTimeOffset now)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(now, TestContext.Current.CancellationToken);
    }

    /// <summary>A member who followed both bills long ago, with the choice <paramref name="frequency"/> (none for the default).</summary>
    private async Task<(TestBrowser Browser, string Email)> FollowerAsync(string? frequency = null)
    {
        var (browser, email) = await Factory.MemberAsync();
        await browser.FollowBillAsync(PlannedBill);
        await browser.FollowBillAsync(VotedBill);
        await Database.AgeFollowsAsync(email, TimeSpan.FromDays(30));
        if (frequency is not null)
        {
            await ChooseAsync(browser, frequency);
        }

        return (browser, email);
    }

    private static async Task ChooseAsync(TestBrowser browser, string frequency)
    {
        using var chosen = await browser.SendAsync(HttpMethod.Put, NoticeFrequencyRoutes.Mine, new NoticeFrequencyRequest { Frequency = frequency });
        chosen.StatusCode.ShouldBe(HttpStatusCode.NoContent, await chosen.Content.ReadAsStringAsync());
    }

    /// <summary>The follow emails written to the address, by outbox kind; sign-in codes and other notices left out.</summary>
    private async Task<List<(string Kind, JsonElement Payload)>> FollowMailAsync(string email) =>
        (await Database.InboxAsync(email)).Where(m => m.Kind.StartsWith("email.follow-", StringComparison.Ordinal) || m.Kind == MessageKinds.GatheredNoticesEmail).ToList();

    private static IEnumerable<string> KindsIn(JsonElement gathered) => gathered.GetProperty("notices").EnumerateArray().Select(n => n.GetProperty("kind").GetString()!);

    private static IEnumerable<JsonElement> Items(JsonElement gathered, string kind) =>
        gathered.GetProperty("notices").EnumerateArray().Where(n => n.GetProperty("kind").GetString() == kind).Select(n => n.GetProperty("payload"));

    /// <summary>The bills' events forgotten; a planned date for <see cref="PlannedBill"/> and the vote on <see cref="VotedBill"/> held an hour before <see cref="Seen"/>.</summary>
    private async Task<DateTime> SetUpAsync(DateOnly planned)
    {
        foreach (var bill in new[] { PlannedBill, VotedBill, OtherBill })
        {
            await Database.ForgetEventsAsync(bill);
        }

        await Database.PlanAsync(PlannedBill, planned, "adoption");
        return await Database.MoveVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, Seen.AddHours(-1));
    }

    private async Task TearDownAsync(DateTime vote)
    {
        await Database.RestoreVoteAsync(ReasonsToComeSteps.TaxConsiderationVote, vote);
        await Database.UnplanAsync(PlannedBill);
        await Database.UnplanAsync(OtherBill);
        foreach (var bill in new[] { PlannedBill, VotedBill, OtherBill })
        {
            await Database.ForgetEventsAsync(bill);
        }
    }

    [Fact]
    public async Task The_choice_is_the_members_own_immediate_until_changed_in_the_export_audited_without_itself_and_gone_with_the_account()
    {
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        (await member.GetJsonAsync<NoticeFrequencyResponse>(NoticeFrequencyRoutes.Mine)).Frequency.ShouldBe(NoticeFrequencies.Immediate);
        (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).Frequency.ShouldBe(NoticeFrequencies.Immediate);

        using (var wrong = await member.SendAsync(HttpMethod.Put, NoticeFrequencyRoutes.Mine, new NoticeFrequencyRequest { Frequency = "monthly" }))
        {
            await wrong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "notices.frequency-invalid");
        }

        using var visitor = NewBrowser();
        using (var refused = await visitor.SendAsync(HttpMethod.Put, NoticeFrequencyRoutes.Mine, new NoticeFrequencyRequest { Frequency = NoticeFrequencies.Daily }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Saving the switches keeps the choice, and the choice keeps the switches.
        await ChooseAsync(member, NoticeFrequencies.Weekly);
        (await member.SendAsync(HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Planned = false })).Dispose();
        var notices = await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices);
        (notices.Planned, notices.Frequency).ShouldBe((false, NoticeFrequencies.Weekly));
        await ChooseAsync(member, NoticeFrequencies.Daily);
        (await member.GetJsonAsync<NoticeSettingsResponse>(FollowRoutes.Notices)).Planned.ShouldBeFalse();

        var userId = await Database.UserIdAsync(email);
        var audited = await Database.ScalarAsync<string>(
            "SELECT json_agg(e)::text FROM audit.event e WHERE action = 'notices.frequency-changed' AND actor_user_id = @u", ("u", userId));
        using (var rows = JsonDocument.Parse(audited!))
        {
            rows.RootElement.GetArrayLength().ShouldBe(2);
            foreach (var row in rows.RootElement.EnumerateArray())
            {
                row.GetProperty("details").ValueKind.ShouldBe(JsonValueKind.Null, "never the choice itself");
            }
        }

        using (var export = await member.Client.GetAsync(ApiRoutes.Me.Export))
        {
            using var json = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("follows").GetProperty("emails").GetProperty("frequency").GetString().ShouldBe(NoticeFrequencies.Daily);
        }

        (await member.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).Dispose();
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.notice_setting WHERE user_id = @u", ("u", userId))).ShouldBe(0);
    }

    [Fact]
    public async Task Is_karto_gathers_a_runs_events_into_one_email_and_tells_each_once()
    {
        var vote = await SetUpAsync(Monday.AddDays(10));
        try
        {
            var (member, email) = await FollowerAsync();
            using var _m = member;
            await RunAtAsync(Seen);
            var mail = await FollowMailAsync(email);
            var (kind, gathered) = mail.ShouldHaveSingleItem("one email per person per run");
            kind.ShouldBe(MessageKinds.GatheredNoticesEmail);
            gathered.GetProperty("rhythm").GetString().ShouldBe("immediate");
            KindsIn(gathered).ShouldContain(MessageKinds.FollowPlannedEmail);
            KindsIn(gathered).ShouldContain(MessageKinds.FollowVotedEmail);
            Items(gathered, MessageKinds.FollowPlannedEmail).Single(p => p.GetProperty("billNumber").GetString() == PlannedBill)
                .GetProperty("date").GetString().ShouldBe(Day(Monday.AddDays(10)));
            gathered.GetProperty("accountPath").GetString().ShouldBe("/sveiki#sekami");

            await RunAtAsync(Seen.AddMinutes(15));
            await RunAtAsync(At(Monday.AddDays(2), 8, 7));
            (await FollowMailAsync(email)).Count.ShouldBe(1, "each thing is told once; nothing waits for a morning");
        }
        finally
        {
            await TearDownAsync(vote);
        }
    }

    [Fact]
    public async Task Karta_per_diena_sends_one_morning_email_with_everything_since_and_a_sign_in_code_at_once()
    {
        var vote = await SetUpAsync(Monday.AddDays(10));
        try
        {
            var (member, email) = await FollowerAsync(NoticeFrequencies.Daily);
            using var _m = member;
            await RunAtAsync(Seen);
            await RunAtAsync(Seen.AddHours(6));
            await RunAtAsync(At(Monday.AddDays(2), 7, 52));
            (await FollowMailAsync(email)).ShouldBeEmpty("nothing before the morning's hour");

            // A sign-in code is never held back.
            using (var code = await NewBrowser().PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
            {
                code.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            (await Database.InboxAsync(email)).ShouldContain(m => m.Kind == MessageKinds.SignInCodeEmail);

            await RunAtAsync(At(Monday.AddDays(2), 8, 7));
            var (kind, gathered) = (await FollowMailAsync(email)).ShouldHaveSingleItem();
            kind.ShouldBe(MessageKinds.GatheredNoticesEmail);
            gathered.GetProperty("rhythm").GetString().ShouldBe("daily");
            Items(gathered, MessageKinds.FollowPlannedEmail).ShouldContain(p => p.GetProperty("billNumber").GetString() == PlannedBill);
            Items(gathered, MessageKinds.FollowVotedEmail).ShouldContain(p => p.GetProperty("votePath").GetString() == "/seimas/balsavimai/" + ReasonsToComeSteps.TaxConsiderationVote);

            await RunAtAsync(At(Monday.AddDays(2), 8, 22));
            await RunAtAsync(At(Monday.AddDays(3), 8, 7));
            (await FollowMailAsync(email)).Count.ShouldBe(1, "the rest of the day and the next morning have nothing new");
        }
        finally
        {
            await TearDownAsync(vote);
        }
    }

    [Fact]
    public async Task The_weekly_choice_sends_a_sitting_that_cannot_wait_at_once_alone_and_the_rest_on_Monday()
    {
        // Planned for next Monday: the day of the next weekly email, so it cannot wait.
        var vote = await SetUpAsync(Monday.AddDays(7));
        try
        {
            var (member, email) = await FollowerAsync(NoticeFrequencies.Weekly);
            using var _m = member;
            await RunAtAsync(Seen);
            var (kind, early) = (await FollowMailAsync(email)).ShouldHaveSingleItem("the planned sitting, alone");
            kind.ShouldBe(MessageKinds.FollowPlannedEmail);
            early.GetProperty("billNumber").GetString().ShouldBe(PlannedBill);

            await RunAtAsync(At(Monday.AddDays(2), 8, 7));
            await RunAtAsync(At(Monday.AddDays(6), 23));
            (await FollowMailAsync(email)).Count.ShouldBe(1, "the vote waits for Monday");

            await RunAtAsync(At(Monday.AddDays(7), 8, 7));
            (await Database.EmailsAsync(MessageKinds.FollowVotedEmail, email)).ShouldBe(1, "Monday's email holds the vote");
            (await Database.EmailsAsync(MessageKinds.FollowPlannedEmail, email)).ShouldBe(1, "the planned sitting went early and is not told again");
            await RunAtAsync(At(Monday.AddDays(7), 8, 22));
            (await FollowMailAsync(email)).Count.ShouldBe(2);
        }
        finally
        {
            await TearDownAsync(vote);
        }
    }

    [Fact]
    public async Task What_waited_when_the_choice_changed_comes_in_the_email_it_waited_for_and_nothing_twice()
    {
        var vote = await SetUpAsync(Monday.AddDays(10));
        try
        {
            var (member, email) = await FollowerAsync(NoticeFrequencies.Daily);
            using var _m = member;
            await member.FollowBillAsync(OtherBill);
            await Database.AgeFollowsAsync(email, TimeSpan.FromDays(1));
            await RunAtAsync(Seen);
            (await FollowMailAsync(email)).ShouldBeEmpty();

            // The member switches to "Iš karto" an hour after the events (the moment moved into the test's week).
            await ChooseAsync(member, NoticeFrequencies.Immediate);
            await Database.ExecuteAsync(
                "UPDATE notify.notice_setting SET frequency_since = @at WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
                ("at", Seen.AddHours(1).UtcDateTime), ("e", email));

            // A later event comes at once; what waited keeps its place.
            await Database.PlanAsync(OtherBill, Monday.AddDays(12), "consideration");
            await RunAtAsync(Seen.AddHours(3));
            var (kind, now) = (await FollowMailAsync(email)).ShouldHaveSingleItem();
            kind.ShouldBe(MessageKinds.FollowPlannedEmail);
            now.GetProperty("billNumber").GetString().ShouldBe(OtherBill);

            await RunAtAsync(At(Monday.AddDays(2), 8, 7));
            var mail = await FollowMailAsync(email);
            mail.Count.ShouldBe(2);
            var gathered = mail[1].Payload;
            gathered.GetProperty("rhythm").GetString().ShouldBe("daily");
            Items(gathered, MessageKinds.FollowPlannedEmail).Select(p => p.GetProperty("billNumber").GetString()).ShouldBe([PlannedBill], "the other bill went already");
            Items(gathered, MessageKinds.FollowVotedEmail).ShouldHaveSingleItem();

            await RunAtAsync(At(Monday.AddDays(3), 8, 7));
            (await FollowMailAsync(email)).Count.ShouldBe(2, "nothing is sent twice");
        }
        finally
        {
            await TearDownAsync(vote);
        }
    }

    [Fact]
    public async Task A_member_with_every_switch_off_gets_nothing_whatever_the_choice()
    {
        var vote = await SetUpAsync(Monday.AddDays(3));
        try
        {
            var people = new List<(TestBrowser Browser, string Email)>();
            foreach (var frequency in new string?[] { null, NoticeFrequencies.Daily, NoticeFrequencies.Weekly })
            {
                var follower = await FollowerAsync(frequency);
                people.Add(follower);
                using var off = await follower.Browser.SendAsync(
                    HttpMethod.Put, FollowRoutes.Notices, new NoticeSettingsRequest { Planned = false, Voted = false, Statements = false, Responses = false });
                off.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            // A member with the switches on, to prove the runs had something to send.
            var (on, onEmail) = await FollowerAsync();
            using var _on = on;
            await RunAtAsync(Seen);
            await RunAtAsync(At(Monday.AddDays(2), 8, 7));
            await RunAtAsync(At(Monday.AddDays(7), 8, 7));
            (await FollowMailAsync(onEmail)).ShouldNotBeEmpty();
            foreach (var (browser, email) in people)
            {
                (await FollowMailAsync(email)).ShouldBeEmpty();
                browser.Dispose();
            }
        }
        finally
        {
            await TearDownAsync(vote);
        }
    }
}
