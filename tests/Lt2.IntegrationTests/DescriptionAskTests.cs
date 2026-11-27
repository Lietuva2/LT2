using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.13.2, "Prašau aprašymo" (owner, 2026-11-27), over HTTP: a signed-in member asks for the description of any bill
/// that shows none, open or finished, once; only the count is public; the asked bills lead the moderators' "Projektai be
/// aprašymo", a finished one too; a person's description clears the asks; the asks are the member's own data.
/// </summary>
public sealed class DescriptionAskTests(DatabaseFixture database) : ApiTest(database)
{
    /// <summary>A final vote on <see cref="AppealSteps.ThirdBill"/> for these tests, as <c>AppealClosingTests</c> makes one.</summary>
    private const long FinalVote = 99199619;

    private static readonly string[] Bills = [AppealSteps.TaxBill, AppealSteps.SecondBill, AppealSteps.ThirdBill];

    private async Task FreeAsync()
    {
        await Database.ExecuteAsync("DELETE FROM parliament.vote WHERE id = @id", ("id", FinalVote));
        foreach (var bill in Bills)
        {
            await Database.FreeBillAsync(bill);
            await Database.UnfollowBillAsync(bill);
        }
    }

    private static async Task<DescriptionAskResponse> StateAsync(TestBrowser browser, string bill) =>
        await browser.GetJsonAsync<DescriptionAskResponse>(AppealRoutes.DescriptionAskOf(bill));

