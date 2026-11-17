using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Mps;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Notifications.Templates;
using Lt2.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

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
                signedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signedIn.Content.ReadAsStringAsync());
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

    // ---- Owner, 2026-11-17: the site sends the invitation; it counts as an invitation; its code can be typed. ----

    [Fact]
    public async Task The_site_emails_an_invitation_with_the_link_and_the_code_and_keeps_the_address_nowhere_else()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var address = $"biuro.kviestinis.{Guid.NewGuid():N}@pavyzdys.test";
        try
        {
            // A wrong address is refused and nothing is made.
            using (var wrong = await mp.Browser.PostAsync(MpRoutes.OfficeInvitations, new MpOfficeInvitationRequest { Email = "ne-adresas" }))
            {
                wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await wrong.Content.ReadAsStringAsync());
            }

            var sent = await mp.Browser.PostJsonAsync<MpOfficeInvitationResponse>(MpRoutes.OfficeInvitations, new MpOfficeInvitationRequest { Email = address });
            sent.Emailed.ShouldBeTrue();
            OneTimeCode.Normalize(sent.Code).ShouldBe(sent.Code, "a typeable one-time code in its one form");
            sent.Path.ShouldBe(MpRoutes.OfficeInvitationPage + "#" + sent.Code, "design 6.8: the code after the #");

            // One email to the address, with the link and, for the way without it, the page and the code.
            var inbox = await Database.InboxAsync(address);
            var (kind, payload) = inbox.ShouldHaveSingleItem();
            kind.ShouldBe(MessageKinds.MpOfficeInvitationEmail);
            payload.GetProperty("code").GetString().ShouldBe(sent.Code);
            payload.GetProperty("pagePath").GetString().ShouldBe(MpRoutes.OfficeInvitationPage);
            // The payload rendered by the template the worker registers for this kind, with a site address (the test host has none)
            // to read the full links of both parts.
            var email = new MpTemplates(
                    Factory.Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<EmailTemplates>>(),
                    Microsoft.Extensions.Options.Options.Create(new SiteOptions { Address = "https://lt2.test" }))
                .OfficeInvitation(payload.Deserialize<MpOfficeInvitationEmail>(JsonSerializerOptions.Web)!);
            email.Subject.ShouldBe("Kvietimas į Seimo nario biurą Lietuva 2.0");
            email.TextBody.ShouldContain("Seimo narys: Aurelija Kadagytė.");
            email.TextBody.ShouldContain($"Priimti kvietimą: https://lt2.test{MpRoutes.OfficeInvitationPage}#{sent.Code}");
            email.TextBody.ShouldContain($"atverkite https://lt2.test{MpRoutes.OfficeInvitationPage} ir įveskite šį kodą:\n\n{sent.Code}\n\n");
            email.HtmlBody.ShouldNotBeNull().ShouldContain($"<a href=\"https://lt2.test{MpRoutes.OfficeInvitationPage}#{sent.Code}\" style=\"font-weight:600;color:#1f6b3a\">Priimti kvietimą</a></p>");
            email.HtmlBody.ShouldContain($"{sent.Code}</p>");

            // The address is kept nowhere but the outbox row: not on the invitation, not in the audit.
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM identity.politician_token t WHERE row_to_json(t)::text ILIKE @a", ("a", "%" + address + "%"))).ShouldBe(0);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event e WHERE row_to_json(e)::text ILIKE @a", ("a", "%" + address + "%"))).ShouldBe(0);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'mp.office-invited' AND details::jsonb->>'emailed' = 'true'")).ShouldBeGreaterThan(0);
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task An_mp_makes_at_most_ten_invitations_a_day()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Ievaite);
        try
        {
            var made = await Database.ScalarAsync<long>(
                "SELECT count(*) FROM identity.politician_token WHERE kind = 'office' AND seimas_person_id = @p AND created_at > now() - interval '1 day'",
                ("p", MpSteps.Ievaite.SeimasPersonId));
            for (var i = made; i < 10; i++)
            {
                (await mp.Browser.PostJsonAsync<MpOfficeInvitationResponse>(MpRoutes.OfficeInvitations, new MpOfficeInvitationRequest())).Emailed.ShouldBeFalse();
            }

            await (await mp.Browser.PostAsync(MpRoutes.OfficeInvitations, new MpOfficeInvitationRequest()))
                .ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "mp.office-invitation-limit");
        }
        finally
        {
            await Database.ExecuteAsync("DELETE FROM identity.politician_token WHERE kind = 'office' AND seimas_person_id = @p", ("p", MpSteps.Ievaite.SeimasPersonId));
            await mp.Browser.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task A_person_without_an_account_registers_with_the_office_invitation_in_place_of_an_invitation_code_and_accepts_it_typed()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Eglaite);
        using var person = NewBrowser();
        var address = TestData.Email();
        using var key = new SoftwareAuthenticator();
        try
        {
            var code = await TokenAsync(mp);

            // Without any invitation the closed trial refuses; with the office invitation it lets the registration through.
            await (await person.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = address }))
                .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.required");
            using (var registered = await person.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = address, OfficeInvitation = code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) }))
            {
                registered.StatusCode.ShouldBe(HttpStatusCode.Accepted, await registered.Content.ReadAsStringAsync());
            }

            await person.SignInWithCodeAsync(address);
            (await Database.ScalarAsync<string>("SELECT invitation_code FROM identity.profile WHERE normalized_email = lower(@e)", ("e", address))).ShouldBeNull("the pending account holds no invitation");

            // Completed without an invitation code: refused; with the office invitation: a member, keeping only a level.
            await (await person.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { DisplayName = "Biuro Narė", ConfirmsAdult = true, AcceptsTerms = true }))
                .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.required");
            using (var completed = await person.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { DisplayName = "Biuro Narė", ConfirmsAdult = true, AcceptsTerms = true, OfficeInvitation = code }))
            {
                completed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await completed.Content.ReadAsStringAsync());
            }

            var mpLevel = await Database.ScalarAsync<int?>("SELECT invitation_level FROM identity.profile p JOIN identity.\"user\" u ON u.id = p.user_id WHERE lower(u.email) = lower(@e)", ("e", mp.Email));
            (await Database.ScalarAsync<int?>("SELECT invitation_level FROM identity.profile WHERE normalized_email = lower(@e)", ("e", address))).ShouldBe((mpLevel ?? 1) + 1);
            (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.profile WHERE normalized_email = lower(@e) AND invitation_id IS NULL AND invitation_code IS NULL", ("e", address))).ShouldBe(1, "nothing on the account names the MP");
            (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.politician_token WHERE token_hash = @h AND used_at IS NULL", ("h", OneTimeCode.Hash(code)))).ShouldBe(1, "registering does not use the invitation up");

            // The new member accepts with the code as typed, bringing a passkey made now.
            using var creation = await person.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions);
            var joined = await person.PostJsonAsync<MpLinkResponse>(
                MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = " " + code.ToLowerInvariant() + " ", NewPasskey = key.Create(await creation.Content.ReadAsStringAsync()) });
            (joined.Part, joined.MayWrite).ShouldBe(("office", true));
        }
        finally
        {
            await person.DeleteAccountAsync();
            await mp.Browser.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task A_used_or_expired_or_unknown_office_invitation_lets_no_registration_through_and_gets_one_answer()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var (member, _, _) = await Factory.PasskeyMemberAsync("Kita Biuro Narė");
        using var _m = member;
        try
        {
            var used = await TokenAsync(mp);
            await member.PostJsonAsync<MpLinkResponse>(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = used });
            var expired = await TokenAsync(mp);
            await Database.ExecuteAsync("UPDATE identity.politician_token SET expires_at = now() - interval '1 minute' WHERE token_hash = @h", ("h", OneTimeCode.Hash(expired)));

            foreach (var code in new[] { used, expired, OneTimeCode.New(), "ne-kodas" })
            {
                using var person = NewBrowser();
                var answer = await (await person.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = TestData.Email(), OfficeInvitation = code })).ProblemAsync();
                answer.GetProperty("code").GetString().ShouldBe("invitation.invalid", code);
            }

            // At acceptance the same: one answer for a used, an expired, an unknown and a malformed code.
            foreach (var code in new[] { used, expired, OneTimeCode.New(), "ne-kodas" })
            {
                await (await member.PostAsync(MpRoutes.OfficeAcceptance, new MpOfficeAcceptanceRequest { Token = code })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "mp.link-invalid");
            }
        }
        finally
        {
            await member.DeleteAccountAsync();
            await mp.Browser.DeleteAccountAsync();
        }
    }
}