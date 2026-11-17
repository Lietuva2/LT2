using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.2 (owner, 2026-11-17): a passkey session stays a passkey session. ASP.NET Core Identity checks the account's security
/// stamp every 30 minutes and rebuilds the session's principal from the account; the site's own claims (how the session was
/// signed in, the passkey, the session's key) are carried over and a renewal never changes what the session row was signed in
/// as. These tests run on an application that checks the stamp on every request, as every request does once 30 minutes have
/// passed. A changed stamp still ends the session.
/// </summary>
public sealed class SessionRefreshTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;

    private static readonly Lock Gate = new();
    private static StampFactory? _stamp;

    /// <summary>The application with the security stamp checked on every request.</summary>
    private StampFactory Stamp
    {
        get
        {
            lock (Gate)
            {
                return _stamp ??= new StampFactory(Database);
            }
        }
    }

    [Fact]
    public async Task An_mps_account_keeps_its_passkey_session_when_the_security_stamp_is_checked_and_writes_its_response()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Eglaite);
        try
        {
            using var browser = await SignedInWithPasskeyAsync(mp.Authenticator);
            for (var request = 0; request < 3; request++)
            {
                var me = await browser.MeAsync();
                me.PasskeySession.ShouldBeTrue($"request {request}: still a passkey session");
                me.Mp.ShouldNotBeNull().MayWrite.ShouldBeTrue();
            }

            await browser.RespondOkAsync(Bill, "for", "Balsuosiu už: pataisa padės šeimoms.");
            (await MethodOfCurrentSessionAsync(browser)).ShouldBe(("pop", true), "the session row keeps how it was signed in");
        }
        finally
        {
            await Database.ClearResponsesAsync(MpSteps.Eglaite.SeimasPersonId);
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    /// <summary>The report from the trial: an office member whose session "expired" could not write as the MP.</summary>
    [Fact]
    public async Task An_office_member_keeps_the_passkey_session_when_the_security_stamp_is_checked_and_writes_for_the_mp()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Sermuksnyte);
        var (office, officeKey, _) = await Factory.OfficeMemberAsync(mp, "Biuro Narė Bandymas");
        using var _o = office;
        using var _k = officeKey;
        try
        {
            using var browser = await SignedInWithPasskeyAsync(officeKey);
            for (var request = 0; request < 3; request++)
            {
                var me = await browser.MeAsync();
                me.PasskeySession.ShouldBeTrue($"request {request}: still a passkey session");
                me.Mp.ShouldNotBeNull().Part.ShouldBe("office");
                me.Mp!.MayWrite.ShouldBeTrue();
            }

            await browser.RespondOkAsync(Bill, "against", "Biuras: balsuosime prieš, nes trūksta skaičiavimų.");
            using (var argument = await browser.WriteReasonAsync(Bill, "for", "Biuro narė savo vardu mano, kad pataisa naudinga šeimoms."))
            {
                argument.IsSuccessStatusCode.ShouldBeTrue(await argument.Content.ReadAsStringAsync());
            }

            (await MethodOfCurrentSessionAsync(browser)).ShouldBe(("pop", true));
        }
        finally
        {
            await Database.ClearResponsesAsync(MpSteps.Sermuksnyte.SeimasPersonId);
            await office.DeleteAccountAsync();
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_staff_session_stays_a_staff_session_when_the_security_stamp_is_checked()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var browser = await SignedInWithPasskeyAsync(admin.Authenticator);

        for (var request = 0; request < 3; request++)
        {
            (await browser.MeAsync()).ActiveStaffRoles.ShouldBe(["administrator"], $"request {request}");
            using var settings = await browser.Client.GetAsync(ApiRoutes.Admin.Settings);
            settings.StatusCode.ShouldBe(HttpStatusCode.OK, $"request {request}: a staff screen's answer");
        }

        (await MethodOfCurrentSessionAsync(browser)).ShouldBe(("pop", true));
    }

    [Fact]
    public async Task A_changed_security_stamp_still_ends_the_session()
    {
        var (setup, email) = await Factory.MemberAsync("Stampo Bandymas");
        using var _s = setup;
        using var key = new SoftwareAuthenticator();
        (await setup.AddPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var browser = await SignedInWithPasskeyAsync(key);
        var current = (await SessionsAsync(browser)).Single(s => s.IsCurrent);
        (await browser.MeAsync()).PasskeySession.ShouldBeTrue();

        await Database.ExecuteAsync(
            "UPDATE identity.\"user\" SET security_stamp = 'pakeistas-bandymo-antspaudas' WHERE lower(email) = lower(@e)", ("e", email));

        using (var me = await browser.Client.GetAsync(ApiRoutes.Me.Root))
        {
            me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the session ended with the changed stamp");
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.session WHERE id = @id", ("id", current.Id))).ShouldBe(0);
        await Database.ForgetAccountAsync(email);
    }

    /// <summary>A browser of the stamp-checking application signed in with the authenticator's passkey.</summary>
    private async Task<TestBrowser> SignedInWithPasskeyAsync(SoftwareAuthenticator authenticator)
    {
        var browser = Stamp.NewBrowser();
        using var signIn = await browser.SignInWithPasskeyAsync(authenticator);
        signIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signIn.Content.ReadAsStringAsync());
        return browser;
    }

    private static async Task<List<SessionResponse>> SessionsAsync(TestBrowser browser) =>
        (await browser.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!;

    /// <summary>The current session row's way of signing in and whether it holds a passkey.</summary>
    private async Task<(string? Method, bool Passkey)> MethodOfCurrentSessionAsync(TestBrowser browser)
    {
        var id = (await SessionsAsync(browser)).Single(s => s.IsCurrent).Id;
        var method = await Database.ScalarAsync<string>("SELECT method FROM identity.session WHERE id = @id", ("id", id));
        var passkey = await Database.ScalarAsync<bool>("SELECT passkey_id IS NOT NULL FROM identity.session WHERE id = @id", ("id", id));
        return (method, passkey);
    }

    /// <summary>The application with the security stamp checked on every request, as it is once 30 minutes have passed.</summary>
    private sealed class StampFactory(DatabaseFixture database) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero));
        }
    }
}
