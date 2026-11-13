using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Audit;
using Lt2.Identity;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.IntegrationTests;

/// <summary>
/// Registration (design 6.2): <c>POST /api/v1/auth/register</c> creates a pending account from the address and the
/// invitation code; <c>PUT /api/v1/me/registration</c> completes it once the address is proven.
/// </summary>
public sealed class RegisterTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Registration_creates_a_pending_account_with_nothing_but_the_address_and_the_invitation_code()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        var usesBefore = await UsesLeftAsync();

        var code = await browser.RegisterAsync(email);

        code.ShouldMatch("^[0-9]{6}$");
        (await ProfileAsync<string>("verification_level", email)).ShouldBe("none");
        (await ProfileAsync<string>("display_name", email)).ShouldBeNull();
        (await ProfileAsync<string>("invitation_code", email)).ShouldBe(TestData.Invitation);
        (await ProfileAsync<DateTime?>("registered_at", email)).ShouldBeNull();
        (await ConsentsAsync(email)).ShouldBe(0);
        (await RolesAsync(email)).ShouldBe(0);
        (await UsesLeftAsync()).ShouldBe(usesBefore, "the invitation use is counted only on completion");
        (await Database.ScalarAsync<string>("SELECT password_hash FROM identity.\"user\" WHERE email = @e", ("e", email))).ShouldBeNull();
    }

    [Fact]
    public async Task A_stranger_who_registers_someone_elses_address_sets_nothing_the_owner_finds_later()
    {
        var email = TestData.Email();
        using (var stranger = NewBrowser())
        {
            await stranger.RegisterAsync(email);
        }

        // The owner reads the code in their own mailbox and signs in from their own browser.
        using var owner = NewBrowser();
        await owner.SignInWithCodeAsync(email);
        var pending = (await owner.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!;
        pending.RegistrationComplete.ShouldBeFalse();
        pending.DisplayName.ShouldBeNull();
        pending.Roles.ShouldBeEmpty();
        (await ConsentsAsync(email)).ShouldBe(0);

        using (var complete = await owner.SendAsync(
            HttpMethod.Put,
            ApiRoutes.Me.Registration,
            new CompleteRegistrationRequest { DisplayName = "  Rūta M.  ", ConfirmsAdult = true }))
        {
            complete.StatusCode.ShouldBe(HttpStatusCode.NoContent, await complete.Content.ReadAsStringAsync());
        }

        var me = (await owner.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!;
        me.RegistrationComplete.ShouldBeTrue();
        me.DisplayName.ShouldBe("Rūta M.");
        me.Roles.ShouldBe(["member"]);
        me.VerificationLevel.ShouldBe("email");
        (await ConsentsAsync(email)).ShouldBe(1);
    }

    [Fact]
    public async Task Addresses_nobody_confirms_do_not_use_up_the_invitation()
    {
        var usesBefore = await UsesLeftAsync();
        using var browser = NewBrowser();
        for (var i = 0; i < 3; i++)
        {
            await browser.RegisterAsync(TestData.Email("ona.jonaitiene"));
        }

        (await UsesLeftAsync()).ShouldBe(usesBefore);

        using var person = NewBrowser();
        await person.RegisterAndSignInAsync();
        (await UsesLeftAsync()).ShouldBe(usesBefore - 1, "counted once, when the registration was completed");
    }

    [Fact]
    public async Task An_invitation_that_ran_out_before_completion_is_refused_and_another_code_can_be_entered()
    {
        var scarce = "VIENAS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await Database.ExecuteAsync(
            "INSERT INTO identity.invitation (id, code, uses_left, created_at) VALUES (gen_random_uuid(), @c, 1, now())",
            ("c", scarce));

        using var first = NewBrowser();
        var firstEmail = TestData.Email();
        await first.RegisterAsync(firstEmail, scarce);
        using var second = NewBrowser();
        var secondEmail = TestData.Email("ona.jonaitiene");
        await second.RegisterAsync(secondEmail, scarce);

        await first.SignInWithCodeAsync(firstEmail);
        await first.CompleteRegistrationAsync();

        await second.SignInWithCodeAsync(secondEmail);
        using (var refused = await second.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { ConfirmsAdult = true }))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid"); // one message for every code that does not work (design 6.2)
            var problem = await refused.ProblemAsync();
            problem.GetProperty("errors").GetProperty("invitationCode")[0].GetString()
                .ShouldBe("Šis kvietimo kodas negalioja. Paprašykite jus pakvietusio žmogaus naujo kodo.");
        }

        (await ProfileAsync<DateTime?>("registered_at", secondEmail)).ShouldBeNull("nothing was stored");

        using (var another = await second.SendAsync(
            HttpMethod.Put,
            ApiRoutes.Me.Registration,
            new CompleteRegistrationRequest { ConfirmsAdult = true, InvitationCode = TestData.Invitation }))
        {
            another.StatusCode.ShouldBe(HttpStatusCode.NoContent, await another.Content.ReadAsStringAsync());
        }

        (await ProfileAsync<string>("invitation_code", secondEmail)).ShouldBe(TestData.Invitation);
    }

    [Fact]
    public async Task Registering_an_address_that_is_still_pending_replaces_its_invitation_code_and_sends_a_code()
    {
        var other = "KITAS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await Database.ExecuteAsync(
            "INSERT INTO identity.invitation (id, code, uses_left, created_at) VALUES (gen_random_uuid(), @c, 5, now())",
            ("c", other));
        using var browser = NewBrowser();
        var email = TestData.Email();
        await browser.RegisterAsync(email);

        using var again = await browser.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = email, InvitationCode = other.ToLowerInvariant() });

        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await again.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await ProfileAsync<string>("invitation_code", email)).ShouldBe(other);
        (await Factory.OutboxCountAsync(email)).ShouldBe(2);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.profile WHERE normalized_email = @e", ("e", email.ToLowerInvariant()))).ShouldBe(1);
    }

    [Fact]
    public async Task Registering_an_address_with_a_completed_account_only_sends_a_code_to_the_address_on_the_account()
    {
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync();
        var usesBefore = await UsesLeftAsync();

        using var again = await NewBrowser().PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = email.ToUpperInvariant(), InvitationCode = TestData.Invitation });

        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await again.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await UsesLeftAsync()).ShouldBe(usesBefore);
        (await Factory.OutboxCountAsync(email)).ShouldBe(2, "the existing account receives a sign-in code");
        (await Factory.OutboxCountAsync(email.ToUpperInvariant())).ShouldBe(0, "what was typed is discarded");
        (await ProfileAsync<DateTime?>("registered_at", email)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Asking_for_a_code_for_a_pending_account_works_like_any_other_and_leads_to_completion()
    {
        var email = TestData.Email();
        using (var first = NewBrowser())
        {
            await first.RegisterAsync(email);
        }

        using var later = NewBrowser();
        using (var request = await later.PostAsync(ApiRoutes.Auth.Code, new RequestCodeRequest { Email = email }))
        {
            request.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await later.SignInWithCodeAsync(email);
        (await later.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!.RegistrationComplete.ShouldBeFalse();
        await later.CompleteRegistrationAsync();
        (await later.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!.RegistrationComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task A_pending_account_can_see_and_end_its_sessions_and_sign_out_but_nothing_a_member_can()
    {
        using var browser = NewBrowser();
        await browser.RegisterAndSignInAsync(complete: false);

        (await browser.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.Client.GetAsync(ApiRoutes.Me.Sessions)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var passkey = await browser.PostAsync(ApiRoutes.Auth.PasskeyCreationOptions))
        {
            await passkey.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
        }

        using var signOut = await browser.PostAsync(ApiRoutes.Auth.SignOut);
        signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await browser.Client.GetAsync(ApiRoutes.Me.Root)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Completion_needs_the_eighteen_or_older_declaration()
    {
        using var browser = NewBrowser();
        var email = await browser.RegisterAndSignInAsync(complete: false);

        using var response = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { DisplayName = "Rūta M." });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        var problem = await response.ProblemAsync();
        problem.GetProperty("errors").GetProperty("confirmsAdult")[0].GetString().ShouldBe("Registruotis gali tik 18 metų ir vyresni asmenys.");
        (await ProfileAsync<string>("display_name", email)).ShouldBeNull();
    }

    [Fact]
    public async Task A_registration_is_completed_only_once()
    {
        using var browser = NewBrowser();
        await browser.RegisterAndSignInAsync();
        var usesBefore = await UsesLeftAsync();

        using var again = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { ConfirmsAdult = true });

        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "registration.complete");
        (await UsesLeftAsync()).ShouldBe(usesBefore);
    }

    [Fact]
    public async Task Completion_needs_a_signed_in_person()
    {
        using var browser = NewBrowser();
        using var response = await browser.SendAsync(HttpMethod.Put, ApiRoutes.Me.Registration, new CompleteRegistrationRequest { ConfirmsAdult = true });
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Accounts_still_pending_after_seven_days_are_deleted_by_the_maintenance_job()
    {
        var stale = TestData.Email();
        var recent = TestData.Email("ona.jonaitiene");
        using (var browser = NewBrowser())
        {
            await browser.RegisterAsync(stale);
            await browser.RegisterAsync(recent);
        }

        using var member = NewBrowser();
        var completed = await member.RegisterAndSignInAsync();
        await Database.ExecuteAsync(
            "UPDATE identity.profile SET created_at = now() - interval '8 days' WHERE normalized_email = ANY(@e)",
            ("e", new[] { stale.ToLowerInvariant(), completed.ToLowerInvariant() }));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(NpgsqlDataSource.Create(Database.WorkerConnectionString));
        services.AddAuditModule();
        services.AddIdentityMaintenance();
        await using (var provider = services.BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIdentityMaintenance>().PurgeExpiredAsync(CancellationToken.None);
        }

        (await AccountsAsync(stale)).ShouldBe(0, "pending for eight days");
        (await AccountsAsync(recent)).ShouldBe(1, "pending, but recent");
        (await AccountsAsync(completed)).ShouldBe(1, "completed accounts are kept");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.\"user\" WHERE email = @e", ("e", stale))).ShouldBe(0);
    }

    [Fact]
    public async Task One_account_per_normalised_address()
    {
        using var browser = NewBrowser();
        var local = "ruta.mockute." + Guid.NewGuid().ToString("N")[..8];
        await browser.RegisterAsync($"{local}@gmail.com");

        using var alias = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = $"{local.Replace(".", "")}+antra@googlemail.com", InvitationCode = TestData.Invitation });

        alias.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.profile WHERE normalized_email = @e", ("e", $"{local.Replace(".", "")}@gmail.com")))
            .ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_invitation_code_is_refused()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = TestData.Email(), InvitationCode = "NERA-TOKIO" });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
    }

    [Fact]
    public async Task During_the_closed_trial_an_invitation_is_required()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = TestData.Email() });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.required");
    }

    [Fact]
    public async Task Disposable_mailbox_addresses_are_refused()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = $"ruta.{Guid.NewGuid():N}@mailinator.com", InvitationCode = TestData.Invitation });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "email.disposable");
    }

    private Task<int> UsesLeftAsync() =>
        Database.ScalarAsync<int>("SELECT uses_left FROM identity.invitation WHERE code = @c", ("c", TestData.Invitation));

    private Task<T?> ProfileAsync<T>(string column, string email) =>
        Database.ScalarAsync<T>($"SELECT {column} FROM identity.profile WHERE normalized_email = @e", ("e", email.ToLowerInvariant()));

    private Task<long> AccountsAsync(string email) =>
        Database.ScalarAsync<long>("SELECT count(*) FROM identity.profile WHERE normalized_email = @e", ("e", email.ToLowerInvariant()));

    private Task<long> ConsentsAsync(string email) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e",
            ("e", email));

    private Task<long> RolesAsync(string email) =>
        Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.user_role r JOIN identity.\"user\" u ON u.id = r.user_id WHERE u.email = @e",
            ("e", email));
}
