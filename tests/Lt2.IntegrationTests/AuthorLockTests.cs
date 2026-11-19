using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 8.3, "A full lock", reached from an item: an administrator locks the account of whoever wrote an argument, a comment, an
/// organisation's statement or a reported profile (the server finds the person; no address is sent or shown), sees the locked accounts
/// by name, day and statement, and unlocks by id. Never a moderator; the same refusals as by address.
/// </summary>
public sealed class AuthorLockTests(DatabaseFixture database) : ApiTest(database)
{
    private const string Bill = AppealSteps.ThirdBill;
    private const string Statement = "Piktnaudžiavimas: pakartotinai skelbiami kitų asmenų duomenys.";

    private static Task<HttpResponseMessage> LockAuthorAsync(StaffMember admin, string kind, Guid id, string statement = Statement) =>
        admin.Browser.PostAsync(ApiRoutes.Admin.LockAuthor, new AuthorLockRequest { TargetKind = kind, TargetId = id, Statement = statement });

    private static Task<HttpResponseMessage> UnlockByIdAsync(StaffMember admin, Guid id, string statement = "Klaida ištaisyta.") =>
        admin.Browser.PostAsync(ApiRoutes.Admin.UnlockOf(id), new StatementRequest { Statement = statement });

    private static Task<List<LockedAccountResponse>> LockedAsync(StaffMember admin) =>
        admin.Browser.GetJsonAsync<List<LockedAccountResponse>>(ApiRoutes.Admin.LockedAccounts);

    private Task<long> AuditAsync(string email, string action) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM audit.event WHERE action = @a AND target = (SELECT id::text FROM identity.\"user\" WHERE email = @e)",
            ("a", action), ("e", email));

