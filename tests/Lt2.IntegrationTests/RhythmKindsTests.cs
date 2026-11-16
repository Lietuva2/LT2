using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Follows;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.24 with the kinds that joined it from the profiles (8.25.5, a followed member's argument) and the comments (8.23.9,
/// comments under one's own argument): both are held for the person's choice and come as their own parts of the gathered email,
/// with nothing of the texts, the writers or the counts; under "Iš karto" each comes in the run's email, once.
/// </summary>
public sealed class RhythmKindsTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.SecondBill;
    private const string ArgumentText = "Slaptas sekamos narės argumentas, kurio laiške būti neturi.";
    private const string OwnText = "Skaitytojos pačios argumentas apie šią pataisą.";
    private const string CommentText = "Slaptas komentaras po skaitytojos argumentu.";

    private async Task RunAtAsync(DateTimeOffset now)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(now, TestContext.Current.CancellationToken);
    }

    /// <summary>The member emails of these kinds and the gathered ones, to the address.</summary>
    private async Task<List<(string Kind, JsonElement Payload)>> MailAsync(string email) =>
        (await Database.InboxAsync(email))
            .Where(m => m.Kind is MessageKinds.FollowMemberEmail or MessageKinds.CommentNoticeEmail or MessageKinds.GatheredNoticesEmail)
            .ToList();

    /// <summary>
    /// A follower of a public member who wrote an argument, with an argument of their own that someone else commented on; the
    /// follower's choice is <paramref name="frequency"/>. Returns the follower's address, the two argument ids and the comment.
    /// </summary>
    private async Task<(string FollowerEmail, Guid Followed, Guid Own, Guid Comment, List<IDisposable> Browsers)> ArrangeAsync(string frequency)
    {
        await Database.FreeBillAsync(Bill);
        await Database.ForgetEventsAsync(Bill);
        await Database.ExecuteAsync("DELETE FROM notify.bill_event WHERE kind = 'comment'");
        var (author, _, profileId) = await Factory.PublicMemberAsync("Sekama Autorė");
        var (follower, followerEmail) = await Factory.MemberAsync("Skaitytoja Ritmė");
        var (commenter, _) = await Factory.MemberAsync("Komentuojantis Kaimynas");
        await follower.FollowMemberAsync(profileId);
        await Database.AgeFollowsAsync(followerEmail, TimeSpan.FromHours(1));
        using (var chosen = await follower.SendAsync(HttpMethod.Put, NoticeFrequencyRoutes.Mine, new NoticeFrequencyRequest { Frequency = frequency }))
        {
            chosen.StatusCode.ShouldBe(HttpStatusCode.NoContent, await chosen.Content.ReadAsStringAsync());
        }

        var followed = await WrittenAsync(author, "against", ArgumentText);
        var own = await WrittenAsync(follower, "for", OwnText);
        var comment = await commenter.CommentOkAsync("reason", own, CommentText);
        return (followerEmail, followed, own, comment, [author, follower, commenter]);
    }

    private static async Task<Guid> WrittenAsync(TestBrowser browser, string side, string text)
    {
        using var written = await browser.WriteReasonAsync(Bill, side, text);
        written.StatusCode.ShouldBe(HttpStatusCode.Created, await written.Content.ReadAsStringAsync());
        return (await written.Content.ReadFromJsonAsync<ReasonCreatedResponse>(JsonSerializerOptions.Web, TestContext.Current.CancellationToken))!.Id;
    }

    private static void HoldsNoText(JsonElement payload)
    {
        var raw = payload.GetRawText();
        raw.ShouldNotContain("Slaptas", Case.Sensitive, "never an argument's or a comment's text");
        raw.ShouldNotContain(OwnText, Case.Sensitive);
        raw.ShouldNotContain("Komentuojantis", Case.Sensitive, "never who commented");
    }

    [Fact]
    public async Task Kartą_per_dieną_holds_a_followed_members_argument_and_comments_under_ones_own_for_the_morning_as_their_own_parts()
    {
        var (email, followed, own, _, browsers) = await ArrangeAsync(NoticeFrequencies.Daily);
        try
        {
            var seen = DateTimeOffset.UtcNow.AddSeconds(1);
            await RunAtAsync(seen);
            (await MailAsync(email)).ShouldBeEmpty("a daily choice waits for the morning");

            // The first morning after the run: 08:30 Vilnius time (Follows:ResponsesAt is 08:00 by default).
            var morning = VilniusTime.StartOf(VilniusTime.DateOf(seen).AddDays(1)).AddHours(8).AddMinutes(30);
            await RunAtAsync(morning);
            var (kind, gathered) = (await MailAsync(email)).ShouldHaveSingleItem("one morning email");
            kind.ShouldBe(MessageKinds.GatheredNoticesEmail);
            gathered.GetProperty("rhythm").GetString().ShouldBe("daily");
            gathered.GetProperty("accountPath").GetString().ShouldBe(AccountScreens.FollowsList);
            var notices = gathered.GetProperty("notices").EnumerateArray().ToList();
            notices.Select(n => n.GetProperty("kind").GetString()).Order(StringComparer.Ordinal)
                .ShouldBe([MessageKinds.CommentNoticeEmail, MessageKinds.FollowMemberEmail]);
            var member = notices.Single(n => n.GetProperty("kind").GetString() == MessageKinds.FollowMemberEmail).GetProperty("payload");
            member.GetProperty("textPath").GetString().ShouldBe($"/seimas/{Bill}#reason-{followed:D}");
            member.GetProperty("memberName").GetString().ShouldBe("Sekama Autorė");
            var comment = notices.Single(n => n.GetProperty("kind").GetString() == MessageKinds.CommentNoticeEmail).GetProperty("payload");
            comment.GetProperty("arguments").EnumerateArray().ShouldHaveSingleItem().GetProperty("path").GetString().ShouldBe($"/seimas/{Bill}#reason-{own:D}");
            HoldsNoText(gathered);

            await RunAtAsync(morning.AddMinutes(15));
            await RunAtAsync(morning.AddDays(1));
            (await MailAsync(email)).Count.ShouldBe(1, "each is told once");
        }
        finally
        {
            browsers.ForEach(b => b.Dispose());
            await Database.FreeBillAsync(Bill);
            await Database.ForgetEventsAsync(Bill);
        }
    }

    [Fact]
    public async Task Iš_karto_tells_both_in_the_runs_one_email()
    {
        var (email, _, _, _, browsers) = await ArrangeAsync(NoticeFrequencies.Immediate);
        try
        {
            await RunAtAsync(DateTimeOffset.UtcNow.AddSeconds(1));
            var (kind, gathered) = (await MailAsync(email)).ShouldHaveSingleItem("one email for the run");
            kind.ShouldBe(MessageKinds.GatheredNoticesEmail);
            gathered.GetProperty("rhythm").GetString().ShouldBe("immediate");
            gathered.GetProperty("notices").GetArrayLength().ShouldBe(2);
            HoldsNoText(gathered);
            await RunAtAsync(DateTimeOffset.UtcNow.AddMinutes(15));
            (await MailAsync(email)).Count.ShouldBe(1, "told once");
        }
        finally
        {
            browsers.ForEach(b => b.Dispose());
            await Database.FreeBillAsync(Bill);
            await Database.ForgetEventsAsync(Bill);
        }
    }

    [Fact]
    public async Task A_comment_hidden_before_the_morning_is_left_out_of_the_daily_email_and_the_followed_argument_still_comes_alone()
    {
        var (email, followed, _, comment, browsers) = await ArrangeAsync(NoticeFrequencies.Daily);
        try
        {
            var seen = DateTimeOffset.UtcNow.AddSeconds(1);
            await RunAtAsync(seen);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.bill_event WHERE kind = 'comment' AND bill_number = @b", ("b", Bill)))
                .ShouldBe(1, "the comment was seen and recorded");
            await (await ModeratorAsync()).Browser.ActOnCommentAsync(CommentRoutes.Hide, comment);

            var morning = VilniusTime.StartOf(VilniusTime.DateOf(seen).AddDays(1)).AddHours(8).AddMinutes(30);
            await RunAtAsync(morning);
            var (kind, alone) = (await MailAsync(email)).ShouldHaveSingleItem();
            kind.ShouldBe(MessageKinds.FollowMemberEmail, "one thing alone keeps its own email; the hidden comment is not told");
            alone.GetProperty("textPath").GetString().ShouldBe($"/seimas/{Bill}#reason-{followed:D}");
            HoldsNoText(alone);
        }
        finally
        {
            browsers.ForEach(b => b.Dispose());
            await Database.FreeBillAsync(Bill);
            await Database.ForgetEventsAsync(Bill);
        }
    }
}