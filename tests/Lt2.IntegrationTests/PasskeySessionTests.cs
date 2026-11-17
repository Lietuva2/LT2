using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.2 (owner, 2026-11-17): "Patvirtinti prieigos raktu" in place. <c>POST /me/passkey-session</c> makes the signed-in
/// account's session a passkey session with any of its own passkeys, under a new key (the old row is deleted), the passkey just
/// confirmed, audited without an address and without an email; a staff session too when it is the staff passkey. Another
/// account's passkey is refused with its own error and changes nothing.
/// </summary>
public sealed class PasskeySessionTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.SecondBill;

    [Fact]
    public async Task A_code_session_becomes_a_passkey_session_with_the_accounts_own_passkey_under_a_new_key()
    {
        var (browser, email, key) = await MemberWithPasskeyAsync("Raktas Bandymas");
        using var _b = browser;
        using var _k = key;
        using var code = await Factory.CodeSessionAsync(email);
        var before = (await SessionsAsync(code)).Single(s => s.IsCurrent);
        before.Method.ShouldBe("otp");
        (await code.MeAsync()).PasskeySession.ShouldBeFalse();
        var notices = await NoticesAsync(email);

        using (var started = await code.PasskeySessionAsync(key))
        {
            started.StatusCode.ShouldBe(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());
        }

        var me = await code.MeAsync();
        me.Email.ShouldBe(email);
        me.PasskeySession.ShouldBeTrue();
        var after = (await SessionsAsync(code)).Single(s => s.IsCurrent);
        after.Id.ShouldNotBe(before.Id, "a new session key on the change of privilege");
        after.Method.ShouldBe("pop");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.session WHERE id = @id", ("id", before.Id))).ShouldBe(0);
        (await AuditCountAsync(email, "passkey.session-started")).ShouldBe(1);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'passkey.session-started' AND (details::text LIKE '%@%' OR target LIKE '%@%')"))
            .ShouldBe(0, "no addresses in the audit log");
        (await NoticesAsync(email)).ShouldBe(notices, "no email of its own");
        await browser.DeleteAccountAsync();
    }

    /// <summary>The MP's account signed in by a code is refused writing, then confirms its passkey in place and writes.</summary>
    [Fact]
    public async Task An_mps_code_session_confirms_its_passkey_in_place_and_then_writes_its_response()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        try
        {
            using var code = await Factory.CodeSessionAsync(mp.Email);
            (await code.MeAsync()).Mp.ShouldNotBeNull().MayWrite.ShouldBeFalse();
            await (await code.RespondAsync(Bill, "for", "Balsuosiu už.")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");

            using (var started = await code.PasskeySessionAsync(mp.Authenticator))
            {
                started.StatusCode.ShouldBe(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());
            }

            (await code.MeAsync()).Mp!.MayWrite.ShouldBeTrue();
            await code.RespondOkAsync(Bill, "for", "Balsuosiu už.");
        }
        finally
        {
            await Database.ClearResponsesAsync(MpSteps.Kadagyte.SeimasPersonId);
            await mp.Browser.DeleteAccountAsync();
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task Another_accounts_passkey_is_refused_and_the_session_stays_as_it_was()
    {
        var (mine, email, _) = await MemberWithPasskeyAsync("Mano Paskyra");
        var (theirs, _, theirKey) = await MemberWithPasskeyAsync("Kito Paskyra");
        using var _m = mine;
        using var _t = theirs;
        using var _k = theirKey;
        using var code = await Factory.CodeSessionAsync(email);
        var before = (await SessionsAsync(code)).Single(s => s.IsCurrent);

        await (await code.PasskeySessionAsync(theirKey)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.other-account");

        var me = await code.MeAsync();
        me.Email.ShouldBe(email, "still the same account");
        me.PasskeySession.ShouldBeFalse();
        var after = (await SessionsAsync(code)).Single(s => s.IsCurrent);
        after.Id.ShouldBe(before.Id);
        after.Method.ShouldBe("otp");
        (await AuditCountAsync(email, "passkey.session-started")).ShouldBe(0);
        await mine.DeleteAccountAsync();
        await theirs.DeleteAccountAsync();
    }

    [Fact]
    public async Task On_a_staff_account_another_passkey_gives_a_passkey_session_and_only_the_staff_passkey_a_staff_session()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var extra = new SoftwareAuthenticator();
        (await admin.Browser.ConfirmPasskeyAsync(admin.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var added = await admin.Browser.AddPasskeyAsync(extra))
        {
            added.StatusCode.ShouldBe(HttpStatusCode.NoContent, await added.Content.ReadAsStringAsync());
        }

        using var code = await Factory.CodeSessionAsync(admin.Email);
        (await code.PasskeySessionAsync(extra)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var me = await code.MeAsync();
        me.PasskeySession.ShouldBeTrue();
        me.ActiveStaffRoles.ShouldBeEmpty("not the staff passkey");
        (await code.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await code.PasskeySessionAsync(admin.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await code.MeAsync()).ActiveStaffRoles.ShouldBe(["administrator"]);
        (await code.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AuditCountAsync(admin.Email, "staff.session-started")).ShouldBe(1);
        (await AuditCountAsync(admin.Email, "passkey.session-started")).ShouldBe(1);
    }

    [Fact]
    public async Task A_code_session_of_an_account_without_a_passkey_and_a_visitor_are_refused()
    {
        var (member, email) = await Factory.MemberAsync("Be Rakto");
        using var _m = member;
        using (var request = await member.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions))
        {
            request.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await (await member.PostAsync(ApiRoutes.Me.PasskeySession, new PasskeyCredentialRequest { Credential = "{\"id\":\"x\"}" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.invalid");
        (await member.MeAsync()).PasskeySession.ShouldBeFalse();
        (await AuditCountAsync(email, "passkey.session-started")).ShouldBe(0);

        using var visitor = NewBrowser();
        using var anonymous = await visitor.PostAsync(ApiRoutes.Me.PasskeySession, new PasskeyCredentialRequest { Credential = "{\"id\":\"x\"}" });
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await member.DeleteAccountAsync();
    }

    /// <summary>A member with a passkey, signed in by code in the browser returned.</summary>
    private async Task<(TestBrowser Browser, string Email, SoftwareAuthenticator Key)> MemberWithPasskeyAsync(string name)
    {
        var (browser, email) = await Factory.MemberAsync(name);
        var key = new SoftwareAuthenticator();
        using var added = await browser.AddPasskeyAsync(key);
        added.StatusCode.ShouldBe(HttpStatusCode.NoContent, await added.Content.ReadAsStringAsync());
        return (browser, email, key);
    }

    private static async Task<List<SessionResponse>> SessionsAsync(TestBrowser browser) =>
        (await browser.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!;

    private Task<long> NoticesAsync(string email) =>
        Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = 'email.account-notice' AND payload->>'to' = @e", ("e", email));

    private Task<long> AuditCountAsync(string email, string action) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = @a AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e)",
            ("a", action),
            ("e", email));
}
