using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3, "Projektai be aprašymo" (owner, 2026-11-25), over HTTP: active bills without a description, or with an AI
/// summary nobody has read, that at least one participant, published argument or follower pays attention to, the most
/// attention first. Moderators only; only the numbers a bill's page shows.
/// </summary>
public sealed class UndescribedBillsTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly string[] Bills = [AppealSteps.TaxBill, AppealSteps.SecondBill, AppealSteps.ThirdBill];

    private async Task FreeAsync()
    {
        foreach (var bill in Bills)
        {
            await Database.FreeBillAsync(bill);
            await Database.ExecuteAsync("DELETE FROM notify.follow WHERE kind = 'bill' AND key = @k", ("k", bill));
        }
    }

    [Fact]
    public async Task Only_an_active_moderator_in_a_passkey_session_sees_the_list()
    {
        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(AppealRoutes.Undescribed)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        using (var refused = await member.Client.GetAsync(AppealRoutes.Undescribed))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        var list = await (await ModeratorAsync()).Browser.GetJsonAsync<UndescribedBillsResponse>(AppealRoutes.Undescribed);
        list.Total.ShouldBeGreaterThanOrEqualTo(list.Bills.Count);
    }

    [Fact]
    public async Task Bills_people_pay_attention_to_without_a_description_are_listed_the_most_attention_first_until_described()
    {
        await FreeAsync();
        try
        {
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            var (follower, _) = await Factory.MemberAsync(consent: false);
            using var _f = follower;
            var moderator = (await ModeratorAsync()).Browser;

            async Task<UndescribedBillsResponse> ListAsync() => await moderator.GetJsonAsync<UndescribedBillsResponse>(AppealRoutes.Undescribed);

            // Nobody touched any of the three: none is listed, whatever its planned date.
            var untouched = await ListAsync();
            untouched.Bills.ShouldNotContain(b => Bills.Contains(b.Bill.Number));

            // A moderator's description, with a participant and an argument: described, so not listed.
            await Database.PrepareBillAsync(member, moderator, AppealSteps.ThirdBill);

            // A participant (a first side also follows the bill, design 8.11.2) on a bill without a description; a follower alone
            // on another.
            (await member.TakeSideAsync(AppealSteps.SecondBill, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            await follower.FollowBillAsync(AppealSteps.TaxBill);

            var list = await ListAsync();
            list.Bills.ShouldNotContain(b => b.Bill.Number == AppealSteps.ThirdBill);
            var second = list.Bills.Single(b => b.Bill.Number == AppealSteps.SecondBill);
            (second.Participants, second.Arguments, second.Followers, second.DescriptionSource).ShouldBe((1, 0, 1, (string?)null));
            second.Bill.Title.ShouldBe((await moderator.RecordAsync(AppealSteps.SecondBill)).Bill.Title);
            var tax = list.Bills.Single(b => b.Bill.Number == AppealSteps.TaxBill);
            (tax.Participants, tax.Arguments, tax.Followers).ShouldBe((0, 0, 1));
            Position(list, AppealSteps.SecondBill).ShouldBeLessThan(Position(list, AppealSteps.TaxBill), "2 before 1");
            list.Total.ShouldBeGreaterThanOrEqualTo(list.Bills.Count);

            // A side and a published argument on the followed bill: 1 + 1 + 2 puts it first.
            (await member.TakeSideAsync(AppealSteps.TaxBill, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await member.WriteReasonAsync(AppealSteps.TaxBill, "against", AppealSteps.AgainstReason)).StatusCode.ShouldBe(HttpStatusCode.Created);
            list = await ListAsync();
            tax = list.Bills.Single(b => b.Bill.Number == AppealSteps.TaxBill);
            (tax.Participants, tax.Arguments, tax.Followers).ShouldBe((1, 1, 2));
            Position(list, AppealSteps.TaxBill).ShouldBeLessThan(Position(list, AppealSteps.SecondBill), "4 before 2");

            // The page renders the same list on the server for a moderator.
            var page = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync("/moderavimas/be-aprasymo"));
            page.ShouldContain($"id=\"undescribed-counts-{AppealSteps.TaxBill}\">Dalyviai: 1 · Argumentai: 1 · Sekėjai: 2</span>");
            page.IndexOf($"id=\"undescribed-{AppealSteps.TaxBill}\"", StringComparison.Ordinal)
                .ShouldBeLessThan(page.IndexOf($"id=\"undescribed-{AppealSteps.SecondBill}\"", StringComparison.Ordinal));

            // An AI summary nobody has read keeps the bill on the list with its label; confirmed by a moderator, it leaves.
            await Database.ExecuteAsync(
                "UPDATE appeals.bill_record SET description = @t, description_source = 'ai', description_at = now(), plain_title = @p WHERE bill_number = @b",
                ("t", AppealSteps.Description), ("p", "Trečias pajamų mokesčio tarifas"), ("b", AppealSteps.SecondBill));
            second = (await ListAsync()).Bills.Single(b => b.Bill.Number == AppealSteps.SecondBill);
            (second.DescriptionSource, second.Bill.PlainTitle).ShouldBe(("ai", "Trečias pajamų mokesčio tarifas"));

            using (var confirmed = await moderator.PostAsync(AppealRoutes.ConfirmDescriptionOf(AppealSteps.SecondBill), new ModerationRequest()))
            {
                confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync());
            }

            (await ListAsync()).Bills.ShouldNotContain(b => b.Bill.Number == AppealSteps.SecondBill);
        }
        finally
        {
            await FreeAsync();
        }
    }

    private static int Position(UndescribedBillsResponse list, string number) =>
        list.Bills.Select((b, i) => (b, i)).Single(x => x.b.Bill.Number == number).i;
}
