using System.Net;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Design 6.2 (decided 2026-11-13): personal invitation codes with counts. One code per member, used any number of times
/// unless a setting limits it, replaceable; counted at completed registration on the code and per day; the member sees
/// only the count; administrators see numbers per day, code and level and switch codes off and on; never combined with
/// sides.
/// </summary>
public sealed class InvitationTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static StaffMember? _administrator;

    private async Task<TestBrowser> AdministratorAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _administrator ??= await StaffMembers.ActiveAsync(Factory, Database, "administrator");
            return _administrator.Browser;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>A new person registers with <paramref name="code"/> and completes; returns the browser and address.</summary>
    private async Task<(TestBrowser Browser, string Email)> RegisterWithAsync(string code, string name = "Pakviestas Asmuo")
    {
        var browser = NewBrowser();
        var email = TestData.Email();
        var signInCode = await browser.RegisterAsync(email, code);
        await browser.SignInWithCodeAsync(email, signInCode);
        await browser.CompleteRegistrationAsync(name);
        return (browser, email);
    }

    private static Task<MyInvitationResponse> MineAsync(TestBrowser browser) => browser.GetJsonAsync<MyInvitationResponse>(ApiRoutes.Me.Invitation);

    [Fact]
    public async Task A_member_has_a_code_and_everyone_who_completes_registration_with_it_counts_once_in_any_spelling()
    {
        var (inviter, inviterEmail) = await Factory.MemberAsync("Kviečianti Narė");
        using var _i = inviter;
        var mine = await MineAsync(inviter);
        mine.Code.ShouldMatch("^[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$");
        mine.Link.ShouldBe("/prisijungti?kodas=" + mine.Code);
        (mine.Completed, mine.On, mine.Contact).ShouldBe((0, true, (string?)null));
        (await MineAsync(inviter)).Code.ShouldBe(mine.Code, "one code per member");

        // Typed in lower case, without the dash: the same code.
        var (first, _) = await RegisterWithAsync(mine.Code.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal));
        using var _f = first;
        var (second, secondEmail) = await RegisterWithAsync(mine.Code);
        using var _s = second;
        (await MineAsync(inviter)).Completed.ShouldBe(2);

        // A started but unfinished registration counts nothing.
        using (var unfinished = NewBrowser())
        {
            await unfinished.RegisterAsync(TestData.Email(), mine.Code);
        }

        (await MineAsync(inviter)).Completed.ShouldBe(2);
        (await Database.ScalarAsync<long>("SELECT coalesce(sum(completed), 0) FROM identity.invitation_daily d JOIN identity.invitation i ON i.id = d.invitation_id WHERE i.code = @c", ("c", mine.Code))).ShouldBe(2);

        // The invited people have codes one level below; the inviter's level comes from the team code.
        var levels = await Database.ScalarAsync<string>(
            "SELECT string_agg(i.level::text, ',' ORDER BY i.level) FROM identity.invitation i JOIN identity.\"user\" u ON u.id = i.issued_by_user_id WHERE u.email IN (@a, @b)",
            ("a", inviterEmail), ("b", secondEmail));
        levels.ShouldBe("2,3");

        // When an invited person deletes their account, the count does not drop.
        (await second.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MineAsync(inviter)).Completed.ShouldBe(2);
    }

    [Fact]
    public async Task Replacing_a_code_stops_the_old_one_and_keeps_the_count_and_a_setting_can_limit_personal_codes()
    {
        var admin = await AdministratorAsync();
        var (inviter, _) = await Factory.MemberAsync("Keičianti Narė");
        using var _i = inviter;
        var old = (await MineAsync(inviter)).Code;
        var (one, _) = await RegisterWithAsync(old);
        using var _o = one;

        using (var replaced = await inviter.PostAsync(ApiRoutes.Me.ReplaceInvitation, new { }))
        {
            replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());
        }

        var mine = await MineAsync(inviter);
        mine.Code.ShouldNotBe(old);
        mine.Completed.ShouldBe(1, "the count carries over");
        using (var stale = NewBrowser())
        using (var refused = await stale.PostAsync(ApiRoutes.Auth.Register, new Lt2.Api.Contracts.Auth.RegisterRequest { Email = TestData.Email(), InvitationCode = old }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
        }

        await SetAsync(admin, "Registration:PersonalCodeUses", 1);
        try
        {
            var (two, _) = await RegisterWithAsync(mine.Code);
            using var _t = two;
            using var third = NewBrowser();
            using var refused = await third.PostAsync(ApiRoutes.Auth.Register, new Lt2.Api.Contracts.Auth.RegisterRequest { Email = TestData.Email(), InvitationCode = mine.Code });
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
        }
        finally
        {
            await SetAsync(admin, "Registration:PersonalCodeUses", 0);
        }
    }

    [Fact]
    public async Task An_administrator_sees_numbers_only_and_switches_a_code_off_and_on_with_a_statement()
    {
        var admin = await AdministratorAsync();
        var (inviter, _) = await Factory.MemberAsync("Statistikos Narė");
        using var _i = inviter;
        var code = (await MineAsync(inviter)).Code;
        var (invited, invitedEmail) = await RegisterWithAsync(code, "Slaptas Pakviestasis");
        using var _v = invited;

        // Not for members, nor for an administrator's code session.
        (await inviter.Client.GetAsync(ApiRoutes.Admin.Invitations)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var stranger = NewBrowser();
        (await stranger.Client.GetAsync(ApiRoutes.Admin.Invitations)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var answer = await admin.Client.GetAsync(ApiRoutes.Admin.Invitations + "?code=" + code);
        var json = await answer.Content.ReadAsStringAsync();
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, json);
        var stats = JsonSerializer.Deserialize<InvitationStatsResponse>(json, JsonSerializerOptions.Web)!;
        var row = stats.Codes.Single();
        (row.Kind, row.Label, row.Completed, row.On).ShouldBe(("personal", "Statistikos Narė", 1, true));
        row.Days.Sum(d => d.Completed).ShouldBe(1);
        stats.Levels.ShouldContain(l => l.Level == 2 && l.Completed >= 1);

        // Numbers only: nothing of the invited person, and nothing about sides.
        json.ShouldNotContain("Slaptas Pakviestasis");
        json.ShouldNotContain(invitedEmail, Case.Insensitive);
        json.ShouldNotContain((await Database.UserIdAsync(invitedEmail)).ToString(), Case.Insensitive);
        foreach (var word in new[] { "\"for\"", "\"against\"", "side", "position", "reason" })
        {
            json.ShouldNotContain(word, Case.Insensitive);
        }

        using (var noStatement = await admin.PostAsync(ApiRoutes.Admin.SwitchInvitation(row.Id, on: false), new StatementRequest { Statement = " " }))
        {
            noStatement.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await admin.PostAsync(ApiRoutes.Admin.SwitchInvitation(row.Id, on: false), new StatementRequest { Statement = "Kodas išplatintas viešai." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var off = await MineAsync(inviter);
        (off.On, off.Contact).ShouldBe((false, "moderatoriai@lt2.test"));
        using (var refused = await stranger.PostAsync(ApiRoutes.Auth.Register, new Lt2.Api.Contracts.Auth.RegisterRequest { Email = TestData.Email(), InvitationCode = code }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
        }

        using (var replace = await inviter.PostAsync(ApiRoutes.Me.ReplaceInvitation, new { }))
        {
            await replace.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invitation.switched-off");
        }

        (await admin.PostAsync(ApiRoutes.Admin.SwitchInvitation(row.Id, on: true), new StatementRequest { Statement = "Patikrinta." })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MineAsync(inviter)).On.ShouldBeTrue();
        (await Database.ScalarAsync<long>("SELECT count(*) FROM audit.event WHERE action IN ('invitation.switched-off', 'invitation.switched-on') AND target = @t", ("t", "invitation:" + row.Id))).ShouldBe(2);
    }

    [Fact]
    public async Task When_the_inviter_deletes_the_account_the_code_stops_and_keeps_nothing_of_them_and_export_shows_own_codes()
    {
        var admin = await AdministratorAsync();
        var (inviter, inviterEmail) = await Factory.MemberAsync("Išeinanti Kviečiančioji");
        using var _i = inviter;
        var code = (await MineAsync(inviter)).Code;
        var (invited, _) = await RegisterWithAsync(code);
        using var _v = invited;

        using (var export = await invited.Client.GetAsync(ApiRoutes.Me.Export))
        {
            var text = await export.Content.ReadAsStringAsync();
            text.ShouldContain(code, customMessage: "the code they registered with");
            text.ShouldNotContain("Išeinanti Kviečiančioji", customMessage: "not the inviter's name");
            text.ShouldContain("myInvitation");
        }

        using (var export = await inviter.Client.GetAsync(ApiRoutes.Me.Export))
        {
            using var document = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
            var own = document.RootElement.GetProperty("account").GetProperty("myInvitation");
            own.GetProperty("code").GetString().ShouldBe(code);
            own.GetProperty("completed").GetInt32().ShouldBe(1);
        }

        var userId = await Database.UserIdAsync(inviterEmail);
        (await inviter.SendAsync(HttpMethod.Delete, ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.invitation WHERE issued_by_user_id = @u", ("u", userId))).ShouldBe(0);
        (await Database.ScalarAsync<int>("SELECT used_count FROM identity.invitation WHERE code = @c", ("c", code))).ShouldBe(1, "the number stays");
        using var stranger = NewBrowser();
        using (var refused = await stranger.PostAsync(ApiRoutes.Auth.Register, new Lt2.Api.Contracts.Auth.RegisterRequest { Email = TestData.Email(), InvitationCode = code }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
        }

        var id = await Database.ScalarAsync<Guid>("SELECT id FROM identity.invitation WHERE code = @c", ("c", code));
        using var on = await admin.PostAsync(ApiRoutes.Admin.SwitchInvitation(id, on: true), new StatementRequest { Statement = "Bandymas." });
        await on.ShouldBeProblemAsync(HttpStatusCode.Conflict, "invitation.cannot-switch-on");
        var stats = await admin.GetJsonAsync<InvitationStatsResponse>(ApiRoutes.Admin.Invitations + "?code=" + code);
        stats.Codes.Single().Label.ShouldBeNull("nothing of the deleted inviter stays on the code");
    }

    private static async Task SetAsync(TestBrowser admin, string key, object value)
    {
        using var response = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(key), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(value) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
