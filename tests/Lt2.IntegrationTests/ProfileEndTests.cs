using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Where a public profile ends (design 8.25.1): taking over an MP's profile switches it off as the switch does (8.5.4: the MP's
/// own account has none); nothing waiting in the outbox about the member goes after a switch-off or the account's deletion;
/// the 410 page is true for both.
/// </summary>
public sealed class ProfileEndTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.SecondBill;

    private Task<long> FollowsOfAsync(string profileId) =>
        Database.ScalarAsync<long>("SELECT count(*) FROM notify.follow WHERE kind = 'member' AND key = @k", ("k", profileId))!;

    private async Task<List<(string Kind, JsonElement Payload)>> MailAsync(string email) =>
        (await Database.InboxAsync(email)).Where(m => m.Kind is MessageKinds.FollowMemberEmail or MessageKinds.GatheredNoticesEmail).ToList();

    [Fact]
    public async Task Taking_over_an_mps_profile_switches_the_accounts_public_profile_off_with_its_follows_and_consent()
    {
        var official = await Factory.OfficialMemberAsync(MpSteps.Klevinskas);
        var deleted = false;
        var (follower, _) = await Factory.MemberAsync("Sekanti Narė");
        using var _f = follower;
        try
        {
            var id = (await official.SavedProfileAsync("Kandidatas.", isPublic: true)).ProfileId.ShouldNotBeNull();
            await follower.FollowMemberAsync(id);
            (await FollowsOfAsync(id)).ShouldBe(1, "the follow exists before");

            var slug = (await Database.SlugAsync(MpSteps.Klevinskas))!;
            using (var claimed = await official.ClaimAsync(slug))
            {
                claimed.StatusCode.ShouldBe(HttpStatusCode.Accepted, await claimed.Content.ReadAsStringAsync());
            }

            using (var taken = await official.TakeOverAsync(new SoftwareAuthenticator(), await Database.TakeoverTokenAsync(MpSteps.Klevinskas.Address)))
            {
                taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync());
            }

            (await official.GetJsonAsync<MyProfileResponse>(ProfileRoutes.Mine)).ProfileId.ShouldBeNull();
            (await FollowsOfAsync(id)).ShouldBe(0);
            using var visitor = NewBrowser();
            using (var page = await visitor.Client.GetAsync("/nariai/" + id, TestContext.Current.CancellationToken))
            {
                page.StatusCode.ShouldBe(HttpStatusCode.Gone);
            }

            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE lower(u.email) = lower(@e) AND c.kind = 'public-profile' AND c.withdrawn_at IS NULL",
                ("e", MpSteps.Klevinskas.Address))).ShouldBe(0, "the consent is withdrawn");
            await official.DeleteAccountAsync();
            deleted = true;
        }
        finally
        {
            if (!deleted)
            {
                await Database.ForgetAccountAsync(MpSteps.Klevinskas.Address);
            }

            official.Dispose();
        }
    }

    [Fact]
    public async Task Emails_waiting_about_a_member_are_dropped_or_cut_when_the_profile_goes_off_or_the_account_is_deleted()
    {
        await Database.FreeBillAsync(Bill);
        await Database.ForgetEventsAsync(Bill);
        var (first, _, firstId) = await Factory.PublicMemberAsync("Pirma Autorė");
        var (second, _, secondId) = await Factory.PublicMemberAsync("Antra Autorė");
        var (alone, aloneEmail) = await Factory.MemberAsync("Vieną Sekanti");
        var (both, bothEmail) = await Factory.MemberAsync("Abi Sekanti");
        using var _a = alone;
        using var _b = both;
        using var _s = second;
        var secondDeleted = false;
        try
        {
            await alone.FollowMemberAsync(firstId);
            await both.FollowMemberAsync(firstId);
            await both.FollowMemberAsync(secondId);
            await Database.AgeFollowsAsync(aloneEmail, TimeSpan.FromHours(1));
            await Database.AgeFollowsAsync(bothEmail, TimeSpan.FromHours(1));
            (await first.WriteReasonAsync(Bill, "for", "Pirmos autorės argumentas apie pataisą.")).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await second.WriteReasonAsync(Bill, "against", "Antros autorės argumentas apie pataisą.")).StatusCode.ShouldBe(HttpStatusCode.Created);
            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(DateTimeOffset.UtcNow.AddSeconds(1), TestContext.Current.CancellationToken);
            }

            (await MailAsync(aloneEmail)).Select(m => m.Kind).ShouldBe([MessageKinds.FollowMemberEmail], "waiting: the first author's line alone");
            var gathered = (await MailAsync(bothEmail)).ShouldHaveSingleItem();
            gathered.Kind.ShouldBe(MessageKinds.GatheredNoticesEmail);
            gathered.Payload.GetProperty("notices").GetArrayLength().ShouldBe(2);

            (await first.SavedProfileAsync(null, isPublic: false)).ProfileId.ShouldBeNull();
            (await MailAsync(aloneEmail)).ShouldBeEmpty("the email about a switched-off profile is dropped");
            var cut = (await MailAsync(bothEmail)).ShouldHaveSingleItem("the gathered email stays with the other member's line");
            var left = cut.Payload.GetProperty("notices").EnumerateArray().ShouldHaveSingleItem();
            left.GetProperty("payload").GetProperty("profilePath").GetString().ShouldEndWith("-" + secondId);
            cut.Payload.GetRawText().ShouldNotContain(firstId, Case.Sensitive);

            await second.DeleteAccountAsync();
            secondDeleted = true;
            (await MailAsync(bothEmail)).ShouldBeEmpty("nothing is left of the gathered email after the deletion");

            // The 410 page is true for a deleted account too.
            using var visitor = NewBrowser();
            using var gone = await visitor.Client.GetAsync("/nariai/" + secondId, TestContext.Current.CancellationToken);
            gone.StatusCode.ShouldBe(HttpStatusCode.Gone);
            WebUtility.HtmlDecode(await gone.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("arba ištrynė paskyrą");
        }
        finally
        {
            first.Dispose();
            if (!secondDeleted)
            {
                await second.DeleteAccountAsync();
            }

            await Database.FreeBillAsync(Bill);
            await Database.ForgetEventsAsync(Bill);
        }
    }
}
