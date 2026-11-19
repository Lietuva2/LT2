using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.2, "Changing the email address": <c>POST /me/email/proof-code</c>, <c>/me/email/change</c>, <c>/me/email/confirm</c> and
/// <c>/me/email/restore</c>. The change starts only in a passkey session or after a code to the address in use (a staff account:
/// its staff passkey, freshly confirmed; the MP's account: a passkey session); the new address gets a code; the answer is the same
/// whether the address is free or another account's, which is told instead; an account only pending on the address goes. On the
/// code the account moves, its other sessions and open codes end, this session goes on under a new key, and the old address gets
/// the masked new address and the link back, which puts it back without a session and without the terms accepted.
/// </summary>
public sealed class EmailChangeTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task A_passkey_session_moves_the_account_with_the_code_sent_to_the_new_address_and_ends_the_other_sessions()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        using var _b = browser;
        using var _k = key;
        using var other = await Factory.CodeSessionAsync(email);
        var before = (await SessionsAsync(browser)).Single(s => s.IsCurrent);
        var next = TestData.Email("ruta.nauja");

        using (var asked = await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = next }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
            (await asked.Content.ReadAsStringAsync()).ShouldBeEmpty();
        }

        var code = (await ChangeCodeAsync(next)).ShouldNotBeNull("the code goes to the new address");
        (await browser.MeAsync()).Email.ShouldBe(email, "nothing changes before the code");

        var confirmed = await ConfirmAsync(browser, code);
        confirmed.Email.ShouldBe(next);

        var me = await browser.MeAsync();
        me.Email.ShouldBe(next);
        me.PasskeySession.ShouldBeTrue("the session goes on as what it was signed in as");
        var after = (await SessionsAsync(browser)).ShouldHaveSingleItem("the account's other sessions ended");
        after.IsCurrent.ShouldBeTrue();
        after.Id.ShouldNotBe(before.Id, "this session goes on under a new key");
        after.Method.ShouldBe("pop");
        (await other.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await Database.ScalarAsync<string>("SELECT p.normalized_email FROM identity.profile p JOIN identity.\"user\" u ON u.id = p.user_id WHERE u.email = @e", ("e", next)))
            .ShouldBe(next.ToLowerInvariant());
        (await Database.ScalarAsync<string>("SELECT normalized_email FROM identity.\"user\" WHERE email = @e", ("e", next))).ShouldBe(next.ToUpperInvariant());
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.\"user\" WHERE lower(email) = lower(@e)", ("e", email))).ShouldBe(0);

        // The passkey and the known browser stay: a passkey sign-in works with the new address.
        using (var again = NewBrowser())
        {
            (await again.SignInWithPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await again.MeAsync()).Email.ShouldBe(next);
        }

        await browser.DeleteAccountAsync();
    }

    [Fact]
    public async Task A_code_session_changes_the_address_only_after_a_code_sent_to_the_address_in_use()
    {
        var (browser, email) = await Factory.MemberAsync("Ona Jonaitienė");
        using var _b = browser;
        var next = TestData.Email("ona.kita");

        await (await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = next }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "email.proof-required");
        (await ChangeCodeAsync(next)).ShouldBeNull("a stolen session is not enough");

        using (var sent = await browser.PostAsync(ApiRoutes.Me.EmailProofCode))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.Accepted, await sent.Content.ReadAsStringAsync());
        }

        (await Database.ScalarAsync<string>(
            "SELECT payload->>'purpose' FROM notify.outbox WHERE kind = 'email.sign-in-code' AND payload->>'to' = @e ORDER BY created_at DESC, id DESC LIMIT 1",
            ("e", email))).ShouldBe("email-change");
        var proof = (await Factory.LatestCodeAsync(email))!;
        var wrong = proof == "000000" ? "111111" : "000000";
        await (await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = next, ProofCode = wrong }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");

        using (var asked = await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = next, ProofCode = proof }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        }

        (await ConfirmAsync(browser, (await ChangeCodeAsync(next))!)).Email.ShouldBe(next);
        var me = await browser.MeAsync();
        me.Email.ShouldBe(next);
        me.PasskeySession.ShouldBeFalse();
        (await AuditDetailsAsync(next, "account.email-changed")).GetProperty("proof").GetString().ShouldBe("code");

        // The proof code was used up: it does not move the account again.
        await (await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = TestData.Email(), ProofCode = proof }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        await browser.DeleteAccountAsync();
    }

    [Fact]
    public async Task The_old_address_is_told_with_the_new_one_in_part_and_the_link_back_and_the_audit_holds_no_address()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        using var _b = browser;
        using var _k = key;
        var next = $"zeta.{Guid.NewGuid():N}@Pavyzdys.test";
        await ChangeAsync(browser, next);

        var told = await OutboxPayloadAsync("email.email-changed", email);
        told.GetProperty("newAddress").GetString().ShouldBe("z***@pavyzdys.test");
        var path = told.GetProperty("path").GetString()!;
        path.ShouldStartWith(AccountScreens.EmailRestore + "#");
        var token = path[(path.IndexOf('#', StringComparison.Ordinal) + 1)..];
        OneTimeCode.Normalize(token).ShouldBe(token);
        told.GetProperty("code").GetString().ShouldBe(token);
        told.GetProperty("pagePath").GetString().ShouldBe(AccountScreens.EmailRestore);
        (told.GetProperty("expiresAt").GetDateTimeOffset() - told.GetProperty("changedAt").GetDateTimeOffset()).ShouldBe(TimeSpan.FromDays(7));
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.email_restore WHERE token_hash = @h", ("h", OneTimeCode.Hash(token)))).ShouldBe(1, "kept only as a hash");

        var details = await AuditDetailsAsync(next, "account.email-changed");
        details.GetProperty("sessionsEnded").GetInt32().ShouldBe(1, "the registration's browser was still signed in");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action LIKE 'account.email-%' AND (details::text LIKE '%@%' OR target LIKE '%@%')"))
            .ShouldBe(0, "no addresses in the audit log");

        // Everything after goes to the new address; the export holds only the address in use.
        var export = await browser.Client.GetStringAsync(ApiRoutes.Me.Export);
        export.ShouldContain(next);
        export.ShouldNotContain(email);
        await browser.DeleteAccountAsync();
    }

    [Fact]
    public async Task An_address_of_another_members_account_gets_the_same_answer_tells_that_account_and_changes_nothing()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        var (holder, taken) = await Factory.MemberAsync("Ona Jonaitienė");
        using var _b = browser;
        using var _k = key;
        using var _h = holder;
        var free = TestData.Email();

        using var toFree = await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = free });
        using var toTaken = await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = taken.ToUpperInvariant() });

        toTaken.StatusCode.ShouldBe(toFree.StatusCode);
        toTaken.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await toTaken.Content.ReadAsStringAsync()).ShouldBe(await toFree.Content.ReadAsStringAsync());
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = 'email.email-change-code' AND lower(payload->>'to') = lower(@e)", ("e", taken)))
            .ShouldBe(0, "no code to an address another account has");
        (await OutboxPayloadAsync("email.email-change-taken", taken)).EnumerateObject().Select(p => p.Name).ShouldBe(["to"], "no code, no link");

        // Nothing can be confirmed: the code of the free address was replaced by the request for the taken one.
        await (await browser.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = (await ChangeCodeAsync(free))! }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        (await browser.MeAsync()).Email.ShouldBe(email);
        (await holder.MeAsync()).Email.ShouldBe(taken);
        await browser.DeleteAccountAsync();
        await holder.DeleteAccountAsync();
    }

    [Fact]
    public async Task An_address_held_only_by_a_pending_account_is_free_and_that_account_goes_on_confirming()
    {
        var pending = TestData.Email("laukianti");
        using (var stranger = NewBrowser())
        {
            await stranger.RegisterAsync(pending);
        }

        var pendingId = await Database.ScalarAsync<Guid>("SELECT id FROM identity.\"user\" WHERE email = @e", ("e", pending));
        var (browser, key, _) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        using var _b = browser;
        using var _k = key;

        (await ConfirmAsync(browser, await AskAsync(browser, pending))).Email.ShouldBe(pending);

        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.\"user\" WHERE id = @id", ("id", pendingId))).ShouldBe(0, "the pending account is deleted");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.profile WHERE normalized_email = @e", ("e", pending.ToLowerInvariant()))).ShouldBe(1);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE kind = 'email.sign-in-code' AND payload->>'to' = @e", ("e", pending)))
            .ShouldBe(0, "its unsent emails went with it");
        (await AuditDetailsAsync(pending, "account.email-changed")).GetProperty("pendingAccountRemoved").GetBoolean().ShouldBeTrue();
        await browser.DeleteAccountAsync();
    }

    [Fact]
    public async Task A_wrong_expired_or_used_code_is_refused_with_one_answer()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        using var _b = browser;
        using var _k = key;

        // Five wrong attempts use the code up.
        var next = TestData.Email();
        var code = await AskAsync(browser, next);
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
        {
            await (await browser.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = wrong })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        }

        await (await browser.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = code })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");

        // Expired.
        code = await AskAsync(browser, next);
        await Database.ExecuteAsync(
            "UPDATE identity.email_change SET expires_at = now() - interval '1 second' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email));
        await (await browser.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = code })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");

        // Replaced by a newer one.
        var first = await AskAsync(browser, next);
        var second = await AskAsync(browser, next);
        if (first != second)
        {
            await (await browser.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = first })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        }

        // Only from the session that asked.
        using (var other = await Factory.CodeSessionAsync(email))
        {
            await (await other.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = second })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        }

        // Used once only.
        (await ConfirmAsync(browser, second)).Email.ShouldBe(next);
        await (await browser.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = second })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        await browser.DeleteAccountAsync();
    }

    [Fact]
    public async Task A_code_sent_to_the_old_address_stops_working()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        using var _b = browser;
        using var _k = key;
        using (var elsewhere = NewBrowser())
        {
            (await elsewhere.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var old = (await Factory.LatestCodeAsync(email))!;
        var next = TestData.Email();
        await ChangeAsync(browser, next);

        using var stranger = NewBrowser();
        await (await stranger.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = next, Code = old })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        await (await stranger.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = old })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        await browser.DeleteAccountAsync();
    }

    [Fact]
    public async Task The_same_address_a_disposable_one_and_a_visitor_are_refused()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        using var _b = browser;
        using var _k = key;
        var at = email.IndexOf('@', StringComparison.Ordinal);
        var same = email[..at].ToUpperInvariant() + "+zyme" + email[at..];

        await (await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = same })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "email.same");
        await (await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = "ruta@mailinator.com" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "email.disposable");
        await (await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = "ne adresas" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.email_change WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email)))
            .ShouldBe(0);

        using var visitor = NewBrowser();
        (await visitor.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = TestData.Email() })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await visitor.PostAsync(ApiRoutes.Me.EmailProofCode)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await visitor.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = "123456" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await browser.DeleteAccountAsync();
    }

    [Fact]
    public async Task A_staff_account_changes_it_only_in_its_staff_passkey_session_freshly_confirmed()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var next = TestData.Email("administratorius");

        // A code session of the staff account: no code proves it.
        using (var code = await Factory.CodeSessionAsync(admin.Email))
        {
            await (await code.PostAsync(ApiRoutes.Me.EmailProofCode)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.staff-required");
            var proof = (await Factory.LatestCodeAsync(admin.Email))!;
            await (await code.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = next, ProofCode = proof }))
                .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.staff-required");
        }

        // The staff-passkey session, its confirmation older than ten minutes, then confirmed again.
        await Database.ExecuteAsync(
            "UPDATE identity.session SET passkey_confirmed_at = now() - interval '20 minutes' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("e", admin.Email));
        await (await admin.Browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = next }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
        (await ChangeCodeAsync(next)).ShouldBeNull();

        (await admin.Browser.ConfirmPasskeyAsync(admin.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ConfirmAsync(admin.Browser, await AskAsync(admin.Browser, next))).Email.ShouldBe(next);
        (await admin.Browser.MeAsync()).ActiveStaffRoles.ShouldBe(["administrator"], "still the staff session");
        (await admin.Browser.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_mps_own_account_changes_it_only_in_a_passkey_session_and_its_link_stays()
    {
        using var mp = await Factory.MpAccountAsync(MpSteps.Kadagyte);
        var next = TestData.Email("biuras");
        try
        {
            using (var code = await Factory.CodeSessionAsync(mp.Email))
            {
                await (await code.PostAsync(ApiRoutes.Me.EmailProofCode)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
                await (await code.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = next, ProofCode = "123456" }))
                    .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "mp.passkey-required");
            }

            (await ConfirmAsync(mp.Browser, await AskAsync(mp.Browser, next))).Email.ShouldBe(next);
            var me = await mp.Browser.MeAsync();
            me.Mp.ShouldNotBeNull("the link is to the person (design 8.5.2)").MayWrite.ShouldBeTrue();
            me.MpOffer.ShouldBeNull();
            (await Database.OpenLinksAsync(MpSteps.Kadagyte.SeimasPersonId, "mp")).ShouldBe(1);
        }
        finally
        {
            await mp.Browser.DeleteAccountAsync();
        }
    }

    [Fact]
    public async Task The_link_back_restores_the_old_address_ends_every_session_and_works_without_the_terms_accepted()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        using var _b = browser;
        using var _k = key;
        var next = TestData.Email();
        await ChangeAsync(browser, next);
        using var later = await Factory.CodeSessionAsync(next);
        var token = await RestoreTokenAsync(email);

        // The account in this browser has not accepted the terms in force: the link still works here (design 8.22.2).
        await Database.ExecuteAsync("DELETE FROM identity.consent WHERE kind = 'terms' AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", next));
        (await browser.MeAsync()).TermsRequired.ShouldBeTrue();
        using (var restored = await browser.PostAsync(ApiRoutes.Me.EmailRestore, new EmailRestoreRequest { Token = token.ToLowerInvariant() }))
        {
            restored.StatusCode.ShouldBe(HttpStatusCode.NoContent, await restored.Content.ReadAsStringAsync());
        }

        (await Database.ScalarAsync<string>("SELECT u.email FROM identity.\"user\" u JOIN identity.profile p ON p.user_id = u.id WHERE p.normalized_email = @n", ("n", email.ToLowerInvariant())))
            .ShouldBe(email);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.session WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email)))
            .ShouldBe(0, "every session ended");
        (await browser.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await later.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await OutboxPayloadAsync("email.email-restored", next)).GetProperty("to").GetString().ShouldBe(next);
        (await AuditDetailsAsync(email, "account.email-restored")).GetProperty("sessionsEnded").GetInt32().ShouldBe(2);

        // The link works once.
        using var visitor = NewBrowser();
        await (await visitor.PostAsync(ApiRoutes.Me.EmailRestore, new EmailRestoreRequest { Token = token }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "email.restore-invalid");

        // Signed in again with the old address, the account is whole.
        using var again = await Factory.CodeSessionAsync(email);
        (await again.MeAsync()).Email.ShouldBe(email);
        await again.DeleteAccountAsync();
    }

    [Fact]
    public async Task A_visitor_restores_and_an_unknown_expired_or_taken_link_gets_one_refusal()
    {
        var (browser, key, email) = await Factory.PasskeyMemberAsync("Rūta Mockutė");
        using var _b = browser;
        using var _k = key;
        var next = TestData.Email();
        await ChangeAsync(browser, next);
        var token = await RestoreTokenAsync(email);
        using var visitor = NewBrowser();

        async Task RefusedAsync(string sent)
        {
            using var refused = await visitor.PostAsync(ApiRoutes.Me.EmailRestore, new EmailRestoreRequest { Token = sent });
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "email.restore-invalid");
        }

        await RefusedAsync(OneTimeCode.New());
        await RefusedAsync("ne kodas");

        // Expired.
        await Database.ExecuteAsync("UPDATE identity.email_restore SET expires_at = now() - interval '1 second' WHERE token_hash = @h", ("h", OneTimeCode.Hash(token)));
        await RefusedAsync(token);
        await Database.ExecuteAsync("UPDATE identity.email_restore SET expires_at = now() + interval '7 days' WHERE token_hash = @h", ("h", OneTimeCode.Hash(token)));

        // The old address meanwhile belongs to another member's account: nothing changes.
        using (var newcomer = NewBrowser())
        {
            await newcomer.RegisterAndSignInAsync(email);
            await RefusedAsync(token);
            (await browser.MeAsync()).Email.ShouldBe(next);
            await newcomer.DeleteAccountAsync();
        }

        // Free again: a visitor's browser puts it back.
        using (var restored = await visitor.PostAsync(ApiRoutes.Me.EmailRestore, new EmailRestoreRequest { Token = token }))
        {
            restored.StatusCode.ShouldBe(HttpStatusCode.NoContent, await restored.Content.ReadAsStringAsync());
        }

        (await browser.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var again = await Factory.CodeSessionAsync(email);
        await again.DeleteAccountAsync();
    }

    /// <summary>Asks for a change to <paramref name="next"/> in the browser's (passkey) session; returns the code sent to it.</summary>
    private async Task<string> AskAsync(TestBrowser browser, string next)
    {
        using var asked = await browser.PostAsync(ApiRoutes.Me.EmailChange, new EmailChangeRequest { Email = next });
        asked.StatusCode.ShouldBe(HttpStatusCode.Accepted, await asked.Content.ReadAsStringAsync());
        return (await ChangeCodeAsync(next)).ShouldNotBeNull();
    }

    private async Task ChangeAsync(TestBrowser browser, string next) => (await ConfirmAsync(browser, await AskAsync(browser, next))).Email.ShouldBe(next);

    private static async Task<EmailChangedResponse> ConfirmAsync(TestBrowser browser, string code)
    {
        using var confirmed = await browser.PostAsync(ApiRoutes.Me.EmailConfirm, new EmailConfirmRequest { Code = code });
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        return (await confirmed.Content.ReadFromJsonAsync<EmailChangedResponse>(JsonSerializerOptions.Web))!;
    }

    /// <summary>The code in the newest email of a change waiting in the outbox for <paramref name="to"/>.</summary>
    private Task<string?> ChangeCodeAsync(string to) =>
        Database.ScalarAsync<string>(
            "SELECT payload->>'code' FROM notify.outbox WHERE kind = 'email.email-change-code' AND payload->>'to' = @to ORDER BY created_at DESC, id DESC LIMIT 1",
            ("to", to));

    private async Task<JsonElement> OutboxPayloadAsync(string kind, string to)
    {
        var json = await Database.ScalarAsync<string>(
            "SELECT payload::text FROM notify.outbox WHERE kind = @k AND lower(payload->>'to') = lower(@to) ORDER BY created_at DESC, id DESC LIMIT 1",
            ("k", kind),
            ("to", to));
        json.ShouldNotBeNull($"an email {kind} to the address");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task<string> RestoreTokenAsync(string old) => (await OutboxPayloadAsync("email.email-changed", old)).GetProperty("code").GetString()!;

    private async Task<JsonElement> AuditDetailsAsync(string email, string action)
    {
        var json = await Database.ScalarAsync<string>(
            "SELECT details::text FROM audit.event WHERE action = @a AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e) ORDER BY occurred_at DESC LIMIT 1",
            ("a", action),
            ("e", email));
        json.ShouldNotBeNull($"{action} is audited");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static async Task<List<SessionResponse>> SessionsAsync(TestBrowser browser) =>
        (await browser.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!;
}
