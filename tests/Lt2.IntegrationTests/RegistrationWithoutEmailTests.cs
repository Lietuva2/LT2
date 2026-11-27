using System.Net;
using System.Net.Http.Json;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace Lt2.IntegrationTests;

/// <summary>
/// The trial switch of design 6.2, <c>Registration:WithoutEmail</c> (owner, 2026-11-27): on a site whose mail is all caught, a new
/// address with a working invitation code is signed in at registration with no code; an account that exists is not.
/// </summary>
public sealed class RegistrationWithoutEmailTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task A_new_address_with_an_invitation_code_is_signed_in_at_once_and_sent_no_code()
    {
        await using var trial = new TrialFactory(Database, withoutEmail: true);
        using var browser = trial.NewBrowser();
        var email = TestData.Email();

        var answer = await RegisterAsync(browser, email);

        answer.ShouldBe(new RegisterResponse(SignedIn: true, AccountChanged: false));
        (await trial.OutboxCountAsync(email)).ShouldBe(0, "nothing is emailed");
        var pending = (await browser.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!;
        pending.RegistrationComplete.ShouldBeFalse("the name and the declarations come next, as after a code");
        pending.VerificationLevel.ShouldBe("none", "the address is not proven");

        await browser.CompleteRegistrationAsync("Rūta M.");
        var me = (await browser.Client.GetFromJsonAsync<MeResponse>(ApiRoutes.Me.Root))!;
        me.RegistrationComplete.ShouldBeTrue();
        me.Roles.ShouldBe(["member"]);
        me.VerificationLevel.ShouldBe("none");
        me.PasskeyCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_registered_address_gets_a_code_as_everywhere_and_no_session()
    {
        await using var trial = new TrialFactory(Database, withoutEmail: true);
        var email = TestData.Email();
        using (var owner = trial.NewBrowser())
        {
            (await RegisterAsync(owner, email)).SignedIn.ShouldBeTrue();
            await owner.CompleteRegistrationAsync();
        }

        using var stranger = trial.NewBrowser();
        var answer = await RegisterAsync(stranger, email);

        answer.ShouldBe(new RegisterResponse(SignedIn: false, AccountChanged: false));
        (await trial.LatestCodeAsync(email)).ShouldNotBeNull("the code goes to the address, as everywhere");
        using var me = await stranger.Client.GetAsync(ApiRoutes.Me.Root);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "typing a registered address never signs anyone in");
    }

    [Fact]
    public async Task A_pending_address_already_proven_gets_a_code_and_no_session()
    {
        await using var trial = new TrialFactory(Database, withoutEmail: false);
        var email = TestData.Email();
        using (var owner = trial.NewBrowser())
        {
            await owner.RegisterAndSignInAsync(email, complete: false);
        }

        await using var onTrial = new TrialFactory(Database, withoutEmail: true);
        using var stranger = onTrial.NewBrowser();

        (await RegisterAsync(stranger, email)).SignedIn.ShouldBeFalse();
        using var me = await stranger.Client.GetAsync(ApiRoutes.Me.Root);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_code_that_does_not_work_is_refused_as_ever()
    {
        await using var trial = new TrialFactory(Database, withoutEmail: true);
        using var browser = trial.NewBrowser();

        using var response = await browser.PostAsync(
            ApiRoutes.Auth.Register, new RegisterRequest { Email = TestData.Email(), InvitationCode = "ZZZZ-ZZZZ" });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "invitation.invalid");
    }

    [Fact]
    public async Task Without_the_switch_a_registration_answers_that_a_code_was_sent()
    {
        using var browser = NewBrowser();
        var email = TestData.Email();

        (await RegisterAsync(browser, email)).ShouldBe(new RegisterResponse(SignedIn: false, AccountChanged: false));
        (await Factory.LatestCodeAsync(email)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_site_whose_mail_is_not_caught_does_not_start_with_the_switch_on()
    {
        await using var site = new TrialFactory(Database, withoutEmail: true, caught: false);

        Should.Throw<InvalidOperationException>(() => site.CreateClient())
            .Message.ShouldContain("Registration:WithoutEmail");
    }

    private static async Task<RegisterResponse> RegisterAsync(TestBrowser browser, string email)
    {
        using var response = await browser.PostAsync(ApiRoutes.Auth.Register, new RegisterRequest { Email = email, InvitationCode = TestData.Invitation });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RegisterResponse>())!;
    }

    /// <summary>The web application with the trial switch on or off, its mail caught or not.</summary>
    private sealed class TrialFactory(DatabaseFixture database, bool withoutEmail, bool caught = true) : Lt2Factory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Registration:WithoutEmail", withoutEmail ? "true" : "false");
            builder.UseSetting("Email:Caught", caught ? "true" : "false");
        }
    }
}
