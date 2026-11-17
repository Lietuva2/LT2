using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.5.4 (decided by the owner, 2026-11-17): the MP's own account takes no part, so the forms are not shown to it. The
/// islands learn it from the caller's own answers read in the browser: <c>GET /me</c> names the MP and the part (<c>mp</c> or
/// <c>office</c>), and <c>GET /me/bills</c> says <c>mpAccount</c> for the front page's rows. An office member and every other
/// member get false.
/// </summary>
public sealed class MpAccountTakesNoPartTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.TaxBill;

    [Fact]
    public async Task The_callers_own_answers_say_an_mps_own_account_and_nobody_else()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Ievaite);
        var (office, officeKey, _) = await Factory.OfficeMemberAsync(mp, "Biuro Narys Atsakymams");
        var (member, _) = await Factory.MemberAsync("Paprastas Narys");
        using var _o = office;
        using var _k = officeKey;
        using var _m = member;
        try
        {
            (await MyBillsAsync(mp.Browser)).MpAccount.ShouldBeTrue();
            (await mp.Browser.MeAsync()).Mp.ShouldNotBeNull().Part.ShouldBe("mp");

            (await MyBillsAsync(office)).MpAccount.ShouldBeFalse("an office member takes part in their own name");
            (await office.MeAsync()).Mp.ShouldNotBeNull().Part.ShouldBe("office");

            (await MyBillsAsync(member)).MpAccount.ShouldBeFalse();
            (await member.MeAsync()).Mp.ShouldBeNull();

            // The server refuses as before (the safety net), whatever the forms show.
            await (await mp.Browser.TakeSideAsync(Bill, "for")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.takes-no-part");
        }
        finally
        {
            await office.DeleteAccountAsync();
            await member.DeleteAccountAsync();
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    private static Task<MyBillsResponse> MyBillsAsync(TestBrowser browser) =>
        browser.GetJsonAsync<MyBillsResponse>($"{ApiRoutes.Me.Bills}?numbers={Uri.EscapeDataString(Bill)}");
}
