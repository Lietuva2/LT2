using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3, "A full lock": an administrator (never a moderator) locks an account with a statement and a fresh passkey
/// confirmation; every session ends, no sign-in works (a code request answers as for any address, a typed code and a passkey
/// are refused as wrong ones), the public profile is not shown, the person is emailed the statement, and an administrator
/// unlocks it again. A staff account with an active role and oneself are not locked.
/// </summary>
public sealed class AccountLockTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Statement = "Piktnaudžiavimas: pakartotinai skelbiami kitų asmenų duomenys.";

    private static Task<HttpResponseMessage> LockAsync(StaffMember admin, string email, string statement = Statement) =>
        admin.Browser.PostAsync(ApiRoutes.Admin.LockAccount, new AccountLockRequest { Email = email, Statement = statement });

    private static Task<HttpResponseMessage> UnlockAsync(StaffMember admin, string email, string statement = "Klaida ištaisyta.") =>
        admin.Browser.PostAsync(ApiRoutes.Admin.UnlockAccount, new AccountLockRequest { Email = email, Statement = statement });

    private Task<long> AuditAsync(string email, string action) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = @a AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e)",
            ("a", action), ("e", email));

    private async Task<HttpResponseMessage> SignInWithCodeAsync(TestBrowser browser, string email, string? code = null)
    {
        code ??= await Factory.LatestCodeAsync(email);
        return await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = code! });
    }

    [Fact]
    public async Task Locking_ends_every_session_and_stops_every_way_of_signing_in_and_unlocking_restores_them()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;
        using var key = new SoftwareAuthenticator();
        (await member.AddPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var second = NewBrowser();
        (await second.SignInWithPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A code asked for before the lock does not outlive it.
        using (var asked = await NewBrowser().PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var before = await Factory.LatestCodeAsync(email);
        using (var locked = await LockAsync(admin, email))
        {
            locked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await locked.Content.ReadAsStringAsync());
        }

        (await member.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the session ended at once");
        (await second.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "every session of the account");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.session WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)", ("e", email))).ShouldBe(0);

        // The answers are those of an address nobody holds.
        using var stranger = NewBrowser();
        var sent = await Factory.OutboxCountAsync(email);
        using (var asked = await stranger.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var unknown = await stranger.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = TestData.Email() }))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await Factory.OutboxCountAsync(email)).ShouldBe(sent, "no code is sent to a locked account");
        using (var typed = await SignInWithCodeAsync(stranger, email, before))
        {
            await typed.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        }

        using (var unknownCode = await SignInWithCodeAsync(stranger, TestData.Email(), "123456"))
        {
            await unknownCode.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
        }

        using (var passkey = await stranger.SignInWithPasskeyAsync(key))
        {
            await passkey.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.invalid");
        }

        (await AuditAsync(email, "account.locked")).ShouldBe(1);
        var notice = await Database.LatestEmailAsync("email.account-notice", email);
        using (var told = JsonDocument.Parse(notice!))
        {
            told.RootElement.GetProperty("notice").GetString().ShouldBe("account-locked");
            told.RootElement.GetProperty("statement").GetString().ShouldBe(Statement);
        }

        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action IN ('account.locked', 'account.unlocked') AND (details::text LIKE '%@%' OR target LIKE '%@%')"))
            .ShouldBe(0, "no address in the audit log");

        using (var unlocked = await UnlockAsync(admin, email))
        {
            unlocked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await unlocked.Content.ReadAsStringAsync());
        }

        (await AuditAsync(email, "account.unlocked")).ShouldBe(1);
        using (var again = NewBrowser())
        {
            (await again.SignInWithPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var codeBrowser = NewBrowser();
        using (var asked = await codeBrowser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var signedIn = await SignInWithCodeAsync(codeBrowser, email))
        {
            signedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signedIn.Content.ReadAsStringAsync());
        }

        using (var back = JsonDocument.Parse((await Database.LatestEmailAsync("email.account-notice", email))!))
        {
            back.RootElement.GetProperty("notice").GetString().ShouldBe("account-unlocked");
        }
    }

    [Fact]
    public async Task Only_an_administrator_with_a_fresh_confirmation_locks_and_the_statement_is_required()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;

        using (var refused = await LockAsync(moderator, email))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using (var byMember = await member.PostAsync(ApiRoutes.Admin.LockAccount, new AccountLockRequest { Email = email, Statement = Statement }))
        {
            byMember.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var visitor = NewBrowser();
        using (var anonymous = await visitor.PostAsync(ApiRoutes.Admin.LockAccount, new AccountLockRequest { Email = email, Statement = Statement }))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var empty = await LockAsync(admin, email, "  "))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        await Database.ExecuteAsync(
            "UPDATE identity.session SET passkey_confirmed_at = now() - interval '30 minutes' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("e", admin.Email));
        using (var stale = await LockAsync(admin, email))
        {
            await stale.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
        }

        (await member.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.OK, "nothing was locked");
        (await AuditAsync(email, "account.locked")).ShouldBe(0);
    }

    [Fact]
    public async Task Oneself_a_staff_account_with_an_active_role_an_unknown_address_and_a_repeat_are_refused()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var staff = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        var (member, email) = await Factory.MemberAsync();
        using var _m = member;

        using (var self = await LockAsync(admin, admin.Email))
        {
            await self.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "account.lock-self");
        }

        using (var role = await LockAsync(admin, staff.Email))
        {
            await role.ShouldBeProblemAsync(HttpStatusCode.Conflict, "account.lock-staff");
        }

        using (var none = await LockAsync(admin, TestData.Email()))
        {
            await none.ShouldBeProblemAsync(HttpStatusCode.NotFound, "account.not-found");
        }

        using (var notLocked = await UnlockAsync(admin, email))
        {
            await notLocked.ShouldBeProblemAsync(HttpStatusCode.Conflict, "account.not-locked");
        }

        using (var first = await LockAsync(admin, email))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var second = await LockAsync(admin, email))
        {
            await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, "account.locked");
        }

        (await AuditAsync(email, "account.locked")).ShouldBe(1);
        (await Database.InboxAsync(email)).Count(m => m.Kind == "email.account-notice").ShouldBe(1);

        // After a reset the staff role is inactive and the account can be locked.
        using (var reset = await admin.Browser.PostAsync(ApiRoutes.Admin.ResetAccount, new ResetAccountRequest { Email = staff.Email }))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var afterReset = await LockAsync(admin, staff.Email))
        {
            afterReset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await afterReset.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_locked_accounts_public_profile_is_not_shown_and_comes_back_when_unlocked()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var (owner, email, id) = await Factory.PublicMemberAsync("Užrakinta Narė");
        using var _o = owner;
        using var visitor = NewBrowser();
        var path = ProfileRoutes.Page(id, "Užrakinta Narė");
        (await visitor.Client.GetAsync(ProfileRoutes.MemberOf(id), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await LockAsync(admin, email)).Dispose();

        using (var api = await visitor.Client.GetAsync(ProfileRoutes.MemberOf(id), TestContext.Current.CancellationToken))
        {
            api.StatusCode.ShouldBe(HttpStatusCode.Gone, "as a profile switched off");
        }

        using (var page = await visitor.Client.GetAsync(path, TestContext.Current.CancellationToken))
        {
            page.StatusCode.ShouldBe(HttpStatusCode.Gone);
            (await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("Užrakinta Narė");
        }

        (await UnlockAsync(admin, email)).Dispose();
        (await visitor.Client.GetAsync(ProfileRoutes.MemberOf(id), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
