using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Lt2.Migrator;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Lt2.IntegrationTests;

/// <summary>
/// Staff passkeys (design 6.2, decided 2026-11-12): a staff role works only in a session signed in with the account's
/// staff passkey, set by a one-time activation code the administrator passes on directly. Getting into a staff member's
/// mailbox is not enough.
/// </summary>
public sealed class StaffPasskeyTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task A_code_session_is_a_member_session_whatever_roles_the_account_has()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        (await MeAsync(admin.Browser)).ActiveStaffRoles.ShouldBe(["administrator"]);

        using var mailbox = NewBrowser();
        await SignInFromMailboxAsync(mailbox, admin.Email);

        var me = await MeAsync(mailbox);
        me.Roles.ShouldContain("administrator");
        me.ActiveStaffRoles.ShouldBeEmpty();
        using var settings = await mailbox.Client.GetAsync(ApiRoutes.Admin.Settings);
        await settings.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_mailbox_only_attacker_is_refused_before_activation_and_loses_their_passkey_when_the_person_activates()
    {
        using var person = NewBrowser();
        var email = await person.RegisterAndSignInAsync();
        var code = await Database.GrantFromServerAsync(email, "administrator");

        // The attacker reads the person's mail: signs in by code, adds a passkey, signs in with it.
        using var attackerKey = new SoftwareAuthenticator();
        using (var attacker = NewBrowser())
        {
            await SignInFromMailboxAsync(attacker, email);
            using var added = await attacker.AddPasskeyAsync(attackerKey);
            added.StatusCode.ShouldBe(HttpStatusCode.NoContent, "before activation the account is an ordinary member");
        }

        using var attackerSession = NewBrowser();
        (await attackerSession.SignInWithPasskeyAsync(attackerKey)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var settings = await attackerSession.Client.GetAsync(ApiRoutes.Admin.Settings))
        {
            await settings.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using (var guess = await attackerSession.ActivateRoleAsync("AAAA-BBBB-CCCC-DDDD"))
        {
            await guess.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "role-activation.invalid");
        }

        // The real person gets the code from the administrator in person and activates with their own passkey.
        using var personKey = new SoftwareAuthenticator();
        (await person.AddPasskeyAsync(personKey)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var staff = NewBrowser();
        (await staff.SignInWithPasskeyAsync(personKey)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var activation = await staff.ActivateRoleAsync(code.ToLowerInvariant()))
        {
            activation.StatusCode.ShouldBe(HttpStatusCode.NoContent, await activation.Content.ReadAsStringAsync());
        }

        (await MeAsync(staff)).PasskeyCount.ShouldBe(1, "every other passkey on the account is removed");
        (await staff.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The attacker's session was signed in with a passkey the activation removed: it ends with it (design 6.8).
        using (var stillAttacker = await attackerSession.Client.GetAsync(ApiRoutes.Me.Root))
        {
            stillAttacker.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var again = NewBrowser();
        using var refused = await again.SignInWithPasskeyAsync(attackerKey);
        await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "passkey.invalid");
        (await AuditCountAsync(email, "passkey.removed")).ShouldBe(1);
    }

    [Fact]
    public async Task An_activation_code_works_once()
    {
        using var person = NewBrowser();
        var email = await person.RegisterAndSignInAsync();
        using var key = new SoftwareAuthenticator();
        (await person.AddPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var code = await Database.GrantFromServerAsync(email, "moderator");
        using var session = NewBrowser();
        (await session.SignInWithPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await session.ActivateRoleAsync(code)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var second = await session.ActivateRoleAsync(code);

        await second.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "role-activation.invalid");
        (await MeAsync(session)).ActiveStaffRoles.ShouldBe(["moderator"]);
    }

    [Fact]
    public async Task An_activation_code_expires_after_a_day()
    {
        using var person = NewBrowser();
        var email = await person.RegisterAndSignInAsync();
        using var key = new SoftwareAuthenticator();
        (await person.AddPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var code = await Database.GrantFromServerAsync(email, "administrator");
        (await Database.ScalarAsync<double>(
            "SELECT (extract(epoch FROM a.expires_at - a.created_at) / 3600)::double precision FROM identity.role_activation a JOIN identity.\"user\" u ON u.id = a.user_id WHERE u.email = @e",
            ("e", email))).ShouldBe(24, 0.01);
        await Database.ExecuteAsync(
            "UPDATE identity.role_activation SET expires_at = now() - interval '1 minute' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("e", email));

        using var session = NewBrowser();
        (await session.SignInWithPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var late = await session.ActivateRoleAsync(code);

        await late.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "role-activation.invalid");
        (await session.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_activation_code_is_used_only_in_a_passkey_session()
    {
        using var person = NewBrowser();
        var email = await person.RegisterAndSignInAsync();
        var code = await Database.GrantFromServerAsync(email, "administrator");

        using var response = await person.ActivateRoleAsync(code);

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "role-activation.passkey-required");
        (await MeAsync(person)).AwaitingActivation.ShouldBeTrue("the code is still usable from a passkey session");
    }

    [Fact]
    public async Task Granting_a_role_again_makes_the_earlier_unused_code_stop_working()
    {
        using var person = NewBrowser();
        var email = await person.RegisterAndSignInAsync();
        using var key = new SoftwareAuthenticator();
        (await person.AddPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var first = await Database.GrantFromServerAsync(email, "administrator");
        var second = await Database.GrantFromServerAsync(email, "administrator");
        using var session = NewBrowser();
        (await session.SignInWithPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await (await session.ActivateRoleAsync(first)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "role-activation.invalid");
        (await session.ActivateRoleAsync(second)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task On_a_staff_account_adding_a_passkey_needs_the_staff_passkey_and_a_fresh_confirmation()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var extra = new SoftwareAuthenticator();

        // From a code session: refused.
        using (var mailbox = NewBrowser())
        {
            await SignInFromMailboxAsync(mailbox, admin.Email);
            using var fromCode = await mailbox.AddPasskeyAsync(extra);
            await fromCode.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.staff-required");
        }

        // From the staff passkey session, more than ten minutes after the passkey was last used: refused.
        await AgeConfirmationAsync(admin.Email, minutes: 11);
        using (var stale = await admin.Browser.AddPasskeyAsync(extra))
        {
            await stale.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
        }

        // After a fresh confirmation with the staff passkey: allowed.
        (await admin.Browser.ConfirmPasskeyAsync(admin.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var fresh = await admin.Browser.AddPasskeyAsync(extra))
        {
            fresh.StatusCode.ShouldBe(HttpStatusCode.NoContent, await fresh.Content.ReadAsStringAsync());
        }

        // The new passkey signs in, but it is not the staff passkey: a member session that cannot confirm either.
        using var other = NewBrowser();
        (await other.SignInWithPasskeyAsync(extra)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await (await other.ConfirmPasskeyAsync(extra)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.staff-required");
    }

    [Fact]
    public async Task A_member_without_a_staff_role_adds_passkeys_as_before_and_is_told_by_email()
    {
        using var member = NewBrowser();
        var email = await member.RegisterAndSignInAsync();
        using var key = new SoftwareAuthenticator();

        (await member.AddPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await NoticesAsync(email)).ShouldBe(["passkey-added"]);
        (await AuditCountAsync(email, "passkey.added")).ShouldBe(1);
    }

    /// <summary>Design 6.9: saving a setting asks for the passkey again; switching an invitation code does not.</summary>
    [Fact]
    public async Task Saving_a_setting_needs_a_fresh_passkey_confirmation_and_switching_a_code_does_not()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var request = new UpdateSettingRequest { Value = System.Text.Json.JsonSerializer.SerializeToElement(12) };

        await AgeConfirmationAsync(admin.Email, minutes: 11);
        using (var stale = await admin.Browser.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey("Feedback:KeepMonths"), request))
        {
            await stale.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
        }

        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action = 'setting.changed' AND actor_user_id = @u", ("u", await Database.UserIdAsync(admin.Email))))
            .ShouldBe(0, "nothing was saved");

        // An invitation code is switched without a new confirmation.
        var (member, _) = await Factory.MemberAsync();
        using var _m = member;
        var code = await member.GetJsonAsync<MyInvitationResponse>(ApiRoutes.Me.Invitation);
        var invitationId = await Database.ScalarAsync<Guid>("SELECT id FROM identity.invitation WHERE code = @c", ("c", code.Code));
        (await admin.Browser.PostAsync(ApiRoutes.Admin.SwitchInvitation(invitationId, on: false), new Lt2.Api.Contracts.Appeals.StatementRequest { Statement = "Bandymas." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.Browser.ConfirmPasskeyAsync(admin.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var saved = await admin.Browser.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey("Feedback:KeepMonths"), request);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Design 6.2: approving an urgent summary to MPs is a sensitive action: without a passkey confirmation in the last ten
    /// minutes it is refused and nothing is made or audited; after one, the summary is made.
    /// </summary>
    [Fact]
    public async Task Approving_an_urgent_summary_needs_a_fresh_passkey_confirmation()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        var admin = await AdministratorAsync();
        await admin.Browser.SetSettingAsync("Digest:ThresholdMinimum", 1);
        try
        {
            var (initiator, _) = await Factory.MemberAsync();
            using var _i = initiator;
            var record = await Database.PrepareBillAsync(initiator, moderator.Browser, AppealSteps.ThirdBill, "against", threshold: 1);
            var route = AppealRoutes.With(DigestRoutes.Urgent, record.Id);
            var request = new UrgentDigestRequest { Statement = "Balsavimas perkeltas į rytojų." };
            var moderatorId = await Database.UserIdAsync(moderator.Email);

            await AgeConfirmationAsync(moderator.Email, minutes: 11);
            using (var stale = await moderator.Browser.PostAsync(route, request))
            {
                await stale.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
            }

            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM notify.digest_run r JOIN notify.digest_item i ON i.run_id = r.id WHERE r.kind = 'urgent' AND i.record_id = @r",
                ("r", record.Id))).ShouldBe(0, "nothing was made");
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'digest.urgent.approved' AND actor_user_id = @u", ("u", moderatorId))).ShouldBe(0);

            (await moderator.Browser.ConfirmPasskeyAsync(moderator.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using var fresh = await moderator.Browser.PostAsync(route, request);
            fresh.StatusCode.ShouldBe(HttpStatusCode.Created, await fresh.Content.ReadAsStringAsync());
            (await fresh.ReadAsync<DigestRunResponse>()).Kind.ShouldBe("urgent");
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'digest.urgent.approved' AND actor_user_id = @u", ("u", moderatorId))).ShouldBe(1);
        }
        finally
        {
            await admin.Browser.ResetSettingAsync("Digest:ThresholdMinimum");
            await Database.FreeBillAsync(AppealSteps.ThirdBill);
        }
    }

    [Fact]
    public async Task The_administrator_api_grants_a_role_with_a_code_shown_once_after_a_fresh_confirmation()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var person = NewBrowser();
        var email = await person.RegisterAndSignInAsync();
        var request = new GrantRoleRequest { Email = email, Role = "moderator" };

        await AgeConfirmationAsync(admin.Email, minutes: 11);
        using (var stale = await admin.Browser.PostAsync(ApiRoutes.Admin.Roles, request))
        {
            await stale.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
        }

        (await admin.Browser.ConfirmPasskeyAsync(admin.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var granted = await admin.Browser.PostAsync(ApiRoutes.Admin.Roles, request);
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        var grant = (await granted.Content.ReadFromJsonAsync<GrantRoleResponse>())!;
        grant.ActivationCode.ShouldMatch("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$");
        grant.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddHours(23));

        // Only a hash is stored, and the code is in no email and no audit entry.
        (await Database.ScalarAsync<int>(
            "SELECT length(a.code_hash) FROM identity.role_activation a JOIN identity.\"user\" u ON u.id = a.user_id WHERE u.email = @e",
            ("e", email))).ShouldBe(32, "a SHA-256 hash, never the code");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM notify.outbox WHERE payload::text LIKE '%' || @c || '%'", ("c", grant.ActivationCode))).ShouldBe(0);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE details::text LIKE '%' || @c || '%'", ("c", grant.ActivationCode))).ShouldBe(0);
        (await NoticesAsync(email)).ShouldContain("role-granted");
        (await AuditCountAsync(email, "role.granted")).ShouldBe(1);

        // Unknown roles and accounts that do not exist or are still pending are refused.
        await (await admin.Browser.PostAsync(ApiRoutes.Admin.Roles, new GrantRoleRequest { Email = email, Role = "member" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "role.unknown");
        await (await admin.Browser.PostAsync(ApiRoutes.Admin.Roles, new GrantRoleRequest { Email = TestData.Email("nera"), Role = "moderator" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "account.not-found");
        using var pending = NewBrowser();
        var pendingEmail = await pending.RegisterAndSignInAsync(complete: false);
        await (await admin.Browser.PostAsync(ApiRoutes.Admin.Roles, new GrantRoleRequest { Email = pendingEmail, Role = "moderator" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "account.not-found");
    }

    [Fact]
    public async Task Only_an_active_administrator_may_grant_roles()
    {
        using var moderator = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        using var response = await moderator.Browser.PostAsync(ApiRoutes.Admin.Roles, new GrantRoleRequest { Email = moderator.Email, Role = "administrator" });
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_reset_removes_the_passkeys_and_the_role_stays_inactive_until_a_new_activation()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var target = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        (await target.Browser.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var reset = await admin.Browser.PostAsync(ApiRoutes.Admin.ResetAccount, new ResetAccountRequest { Email = target.Email }))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());
        }

        // The open staff session was signed in with a removed passkey: it ends at once (design 6.8).
        (await target.Browser.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "at once, in the open session too");
        using var mailbox = NewBrowser();
        await SignInFromMailboxAsync(mailbox, target.Email);
        var me = await MeAsync(mailbox);
        me.PasskeyCount.ShouldBe(0);
        me.ActiveStaffRoles.ShouldBeEmpty();
        (await NoticesAsync(target.Email)).ShouldContain("account-reset");
        (await AuditCountAsync(target.Email, "account.reset")).ShouldBe(1);

        // A new grant and activation with a new passkey bring the role back.
        var code = await Database.GrantFromServerAsync(target.Email, "administrator");
        using var newKey = new SoftwareAuthenticator();
        using var codeSession = NewBrowser();
        await SignInFromMailboxAsync(codeSession, target.Email);
        (await codeSession.AddPasskeyAsync(newKey)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var staff = NewBrowser();
        (await staff.SignInWithPasskeyAsync(newKey)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await staff.ActivateRoleAsync(code)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await staff.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_reset_ends_every_session_signed_in_with_a_removed_passkey_and_keeps_the_code_sessions()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var target = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        using var secondDevice = NewBrowser();
        (await secondDevice.SignInWithPasskeyAsync(target.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var codeSession = NewBrowser();
        await SignInFromMailboxAsync(codeSession, target.Email);

        using (var reset = await admin.Browser.PostAsync(ApiRoutes.Admin.ResetAccount, new ResetAccountRequest { Email = target.Email }))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());
        }

        (await target.Browser.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await secondDevice.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await codeSession.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.OK, "a code session was not signed in with a passkey");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.session WHERE passkey_id IS NOT NULL AND user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("e", target.Email))).ShouldBe(0);
    }

    [Fact]
    public async Task Grant_activation_and_passkey_changes_are_audited_and_the_person_is_told_without_a_code()
    {
        using var staff = await StaffMembers.ActiveAsync(Factory, Database, "moderator");

        (await AuditCountAsync(staff.Email, "role.granted")).ShouldBe(1);
        (await AuditCountAsync(staff.Email, "role.activated")).ShouldBe(1);
        (await AuditCountAsync(staff.Email, "passkey.added")).ShouldBe(1);
        (await NoticesAsync(staff.Email)).ShouldBe(["passkey-added", "role-granted", "role-activated"], ignoreOrder: true);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action LIKE 'role.%' AND (details::text LIKE '%@%' OR target LIKE '%@%')"))
            .ShouldBe(0, "no addresses in the audit log");
    }

    [Fact]
    public async Task The_migrator_prints_the_activation_code_on_the_console_and_never_to_a_log_sink()
    {
        using var person = NewBrowser();
        var email = await person.RegisterAndSignInAsync();
        var sink = new CollectingSink();
        var previousLogger = Log.Logger;
        var previousOut = Console.Out;
        await using var console = new StringWriter();
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        Console.SetOut(console);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Lt2"] = Database.MigratorConnectionString })
                .Build();
            await MigrationRunner.RunAsync(configuration, ["--grant-role", "administrator", email], CancellationToken.None);
        }
        finally
        {
            Console.SetOut(previousOut);
            Log.Logger = previousLogger;
        }

        var printed = console.ToString();
        var code = System.Text.RegularExpressions.Regex.Match(printed, "[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}").Value;
        code.ShouldNotBeNullOrEmpty(printed);
        printed.ShouldNotContain(email);
        sink.Lines.ShouldNotBeEmpty();
        sink.Lines.ShouldAllBe(line => !line.Contains(code) && !line.Contains(email));

        using var key = new SoftwareAuthenticator();
        (await person.AddPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var session = NewBrowser();
        (await session.SignInWithPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await session.ActivateRoleAsync(code)).StatusCode.ShouldBe(HttpStatusCode.NoContent, "the printed code works");
    }

    /// <summary>
    /// Design 6.2 (owner, 2026-11-16): a code session on a staff account becomes a staff session with the staff passkey,
    /// without signing out. The session gets a new key: the old row is gone, the old cookie leads nowhere, and the browser has
    /// one session, not two. It counts as a fresh confirmation, is audited and sends no email.
    /// </summary>
    [Fact]
    public async Task A_code_session_on_a_staff_account_becomes_a_staff_session_with_the_staff_passkey_under_a_new_key()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var (browser, cookies) = BrowserWithCookies();
        using var mailbox = browser;
        await SignInFromMailboxAsync(mailbox, admin.Email);
        var oldCookie = cookies.GetCookies(new Uri(Lt2Factory.Origin))[Lt2.Identity.IdentityModule.SessionCookieName]?.Value;
        oldCookie.ShouldNotBeNullOrEmpty();
        var sessionsBefore = (await mailbox.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!;
        var before = sessionsBefore.Single(s => s.IsCurrent);
        before.Method.ShouldBe("otp");
        (await mailbox.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var noticesBefore = await NoticesAsync(admin.Email);

        using (var started = await mailbox.StartStaffSessionAsync(admin.Authenticator))
        {
            started.StatusCode.ShouldBe(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());
        }

        var me = await MeAsync(mailbox);
        me.Email.ShouldBe(admin.Email);
        me.ActiveStaffRoles.ShouldBe(["administrator"]);
        (await mailbox.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.OK, "a staff-only answer works now");

        var sessionsAfter = (await mailbox.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!;
        sessionsAfter.Count.ShouldBe(sessionsBefore.Count, "one session for this browser, not two");
        sessionsAfter.ShouldNotContain(s => s.Id == before.Id);
        var after = sessionsAfter.Single(s => s.IsCurrent);
        after.Id.ShouldNotBe(before.Id, "a new session key on the change of privilege");
        after.Method.ShouldBe("pop");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.session WHERE id = @id", ("id", before.Id))).ShouldBe(0);
        var newCookie = cookies.GetCookies(new Uri(Lt2Factory.Origin))[Lt2.Identity.IdentityModule.SessionCookieName]?.Value;
        newCookie.ShouldNotBeNullOrEmpty();
        newCookie.ShouldNotBe(oldCookie);

        // The old cookie, sent by whoever might hold it, is a visitor's.
        using (var stale = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(Lt2Factory.Origin), HandleCookies = false, AllowAutoRedirect = false }))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Me.Root);
            request.Headers.Add("Cookie", $"{Lt2.Identity.IdentityModule.SessionCookieName}={oldCookie}");
            using var response = await stale.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Audited, with no email of its own; and the passkey counts as just confirmed (adding a passkey needs that).
        (await AuditCountAsync(admin.Email, "staff.session-started")).ShouldBe(1);
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = 'staff.session-started' AND (details::text LIKE '%@%' OR target LIKE '%@%')"))
            .ShouldBe(0, "no addresses in the audit log");
        (await NoticesAsync(admin.Email)).ShouldBe(noticesBefore);
        using var extra = new SoftwareAuthenticator();
        using (var added = await mailbox.AddPasskeyAsync(extra))
        {
            added.StatusCode.ShouldBe(HttpStatusCode.NoContent, await added.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Another_accounts_passkey_is_refused_and_the_session_stays_as_it_was()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var other = await StaffMembers.ActiveAsync(Factory, Database, "moderator");
        using var mailbox = NewBrowser();
        await SignInFromMailboxAsync(mailbox, admin.Email);
        var before = (await mailbox.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!.Single(s => s.IsCurrent);

        using (var refused = await mailbox.StartStaffSessionAsync(other.Authenticator))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.staff-required");
        }

        var me = await MeAsync(mailbox);
        me.Email.ShouldBe(admin.Email, "still the same account");
        me.ActiveStaffRoles.ShouldBeEmpty();
        var after = (await mailbox.Client.GetFromJsonAsync<List<SessionResponse>>(ApiRoutes.Me.Sessions))!.Single(s => s.IsCurrent);
        after.Id.ShouldBe(before.Id);
        after.Method.ShouldBe("otp");
        (await mailbox.Client.GetAsync(ApiRoutes.Admin.Settings)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuditCountAsync(admin.Email, "staff.session-started")).ShouldBe(0);
        (await AuditCountAsync(other.Email, "staff.session-started")).ShouldBe(0);
    }

    [Fact]
    public async Task A_passkey_of_the_staff_account_that_is_not_its_staff_passkey_is_refused_and_the_staff_passkey_works_from_it()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var extra = new SoftwareAuthenticator();
        (await admin.Browser.ConfirmPasskeyAsync(admin.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var added = await admin.Browser.AddPasskeyAsync(extra))
        {
            added.StatusCode.ShouldBe(HttpStatusCode.NoContent, await added.Content.ReadAsStringAsync());
        }

        using (var mailbox = NewBrowser())
        {
            await SignInFromMailboxAsync(mailbox, admin.Email);
            await (await mailbox.StartStaffSessionAsync(extra)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.staff-required");
            (await MeAsync(mailbox)).ActiveStaffRoles.ShouldBeEmpty();
        }

        // A session signed in with that other passkey is not a staff session either; the staff passkey makes it one.
        using var otherKey = NewBrowser();
        (await otherKey.SignInWithPasskeyAsync(extra)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MeAsync(otherKey)).ActiveStaffRoles.ShouldBeEmpty();
        (await otherKey.StartStaffSessionAsync(admin.Authenticator)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MeAsync(otherKey)).ActiveStaffRoles.ShouldBe(["administrator"]);
    }

    [Fact]
    public async Task A_member_without_a_staff_role_and_a_visitor_are_refused()
    {
        using var member = NewBrowser();
        var email = await member.RegisterAndSignInAsync();
        using var key = new SoftwareAuthenticator();
        (await member.AddPasskeyAsync(key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await (await member.StartStaffSessionAsync(key)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.staff-required");
        (await MeAsync(member)).ActiveStaffRoles.ShouldBeEmpty();
        (await AuditCountAsync(email, "staff.session-started")).ShouldBe(0);

        using var visitor = NewBrowser();
        using var anonymous = await visitor.StartStaffSessionAsync(key);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>A browser whose cookies the test can read (the old session cookie, for the key's change).</summary>
    private (TestBrowser Browser, CookieContainer Cookies) BrowserWithCookies()
    {
        var cookies = new CookieContainer();
        var client = Factory.CreateDefaultClient(new Uri(Lt2Factory.Origin), new CookieContainerHandler(cookies));
        client.DefaultRequestHeaders.Add("Origin", Lt2Factory.Origin);
        return (new TestBrowser(client, Factory), cookies);
    }

    private static async Task<MeResponse> MeAsync(TestBrowser browser) =>
        (await browser.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!;

    /// <summary>
    /// Asks for a code for the address, as anyone may, and signs in with it as whoever reads that mailbox would.
    /// </summary>
    private static async Task SignInFromMailboxAsync(TestBrowser browser, string email)
    {
        using (var request = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
            request.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await browser.SignInWithCodeAsync(email);
    }

    private Task AgeConfirmationAsync(string email, int minutes) =>
        Database.ExecuteAsync(
            $"UPDATE identity.session SET passkey_confirmed_at = now() - interval '{minutes} minutes' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
            ("e", email));

    private async Task<List<string>> NoticesAsync(string email)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT payload->>'notice' FROM notify.outbox WHERE kind = 'email.account-notice' AND payload->>'to' = @e ORDER BY created_at",
            connection);
        command.Parameters.AddWithValue("e", email);
        var notices = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notices.Add(reader.GetString(0));
        }

        return notices;
    }

    private Task<long> AuditCountAsync(string email, string action) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = @a AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e)",
            ("a", action),
            ("e", email));
}
