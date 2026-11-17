using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.12.5 (owner, 2026-11-17): <c>/projektai</c> sorted by the latest activity, "Naujausia veikla". Each kind of
/// activity moves its bill up: a side taken, changed or withdrawn, an argument published, a comment written, an
/// organisation's statement made public and an MP's response made public. One time per bill; nothing per person.
/// </summary>
public sealed class LastActivityTests(DatabaseFixture database) : ApiTest(database)
{
    private const string A = AppealSteps.ThirdBill;
    private const string B = AppealSteps.SecondBill;

    private async Task<string> FirstAsync()
    {
        using var visitor = NewBrowser();
        var list = await visitor.GetJsonAsync<BillListResponse>(AppealRoutes.AppealsList(null, false, false, null, "latest"));
        var ours = list.Items.Where(i => i.Bill.Number is A or B).ToList();
        ours.Count.ShouldBe(2, "both bills have activity, so both are listed before any without");
        list.Items.Select(i => i.Bill.Number).ToList().IndexOf(ours[0].Bill.Number).ShouldBeLessThan(2, "the latest activity is first");
        ours.ShouldAllBe(i => i.LastActivityAt != null);
        return ours[0].Bill.Number;
    }

    [Fact]
    public async Task Each_kind_of_activity_moves_its_bill_to_the_top_of_the_latest_activity_order_and_nothing_per_person_is_kept()
    {
        var moderator = (await ModeratorAsync()).Browser;
        await Database.FreeBillAsync(A);
        await Database.FreeBillAsync(B);
        using var mp = await Factory.MpAccountAsync(MpSteps.Gluosnis);
        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            await Database.PrepareBillAsync(initiator, moderator, A, "against");
            await Database.PrepareBillAsync(initiator, moderator, B, "for");
            (await FirstAsync()).ShouldBe(B, "B's side and argument came last");

            // A side taken.
            var (member, _) = await Factory.MemberAsync();
            using var _m = member;
            (await member.TakeSideAsync(A, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await FirstAsync()).ShouldBe(A, "a side taken");
            (await member.TakeSideAsync(B, "for")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await FirstAsync()).ShouldBe(B);

            // A side changed, then withdrawn.
            (await member.TakeSideAsync(A, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await FirstAsync()).ShouldBe(A, "a side changed");
            (await member.SendAsync(HttpMethod.Delete, AppealRoutes.PositionOf(B))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await FirstAsync()).ShouldBe(B, "a side withdrawn");

            // The same side again changes nothing.
            (await member.TakeSideAsync(A, "against")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await FirstAsync()).ShouldBe(B, "taking the same side again is no activity");

            // An argument published.
            (await member.WriteReasonAsync(A, "for", "Argumentas, kuris pakelia projektą sąraše.")).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await FirstAsync()).ShouldBe(A, "an argument published");

            // A comment written.
            var reasonOnB = (await member.RecordAsync(B)).Reasons[0].Id;
            await member.CommentOkAsync("reason", reasonOnB, "Komentaras po argumentu.");
            (await FirstAsync()).ShouldBe(B, "a comment written");

            // An organisation's statement made public.
            var (representative, _) = await Factory.MemberAsync();
            using var _r = representative;
            var organisation = await representative.RegisterOrganisationAsync(OrganisationSteps.NewName("Veiklos asociacija"));
            await representative.PublishStatementAsync(A, organisation.Slug, "for", "Asociacija pritaria pataisai.");
            (await FirstAsync()).ShouldBe(A, "a statement made public");

            // An MP's response made public.
            await mp.Browser.RespondOkAsync(B, "for", "Balsuosiu už.");
            (await FirstAsync()).ShouldBe(B, "a response made public");

            // The page: the third order on the ground above the list, and each row's last activity in that order only.
            using var reader = NewBrowser();
            var page = WebUtility.HtmlDecode(await reader.Client.GetStringAsync("/projektai?rikiuoti=naujausia"));
            page.ShouldContain("id=\"sort-naujausia\"");
            page.ShouldContain($"id=\"last-{B}\"");
            System.Text.RegularExpressions.Regex.IsMatch(page, $"id=\"last-{B}\"[^>]*>Paskutinė veikla 20\\d\\d-\\d\\d-\\d\\d \\d\\d:\\d\\d<").ShouldBeTrue();
            WebUtility.HtmlDecode(await reader.Client.GetStringAsync("/projektai")).ShouldNotContain("Paskutinė veikla");

            // One time per bill: the only column of its kind, on the record; no table or column for who acted.
            (await Database.ScalarAsync<string>(
                "SELECT string_agg(table_name || '.' || column_name || ':' || data_type, ',') FROM information_schema.columns WHERE table_schema = 'appeals' AND column_name LIKE '%activity%'"))
                .ShouldBe("bill_record.last_activity_at:timestamp with time zone");
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
            await Database.ExecuteAsync("DELETE FROM notify.outbox WHERE lower(payload->>'to') = lower(@a)", ("a", mp.Email));
            await Database.ClearStatementsAsync(A);
            await Database.FreeBillAsync(A);
            await Database.FreeBillAsync(B);
        }
    }
}
