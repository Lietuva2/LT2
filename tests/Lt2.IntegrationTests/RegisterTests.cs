using System.Net;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.IntegrationTests.Infrastructure;

namespace Lt2.IntegrationTests;

/// <summary><c>POST /api/v1/auth/register</c>: email, invitation code and the 18+ confirmation (design 6.2).</summary>
public sealed class RegisterTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Registration_creates_an_account_and_emails_a_code_through_the_outbox()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();

        var code = await browser.RegisterAsync(email);

        code.ShouldMatch("^[0-9]{6}$");
        (await Database.ScalarAsync<string>(
            "SELECT p.verification_level FROM identity.profile p JOIN identity.\"user\" u ON u.id = p.user_id WHERE u.email = @e",
            ("e", email))).ShouldBe("none");
        (await Database.ScalarAsync<long>(
            "SELECT count(*) FROM identity.consent c JOIN identity.\"user\" u ON u.id = c.user_id WHERE u.email = @e AND c.kind = 'age-18-or-older'",
            ("e", email))).ShouldBe(1);
        (await Database.ScalarAsync<string>("SELECT password_hash FROM identity.\"user\" WHERE email = @e", ("e", email))).ShouldBeNull();
    }

    [Fact]
    public async Task Registering_an_existing_address_answers_the_same_and_changes_nothing()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        await browser.RegisterAsync(email);
        var usesBefore = await UsesLeftAsync();

        using var again = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = email.ToUpperInvariant(), InvitationCode = TestData.Invitation, ConfirmsAdult = true });

        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await again.Content.ReadAsStringAsync()).ShouldBeEmpty();
        (await UsesLeftAsync()).ShouldBe(usesBefore);
        (await Factory.OutboxCountAsync(email.ToUpperInvariant())).ShouldBe(1, "the existing account receives a sign-in code");
        (await Database.ScalarAsync<long>("SELECT count(*) FROM identity.profile WHERE normalized_email = @e", ("e", email.ToLowerInvariant())))
            .ShouldBe(1);
    }

    [Fact]
    public async Task One_account_per_normalised_address()
    {
        using var browser = NewBrowser();
        var local = "jonas.petraitis." + Guid.NewGuid().ToString("N")[..8];
        await browser.RegisterAsync($"{local}@gmail.com");

        using var alias = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = $"{local.Replace(".", "")}+antra@googlemail.com", InvitationCode = TestData.Invitation, ConfirmsAdult = true });

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
            new RegisterRequest { Email = TestData.Email(), InvitationCode = "NERA-TOKIO", ConfirmsAdult = true });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
    }

    [Fact]
    public async Task During_the_closed_trial_an_invitation_is_required()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = TestData.Email(), ConfirmsAdult = true });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.required");
    }

    [Fact]
    public async Task Registration_needs_the_eighteen_or_older_confirmation()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();
        using var response = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = email, InvitationCode = TestData.Invitation, ConfirmsAdult = false });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation");
        var problem = await response.ProblemAsync();
        problem.GetProperty("errors").GetProperty("confirmsAdult")[0].GetString()
            .ShouldBe("Registruotis gali tik 18 metų ir vyresni asmenys.");
        (await Factory.OutboxCountAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task Disposable_mailbox_addresses_are_refused()
    {
        using var browser = NewBrowser();
        using var response = await browser.PostAsync(
            ApiRoutes.Auth.Register,
            new RegisterRequest { Email = $"jonas.{Guid.NewGuid():N}@mailinator.com", InvitationCode = TestData.Invitation, ConfirmsAdult = true });
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "email.disposable");
    }

    [Fact]
    public async Task Registration_uses_up_one_use_of_the_invitation()
    {
        using var browser = NewBrowser();
        var before = await UsesLeftAsync();
        await browser.RegisterAsync(TestData.Email());
        (await UsesLeftAsync()).ShouldBe(before - 1);
    }

    private Task<int> UsesLeftAsync() =>
        Database.ScalarAsync<int>("SELECT uses_left FROM identity.invitation WHERE code = @c", ("c", TestData.Invitation));
}
