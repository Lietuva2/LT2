using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Mps;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.5.3 (owner, 2026-11-17): an office invitation is accepted on its one page. A member signed in by code brings the
/// passkey in the same request, a new one or one the account has, and the session becomes a passkey session; a passkey session
/// accepts as it is. Every rule of 8.5.3 stands: nothing is stored for a wrong token or an account already linked, a linked
/// account never gets a passkey by code, another account's passkey proves nothing, and the MP's account is told.
/// </summary>
public sealed class OfficeInvitationTests(DatabaseFixture database) : ApiTest(database)
{
    private static async Task<string> TokenAsync(MpAccount mp)
    {
        var invitation = await mp.Browser.PostJsonAsync<MpOfficeInvitationResponse>(MpRoutes.OfficeInvitations, new { });
        return Uri.UnescapeDataString(invitation.Path[(invitation.Path.IndexOf('#', StringComparison.Ordinal) + 1)..]);
    }

    private static async Task<string> CreationOptionsAsync(TestBrowser browser)
    {
        using var creation = await browser.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions);
        var body = await creation.Content.ReadAsStringAsync();
        creation.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    private static async Task<string> RequestOptionsAsync(TestBrowser browser)
    {
        using var request = await browser.PostAsync(ApiRoutes.Auth.PasskeyRequestOptions);
        var body = await request.Content.ReadAsStringAsync();
        request.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return body;
    }

    private async Task<int> NoticesToAsync(string address, string kind) =>
        (int)await Database.ScalarAsync<long>(
            "SELECT count(*) FROM notify.outbox WHERE kind = @k AND lower(payload->>'to') = lower(@to) AND payload->>'notice' = @n",
            ("k", MessageKinds.AccountNoticeEmail), ("to", address), ("n", kind));

    [Fact]
    public async Task A_member_signed_in_by_code_without_a_passkey_makes_one_and_accepts_in_one_request()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var (member, email) = await Factory.MemberAsync("Biuro Narė");
        using var _m = member;
        using var key = new SoftwareAuthenticator();
        var officeNotices = await NoticesToAsync(mp.Email, AccountNotices.MpOfficeAdded);
        try
        {
            var token = await TokenAsync(mp);
            var before = await member.MeAsync();
            (before.PasskeySession, before.PasskeyCount).ShouldBe((false, 0), "a code session of an account without a passkey");

            // Nothing brought: refused as before.
            await (await member.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token }))
                .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");

            // A wrong token with a passkey made for it: refused before the passkey is stored.
            var wrong = key.Create(await CreationOptionsAsync(member));
            await (await member.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", NewPasskey = wrong }))
                .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "mp.link-invalid");
            (await member.MeAsync()).PasskeyCount.ShouldBe(0, "nothing stored for a wrong token");

            // The token with a passkey made now: one request stores it, accepts, and makes the session a passkey session.
            var joined = await member.PostJsonAsync<MpLinkResponse>(
                MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token, NewPasskey = key.Create(await CreationOptionsAsync(member)) });
            joined.ShouldBe(new MpLinkResponse(mp.Slug, "Aurelija", "Kadagytė", "office", true));
            var after = await member.MeAsync();
            (after.PasskeySession, after.PasskeyCount).ShouldBe((true, 1));
            after.Mp.ShouldBe(joined);
            (await Database.OpenLinksAsync(MpSteps.Kadagyte.SeimasPersonId, "office")).ShouldBe(1);

            // One session for this browser, the passkey's (the code session's row went with the new key).
            using (var sessions = await member.Client.GetAsync(ApiRoutes.Me.Sessions))
            {
                var list = JsonDocument.Parse(await sessions.Content.ReadAsStringAsync()).RootElement;
                list.GetArrayLength().ShouldBe(1, await sessions.Content.ReadAsStringAsync());
            }

            // The passkey is the account's: it signs in elsewhere. The account is told a passkey was added; the MP's account is told.
            using (var elsewhere = NewBrowser())
            using (var signedIn = await elsewhere.SignInWithPasskeyAsync(key))
            {
                signedIn.StatusCode.ShouldBe(HttpStatusCode.NoContent, await signedIn.Content.ReadAsStringAsync());
            }

            (await NoticesToAsync(email, AccountNotices.PasskeyAdded)).ShouldBe(1);
            (await NoticesToAsync(mp.Email, AccountNotices.MpOfficeAdded)).ShouldBe(officeNotices + 1);
        }
        finally
        {
            await member.DeleteAccountAsync();
            await mp.Browser.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task A_member_signed_in_by_code_with_a_passkey_uses_it_and_accepts_and_another_accounts_passkey_proves_nothing()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Ievaite);
        var (passkeyBrowser, key, email) = await Factory.PasskeyMemberAsync("Biuro Narys");
        var (other, otherKey, _) = await Factory.PasskeyMemberAsync("Kitas Narys");
        using var _p = passkeyBrowser;
        using var _k = key;
        using var _o = other;
        using var _ok = otherKey;
        using var member = await Factory.CodeSessionAsync(email);
        try
        {
            var token = await TokenAsync(mp);
            var before = await member.MeAsync();
            (before.PasskeySession, before.PasskeyCount).ShouldBe((false, 1), "a code session of an account with a passkey");

            // Another account's passkey: refused, the session stays a code session, the invitation unused.
            await (await member.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token, Passkey = otherKey.Get(await RequestOptionsAsync(member)) }))
                .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.invalid");
            (await member.MeAsync()).PasskeySession.ShouldBeFalse();

            // Both ways at once: refused by the validator.
            using (var both = await member.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token, Passkey = "x", NewPasskey = "y" }))
            {
                both.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            // The account's own passkey: accepted, a passkey session.
            var joined = await member.PostJsonAsync<MpLinkResponse>(
                MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token, Passkey = key.Get(await RequestOptionsAsync(member)) });
            (joined.Part, joined.MayWrite).ShouldBe(("office", true));
            var after = await member.MeAsync();
            (after.PasskeySession, after.PasskeyCount).ShouldBe((true, 1), "no passkey added");
            after.Mp!.MayWrite.ShouldBeTrue();
        }
        finally
        {
            await member.DeleteAccountAsync();
            await other.DeleteAccountAsync();
            await mp.Browser.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task A_linked_account_signed_in_by_code_gets_no_passkey_from_an_invitation()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Eglaite);
        using var codeSession = await Factory.CodeSessionAsync(mp.Email);
        using var key = new SoftwareAuthenticator();
        try
        {
            var token = await TokenAsync(mp);
            var before = (await codeSession.MeAsync()).PasskeyCount;

            // The MP's own account by code, with a passkey made for it: refused, and the passkey is not stored (design 8.5.2:
            // a code alone never changes a linked account's passkeys).
            await (await codeSession.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = token, NewPasskey = key.Create(await CreationOptionsAsync(codeSession)) }))
                .ShouldBeProblemAsync(HttpStatusCode.Conflict, "mp.already-linked");
            var after = await codeSession.MeAsync();
            (after.PasskeyCount, after.PasskeySession).ShouldBe((before, false));
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }
}
