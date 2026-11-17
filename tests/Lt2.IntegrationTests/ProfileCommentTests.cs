using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Where public profiles (design 8.25) meet comments (8.23): the name beside a comment and among those who agreed carries a
/// profile id and a card only for a member with a public profile; a member's page lists their comments in their own voice with
/// their arguments, never one written as an organisation; following a member tells about a comment too, never its text.
/// </summary>
public sealed class ProfileCommentTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    private async Task RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IFollows>().RunAsync(DateTimeOffset.UtcNow.AddSeconds(1), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_name_beside_a_comment_and_in_sutinka_carries_a_profile_id_and_one_card_only_for_a_public_profile_and_no_account_id()
    {
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (open, openEmail, profileId) = await Factory.PublicMemberAsync("Atvira Komentatorė", "Mokytoja, rašau apie mokesčius.");
        var (plain, plainEmail) = await Factory.MemberAsync("Paprastas Narys");
        using var _o = open;
        using var _p = plain;
        try
        {
            var openComment = await open.CommentOkAsync("reason", bill.ReasonId, "Viešos profilio narės komentaras.");
            var plainComment = await plain.CommentOkAsync("reason", bill.ReasonId, "Paprasto nario komentaras.");
            (await plain.AgreeAsync(openComment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await open.AgreeAsync(plainComment)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

            using var visitor = NewBrowser();
            var raw = await visitor.Client.GetStringAsync(CommentRoutes.ThreadOf("reason", bill.ReasonId), TestContext.Current.CancellationToken);
            using var json = JsonDocument.Parse(raw);
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetGuid());
            items[openComment].GetProperty("author").GetProperty("profileId").GetString().ShouldBe(profileId);
            items[plainComment].GetProperty("author").GetProperty("profileId").ValueKind.ShouldBe(JsonValueKind.Null);
            var agreedWithPlain = items[plainComment].GetProperty("agreedBy").EnumerateArray().ShouldHaveSingleItem();
            (agreedWithPlain.GetProperty("name").GetString(), agreedWithPlain.GetProperty("profileId").GetString()).ShouldBe(("Atvira Komentatorė", profileId));
            items[openComment].GetProperty("agreedBy").EnumerateArray().ShouldHaveSingleItem().GetProperty("profileId").ValueKind.ShouldBe(JsonValueKind.Null);
            var card = json.RootElement.GetProperty("cards").EnumerateArray().ShouldHaveSingleItem("one card per person, however often they appear");
            card.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList().ShouldBe(["about", "name", "profileId"]); // no follower number in a card
            card.GetProperty("profileId").GetString().ShouldBe(profileId);

            foreach (var email in new[] { openEmail, plainEmail })
            {
                raw.ShouldNotContain((await Database.UserIdAsync(email)).ToString(), Case.Insensitive, "no account's id in a public answer");
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_members_page_lists_their_own_voice_comments_never_one_in_an_organisations_name_and_a_follower_hears_of_one_without_its_text()
    {
        var moderator = await ModeratorAsync();
        await Database.ForgetEventsAsync(Bill);
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        var (author, _, profileId) = await Factory.PublicMemberAsync("Komentuojanti Autorė");
        var (follower, followerEmail) = await Factory.MemberAsync("Sekanti Skaitytoja");
        using var _a = author;
        using var _f = follower;
        try
        {
            await follower.FollowMemberAsync(profileId);
            await Database.AgeFollowsAsync(followerEmail, TimeSpan.FromHours(1));
            const string Text = "Slaptas komentaro tekstas, kurio laiške būti neturi.";
            var comment = await author.CommentOkAsync("reason", bill.ReasonId, Text);

            // The representative has a public profile too: their comment as the organisation is not theirs on their page.
            var representativeProfile = (await bill.Representative.SavedProfileAsync("Atstovas.", isPublic: true)).ProfileId.ShouldNotBeNull();
            var asOrganisation = await bill.Representative.CommentOkAsync("statement", bill.StatementId, "Organizacijos vardu parašytas komentaras.");
            using (var thread = JsonDocument.Parse(await NewBrowser().Client.GetStringAsync(CommentRoutes.ThreadOf("statement", bill.StatementId), TestContext.Current.CancellationToken)))
            {
                var item = thread.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == asOrganisation);
                item.GetProperty("author").GetProperty("voice").GetString().ShouldBe("organisation", "the swept comment is the organisation's");
                item.GetProperty("author").GetProperty("profileId").ValueKind.ShouldBe(JsonValueKind.Null, "an organisation's voice never leads to the person");
                thread.RootElement.GetProperty("cards").GetArrayLength().ShouldBe(0, "no card of the representative");
                thread.RootElement.GetRawText().ShouldNotContain(representativeProfile, Case.Sensitive);
            }

            using var visitor = NewBrowser();
            var texts = await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(profileId, 10));
            var listed = texts.Items.ShouldHaveSingleItem();
            (listed.Id, listed.Kind, listed.On, listed.Side).ShouldBe((comment, MemberTextKinds.Comment, "reason", (string?)null));
            var page = WebUtility.HtmlDecode(await visitor.Client.GetStringAsync(ProfileRoutes.Page(profileId, "Komentuojanti Autorė"), TestContext.Current.CancellationToken));
            page.ShouldContain(Lt2.Api.Contracts.Comments.BillTextLinks.ToComment(Bill, comment), Case.Sensitive, "a link that lands on the comment (design 8.23.6)");
            page.ShouldContain("komentaras po argumentu");
            (await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(representativeProfile, 10))).Items.ShouldBeEmpty("a comment in an organisation's name is not the person's");

            await RunJobAsync();
            (await Database.EmailsAsync(MessageKinds.FollowMemberEmail, followerEmail)).ShouldBe(1);
            using (var payload = JsonDocument.Parse((await Database.LatestEmailAsync(MessageKinds.FollowMemberEmail, followerEmail))!))
            {
                payload.RootElement.GetProperty("what").GetString().ShouldBe(MemberTextKinds.Comment);
                payload.RootElement.GetProperty("textPath").GetString().ShouldBe(Lt2.Api.Contracts.Comments.BillTextLinks.ToComment(Bill, comment));
                payload.RootElement.GetRawText().ShouldNotContain("Slaptas");
            }

            // Hidden by a moderator: gone from the page.
            await moderator.Browser.ActOnCommentAsync(CommentRoutes.Hide, comment);
            (await visitor.GetJsonAsync<MemberTextsResponse>(ProfileRoutes.TextsOf(profileId, 10))).Items.ShouldBeEmpty();
            (await bill.Representative.SavedProfileAsync(null, isPublic: false)).ProfileId.ShouldBeNull();
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
            await Database.ForgetEventsAsync(Bill);
        }
    }
}