    [Fact]
    public async Task Locking_from_an_argument_ends_the_authors_sessions_and_sign_in_lists_the_account_without_its_address_and_unlocking_by_id_restores_it()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var _a = bill.ArgumentAuthor;
        using var _r = bill.Representative;
        try
        {
            using (var asked = await NewBrowser().PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = bill.ArgumentAuthorEmail }))
            {
                asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            var codeBefore = await Factory.LatestCodeAsync(bill.ArgumentAuthorEmail);
            using (var locked = await LockAuthorAsync(admin, LockTargets.Reason, bill.ReasonId))
            {
                locked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await locked.Content.ReadAsStringAsync());
                (await locked.Content.ReadAsStringAsync()).ShouldBeEmpty("nothing about the person comes back");
            }

            (await bill.ArgumentAuthor.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the session ended at once");
            using var stranger = NewBrowser();
            using (var typed = await stranger.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = bill.ArgumentAuthorEmail, Code = codeBefore! }))
            {
                await typed.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "auth.code-invalid");
            }

            (await AuditAsync(bill.ArgumentAuthorEmail, "account.locked")).ShouldBe(1);
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action IN ('account.locked', 'account.unlocked') AND (details::text LIKE '%@%' OR target LIKE '%@%')"))
                .ShouldBe(0, "no address in the audit log");
            using (var told = JsonDocument.Parse((await Database.LatestEmailAsync("email.account-notice", bill.ArgumentAuthorEmail))!))
            {
                told.RootElement.GetProperty("notice").GetString().ShouldBe("account-locked");
                told.RootElement.GetProperty("statement").GetString().ShouldBe(Statement);
            }

            // The list: the name, the day and the statement, and never an address.
            var json = await admin.Browser.Client.GetStringAsync(ApiRoutes.Admin.LockedAccounts);
            json.ShouldNotContain(bill.ArgumentAuthorEmail, Case.Insensitive);
            json.ShouldNotContain("@");
            var authorId = await Database.UserIdAsync(bill.ArgumentAuthorEmail);
            var entry = (await LockedAsync(admin)).Single(a => a.Id == authorId);
            entry.DisplayName.ShouldBe("Rūta Mockutė");
            entry.Statement.ShouldBe(Statement);
            (DateTimeOffset.UtcNow - entry.LockedAt).ShouldBeLessThan(TimeSpan.FromMinutes(5));

            using (var again = await LockAuthorAsync(admin, LockTargets.Reason, bill.ReasonId))
            {
                await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "account.locked");
            }

            using (var unlocked = await UnlockByIdAsync(admin, entry.Id))
            {
                unlocked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await unlocked.Content.ReadAsStringAsync());
            }

            (await LockedAsync(admin)).ShouldNotContain(a => a.Id == entry.Id);
            (await AuditAsync(bill.ArgumentAuthorEmail, "account.unlocked")).ShouldBe(1);
            using (var back = JsonDocument.Parse((await Database.LatestEmailAsync("email.account-notice", bill.ArgumentAuthorEmail))!))
            {
                back.RootElement.GetProperty("notice").GetString().ShouldBe("account-unlocked");
            }

            using (var notLocked = await UnlockByIdAsync(admin, entry.Id))
            {
                await notLocked.ShouldBeProblemAsync(HttpStatusCode.Conflict, "account.not-locked");
            }

            using var codeBrowser = NewBrowser();
            using (var asked = await codeBrowser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = bill.ArgumentAuthorEmail }))
            {
                asked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            using (var signedIn = await codeBrowser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = bill.ArgumentAuthorEmail, Code = (await Factory.LatestCodeAsync(bill.ArgumentAuthorEmail))! }))
            {
                signedIn.StatusCode.ShouldBe(HttpStatusCode.OK, await signedIn.Content.ReadAsStringAsync());
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task A_comments_writer_an_organisations_statements_writer_and_a_reported_profile_are_locked_the_same_way()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var _a = bill.ArgumentAuthor;
        using var _r = bill.Representative;
        var (commenter, commenterEmail) = await Factory.MemberAsync("Komentuojantis Narys");
        using var _c = commenter;
        var (profileOwner, profileEmail, profileId) = await Factory.PublicMemberAsync("Pranešta Narė", "Esu Seimo narys.");
        using var _p = profileOwner;
        var (reporter, _) = await Factory.MemberAsync("Pranešėja");
        using var _rep = reporter;
        try
        {
            var comment = await commenter.CommentOkAsync("reason", bill.ReasonId, "Komentaras, dėl kurio užrakinama.");
            using (var locked = await LockAuthorAsync(admin, LockTargets.Comment, comment))
            {
                locked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await locked.Content.ReadAsStringAsync());
            }

            (await commenter.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            var lockedBefore = (await LockedAsync(admin)).Select(a => a.Id).ToHashSet();
            using (var locked = await LockAuthorAsync(admin, LockTargets.Statement, bill.StatementId))
            {
                locked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await locked.Content.ReadAsStringAsync());
            }

            var representative = (await LockedAsync(admin)).Single(a => !lockedBefore.Contains(a.Id));
            representative.DisplayName.ShouldBe("Tomas Atstovas");

            (await bill.Representative.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the person who wrote the statement, not the organisation");

            using (var reported = await reporter.PostAsync(ProfileRoutes.ReportOf(profileId), new MemberReportRequest { Ground = "impersonation", Text = "Ne Seimo narys." }))
            {
                reported.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            var item = (await admin.Browser.GetJsonAsync<List<ProfileModerationResponse>>(ProfileRoutes.Moderation)).Single(p => p.ProfileId == profileId);
            using (var locked = await LockAuthorAsync(admin, LockTargets.Profile, item.Id))
            {
                locked.StatusCode.ShouldBe(HttpStatusCode.NoContent, await locked.Content.ReadAsStringAsync());
            }

            (await profileOwner.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            var ids = new[] { await Database.UserIdAsync(commenterEmail), representative.Id, await Database.UserIdAsync(profileEmail) };
            var listed = await LockedAsync(admin);
            foreach (var id in ids)
            {
                listed.ShouldContain(a => a.Id == id);
            }

            (await admin.Browser.Client.GetStringAsync(ApiRoutes.Admin.LockedAccounts)).ShouldNotContain("@");
            foreach (var email in new[] { commenterEmail, profileEmail })
            {
                (await AuditAsync(email, "account.locked")).ShouldBe(1);
            }

            foreach (var id in ids)
            {
                using var unlocked = await UnlockByIdAsync(admin, id);
                unlocked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task Only_an_administrator_with_a_fresh_confirmation_locks_from_an_item_and_a_statement_and_a_real_item_are_required()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        var moderator = await ModeratorAsync();
        var bill = await Factory.PrepareCommentedBillAsync(Database, moderator.Browser, Bill);
        using var _a = bill.ArgumentAuthor;
        using var _r = bill.Representative;
        try
        {
            var body = new AuthorLockRequest { TargetKind = LockTargets.Reason, TargetId = bill.ReasonId, Statement = Statement };
            using (var byModerator = await moderator.Browser.PostAsync(ApiRoutes.Admin.LockAuthor, body))
            {
                await byModerator.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
            }

            using (var listed = await moderator.Browser.Client.GetAsync(ApiRoutes.Admin.LockedAccounts))
            {
                await listed.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
            }

            using (var unlocked = await moderator.Browser.PostAsync(ApiRoutes.Admin.UnlockOf(Guid.NewGuid()), new StatementRequest { Statement = "Bandymas." }))
            {
                await unlocked.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
            }

            using (var byMember = await bill.Representative.PostAsync(ApiRoutes.Admin.LockAuthor, body))
            {
                byMember.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            }

            using var visitor = NewBrowser();
            using (var anonymous = await visitor.PostAsync(ApiRoutes.Admin.LockAuthor, body))
            {
                anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            using (var empty = await LockAuthorAsync(admin, LockTargets.Reason, bill.ReasonId, "  "))
            {
                empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            // An MP's response (or anything else the restriction does not reach) has no author to lock.
            using (var kind = await LockAuthorAsync(admin, "response", bill.ReasonId))
            {
                kind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            }

            using (var none = await LockAuthorAsync(admin, LockTargets.Reason, Guid.NewGuid()))
            {
                await none.ShouldBeProblemAsync(HttpStatusCode.NotFound, "reason.not-found");
            }

            using (var noStatement = await LockAuthorAsync(admin, LockTargets.Statement, Guid.NewGuid()))
            {
                await noStatement.ShouldBeProblemAsync(HttpStatusCode.NotFound, "statement.not-found");
            }

            await Database.ExecuteAsync(
                "UPDATE identity.session SET passkey_confirmed_at = now() - interval '30 minutes' WHERE user_id = (SELECT id FROM identity.\"user\" WHERE email = @e)",
                ("e", admin.Email));
            using (var stale = await LockAuthorAsync(admin, LockTargets.Reason, bill.ReasonId))
            {
                await stale.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
            }

            using (var staleUnlock = await UnlockByIdAsync(admin, Guid.NewGuid()))
            {
                await staleUnlock.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "passkey.confirmation-required");
            }

            (await bill.ArgumentAuthor.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.OK, "nothing was locked");
            (await AuditAsync(bill.ArgumentAuthorEmail, "account.locked")).ShouldBe(0);
        }
        finally
        {
            await Database.FreeBillAsync(Bill);
        }
    }

    [Fact]
    public async Task Oneself_and_a_staff_account_with_an_active_role_are_refused_from_an_item_too()
    {
        using var admin = await StaffMembers.ActiveAsync(Factory, Database, "administrator");
        using var staff = await StaffMembers.ActiveAsync(Factory, Database, "moderator");

        using (var self = await LockAuthorAsync(admin, LockTargets.Profile, await Database.UserIdAsync(admin.Email)))
        {
            await self.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "account.lock-self");
        }

        using (var role = await LockAuthorAsync(admin, LockTargets.Profile, await Database.UserIdAsync(staff.Email)))
        {
            await role.ShouldBeProblemAsync(HttpStatusCode.Conflict, "account.lock-staff");
        }

        using (var unknown = await LockAuthorAsync(admin, LockTargets.Profile, Guid.NewGuid()))
        {
            await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "account.not-found");
        }

        (await LockedAsync(admin)).ShouldNotContain(a => a.Id == Guid.Empty);
        (await AuditAsync(admin.Email, "account.locked")).ShouldBe(0);
        (await AuditAsync(staff.Email, "account.locked")).ShouldBe(0);
    }
}