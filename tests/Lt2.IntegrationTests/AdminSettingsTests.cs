using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Auth;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary>
/// Runtime settings (design 5.6) through the minimal administrator endpoint. A staff role works only in a session signed
/// in with the staff passkey (design 6.2): an administrator signed in with an emailed code is a member there.
/// </summary>
public sealed class AdminSettingsTests(DatabaseFixture database) : ApiTest(database)
{
    private static readonly string Key = SharedKernelKeys.RequireInvitation;

    [Fact]
    public async Task Strangers_are_asked_to_sign_in()
    {
        using var browser = NewBrowser();
        using var response = await browser.Client.GetAsync(ApiRoutes.Admin.Settings);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Members_are_refused()
    {
        using var browser = NewBrowser();
        await browser.RegisterAndSignInAsync();
        using var response = await browser.Client.GetAsync(ApiRoutes.Admin.Settings);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task An_active_administrator_signed_in_with_a_code_is_refused()
    {
        using var admin = await StaffMembers.SharedAsync(Factory, Database, "administrator");
        using var browser = NewBrowser();
        await SignInWithCodeAsync(browser, admin.Email);

        using var response = await browser.Client.GetAsync(ApiRoutes.Admin.Settings);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task An_administrator_with_a_passkey_changes_a_setting_and_the_change_is_audited()
    {
        using var staff = await StaffMembers.SharedAsync(Factory, Database, "administrator");
        var admin = staff.Browser;

        var settings = (await admin.Client.GetFromJsonAsync<List<SettingResponse>>(ApiRoutes.Admin.Settings))!;
        settings.Single(s => s.Key == Key).Value.GetBoolean().ShouldBeTrue();
        settings.Single(s => s.Key == "Digest:ThresholdDefault").Value.GetInt32().ShouldBe(100);
        settings.Single(s => s.Key == "Digest:ThresholdMinimum").Default!.Value.GetInt32().ShouldBe(50, "the screen shows each setting's default");
        settings.ShouldAllBe(s => s.Default.HasValue);

        using (var change = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(Key), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(false) }))
        {
            change.StatusCode.ShouldBe(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());
        }

        try
        {
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'setting.changed' AND target = @k AND actor_role = 'administrator' AND ip_hash IS NOT NULL",
                ("k", Key))).ShouldBeGreaterThanOrEqualTo(1);

            // With the setting off, registration needs no invitation.
            using var stranger = NewBrowser();
            using var register = await stranger.PostAsync(
                ApiRoutes.Auth.Register,
                new RegisterRequest { Email = TestData.Email() });
            register.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }
        finally
        {
            using var restore = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(Key), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(true) });
            restore.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task The_code_limits_are_settings_whose_changes_are_audited_and_apply_at_once()
    {
        using var staff = await StaffMembers.SharedAsync(Factory, Database, "administrator");
        var admin = staff.Browser;
        var perHour = Lt2.SharedKernel.SettingKeys.SignInCodesPerHour.Name;
        var settings = (await admin.Client.GetFromJsonAsync<List<SettingResponse>>(ApiRoutes.Admin.Settings))!;
        settings.Single(s => s.Key == perHour).Value.GetInt32().ShouldBe(5);
        settings.Single(s => s.Key == Lt2.SharedKernel.SettingKeys.SignInCodesPerDay.Name).Value.GetInt32().ShouldBe(10);

        using (var change = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(perHour), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(2) }))
        {
            change.StatusCode.ShouldBe(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());
        }

        try
        {
            (await Database.ScalarAsync<long>(
                "SELECT count(*) FROM audit.event WHERE action = 'setting.changed' AND target = @k AND actor_role = 'administrator'",
                ("k", perHour))).ShouldBeGreaterThanOrEqualTo(1);

            using var browser = NewBrowser();
            var email = TestData.Email();
            await browser.RegisterAsync(email);
            for (var i = 0; i < 2; i++)
            {
                using var request = await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email });
                request.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            }

            (await Factory.OutboxCountAsync(email)).ShouldBe(2);
        }
        finally
        {
            using var restore = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey(perHour), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(5) });
            restore.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Unknown_settings_and_wrong_values_are_refused()
    {
        using var staff = await StaffMembers.SharedAsync(Factory, Database, "administrator");
        var admin = staff.Browser;

        using var unknown = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey("Nera:Tokio"), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement(1) });
        await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, "setting.unknown");

        using var wrong = await admin.SendAsync(HttpMethod.Put, ApiRoutes.Admin.SettingByKey("Digest:MaxPerWeek"), new UpdateSettingRequest { Value = JsonSerializer.SerializeToElement("daug") });
        await wrong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "setting.invalid");
    }

    private async Task SignInWithCodeAsync(TestBrowser browser, string email)
    {
        using (await browser.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
        }

        using var signIn = await browser.PostAsync(ApiRoutes.Auth.SignIn, new SignInRequest { Email = email, Code = (await Factory.LatestCodeAsync(email))! });
        signIn.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static class SharedKernelKeys
    {
        public static readonly string RequireInvitation = Lt2.SharedKernel.SettingKeys.RegistrationRequiresInvitation.Name;
    }
}