    private static async Task AskAsync(TestBrowser browser, string bill)
    {
        using var asked = await browser.SendAsync(HttpMethod.Put, AppealRoutes.DescriptionAskOf(bill));
        asked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await asked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_member_asks_once_takes_it_back_and_everyone_sees_only_the_count()
    {
        await FreeAsync();
        try
        {
            using var stranger = NewBrowser();
            (await StateAsync(stranger, AppealSteps.SecondBill)).ShouldBe(new DescriptionAskResponse(0, null, true));
            using (var refused = await stranger.SendAsync(HttpMethod.Put, AppealRoutes.DescriptionAskOf(AppealSteps.SecondBill)))
            {
                refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            using (var unknown = await stranger.Client.GetAsync(AppealRoutes.DescriptionAskOf("XVP-9999")))
            {
                await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "bill.not-found");
            }

            // Asking needs no consent to positions: it tells no side.
            var (member, _) = await Factory.MemberAsync(consent: false);
            using var _m = member;
            var (other, _) = await Factory.MemberAsync();
            using var _o = other;
            (await StateAsync(member, AppealSteps.SecondBill)).ShouldBe(new DescriptionAskResponse(0, false, true));
            await AskAsync(member, AppealSteps.SecondBill);
            await AskAsync(member, AppealSteps.SecondBill.ToLowerInvariant());
            (await StateAsync(member, AppealSteps.SecondBill)).ShouldBe(new DescriptionAskResponse(1, true, true), "asking again changes nothing");
            await AskAsync(other, AppealSteps.SecondBill);
            (await StateAsync(stranger, AppealSteps.SecondBill)).ShouldBe(new DescriptionAskResponse(2, null, true));

            using (var withdrawn = await member.SendAsync(HttpMethod.Delete, AppealRoutes.DescriptionAskOf(AppealSteps.SecondBill)))
            {
                withdrawn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            (await StateAsync(member, AppealSteps.SecondBill)).ShouldBe(new DescriptionAskResponse(1, false, true));
            (await StateAsync(other, AppealSteps.SecondBill)).ShouldBe(new DescriptionAskResponse(1, true, true));

            // The bill's page: the visitor's way in is a link to sign in, in the served HTML, so it works without script.
            var page = WebUtility.HtmlDecode(await stranger.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill));
            page.ShouldContain("id=\"description-ask-sign-in\"");
            page.ShouldContain("Aprašymo paprašė: 1");
        }
        finally
        {
            await FreeAsync();
        }
    }

    [Fact]
    public async Task Asked_bills_lead_the_moderators_list_a_finished_one_too_until_a_person_describes_them()
    {
        await FreeAsync();
        try
        {
            var moderator = (await ModeratorAsync()).Browser;
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            var (other, _) = await Factory.MemberAsync(consent: false);
            using var _o = other;
            async Task<UndescribedBillsResponse> ListAsync() => await moderator.GetJsonAsync<UndescribedBillsResponse>(AppealRoutes.Undescribed);

            // A followed bill is on the list by attention; a bill nobody touched only once asked about; a finished bill nobody
            // took part in, only by the asks.
            await other.FollowBillAsync(AppealSteps.TaxBill);
            await Database.ExecuteAsync(
                """
                INSERT INTO parliament.vote (id, agenda_item_id, bill_id, held_at, wording, type, kind, stage, is_consensus, has_result, "for", against, abstain, voted, seats)
                SELECT @id, agenda_item_id, bill_id, held_at + interval '7 days', 'Priėmimas', type, 'StageResult', 'Adoption', false, true, "for", against, abstain, voted, seats
                FROM parliament.vote WHERE id = 99100102
                """,
                ("id", FinalVote));
            var before = await ListAsync();
            before.Bills.ShouldNotContain(b => b.Bill.Number == AppealSteps.SecondBill || b.Bill.Number == AppealSteps.ThirdBill);
            before.Bills.ShouldContain(b => b.Bill.Number == AppealSteps.TaxBill);

            (await StateAsync(member, AppealSteps.ThirdBill)).Open.ShouldBeTrue("a finished bill without a description can be asked about");
            await AskAsync(member, AppealSteps.ThirdBill);
            await AskAsync(member, AppealSteps.SecondBill);
            await AskAsync(other, AppealSteps.SecondBill);

            var list = await ListAsync();
            var second = list.Bills.Single(b => b.Bill.Number == AppealSteps.SecondBill);
            (second.Asks, second.Finished, second.Participants, second.Followers).ShouldBe((2, false, 0, 0));
            var third = list.Bills.Single(b => b.Bill.Number == AppealSteps.ThirdBill);
            (third.Asks, third.Finished).ShouldBe((1, true));
            list.Bills.Single(b => b.Bill.Number == AppealSteps.TaxBill).Asks.ShouldBe(0);
            Position(list, AppealSteps.SecondBill).ShouldBe(0, "the most asks first");
            Position(list, AppealSteps.ThirdBill).ShouldBeLessThan(Position(list, AppealSteps.TaxBill), "an ask before attention alone");

            var tab = WebUtility.HtmlDecode(await moderator.Client.GetStringAsync("/moderavimas?skiltis=be-aprasymo"));
            tab.ShouldContain($"id=\"undescribed-asks-{AppealSteps.SecondBill}\">Aprašymo paprašė: 2</span>");
            tab.ShouldContain($"id=\"undescribed-finished-{AppealSteps.ThirdBill}\"");

            // The member's own data: in the export, and gone with the account.
            using (var document = JsonDocument.Parse(await member.Client.GetStringAsync(ApiRoutes.Me.Export)))
            {
                document.RootElement.GetProperty("appeals").GetProperty("descriptionAsks").EnumerateArray()
                    .Select(a => a.GetProperty("bill").GetString()).Order().ShouldBe([AppealSteps.SecondBill, AppealSteps.ThirdBill]);
            }

            // A moderator's description answers the asks: they go, asking is closed, the bill leaves the list.
            using (var described = await moderator.SendAsync(
                HttpMethod.Put, AppealRoutes.BillDescriptionOf(AppealSteps.SecondBill),
                new ModeratorDescriptionRequest { Description = AppealSteps.Description, Statement = "Aprašyta, nes nariai paprašė." }))
            {
                described.StatusCode.ShouldBe(HttpStatusCode.NoContent, await described.Content.ReadAsStringAsync());
            }

            (await StateAsync(member, AppealSteps.SecondBill)).ShouldBe(new DescriptionAskResponse(0, false, false));
            using (var late = await member.SendAsync(HttpMethod.Put, AppealRoutes.DescriptionAskOf(AppealSteps.SecondBill)))
            {
                await late.ShouldBeProblemAsync(HttpStatusCode.Conflict, "description-ask.not-wanted");
            }

            WebUtility.HtmlDecode(await member.Client.GetStringAsync("/seimas/" + AppealSteps.SecondBill)).ShouldNotContain("id=\"description-ask\"");
            (await ListAsync()).Bills.ShouldNotContain(b => b.Bill.Number == AppealSteps.SecondBill);

            await member.DeleteAccountAsync();
            (await Database.ScalarAsync<long>("SELECT count(*) FROM appeals.description_ask WHERE bill_number = @b", ("b", AppealSteps.ThirdBill))).ShouldBe(0);
            (await ListAsync()).Bills.ShouldNotContain(b => b.Bill.Number == AppealSteps.ThirdBill);
        }
        finally
        {
            await FreeAsync();
        }
    }

    private static int Position(UndescribedBillsResponse list, string number) =>
        list.Bills.Select((b, i) => (b, i)).Single(x => x.b.Bill.Number == number).i;
}
