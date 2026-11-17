using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Owner, 2026-11-17: where an action needs a passkey session, "Patvirtinti prieigos raktu" makes this session one in place
/// with one of the account's own passkeys (for staff, the staff passkey), then the island reads its state again or the page
/// is loaded again. The button waits for the island; a refusal is said and nothing goes on.
/// </summary>
public sealed class PasskeyStepTests : BunitContext
{
    private static readonly MeResponse StaffCodeSession = MeFakes.Member with { Roles = ["member", "moderator"], StaffAccount = true };

    private readonly ApiFake _me;

    public PasskeyStepTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        _me = MeFakes.Add(Services);
        var auth = DispatchProxy.Create<IAuthApi, ApiFake>();
        ((ApiFake)(object)auth).On("GetPasskeyRequestOptionsAsync", ApiResult<string>.Ok("{\"challenge\":\"AAAA\"}"));
        Services.AddSingleton(auth);
        AddAuthorization().SetAuthorized(MeFakes.Member.Email);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private IStringLocalizer<UiText> L => Services.GetRequiredService<IStringLocalizer<UiText>>();

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    private void PasskeyAnswers()
    {
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"own-key\"}");
    }

    [Fact]
    public void The_step_says_what_is_needed_and_confirms_with_the_accounts_passkey_in_place_then_the_island_reads_again()
    {
        PasskeyAnswers();
        _me.On("StartPasskeySessionAsync", ApiResult.Ok());
        var confirmed = 0;
        var step = Render<PasskeyStep>(p => p.Add(s => s.IdPrefix, "mp-").Add(s => s.Confirmed, () => confirmed++));

        step.Find("#mp-passkey-step-text").TextContent.ShouldBe(L["PasskeyStep.Text"].Value);
        var button = step.Find("#mp-passkey-step-confirm");
        button.TextContent.ShouldBe(L["PasskeyStep.Confirm"].Value);
        button.Click();

        step.WaitForAssertion(() => confirmed.ShouldBe(1));
        _me.Calls.ShouldBe(["StartPasskeySessionAsync"]);
        ((PasskeyCredentialRequest)_me.Arguments[0][0]!).Credential.ShouldBe("{\"id\":\"own-key\"}");
        Navigation.History.ShouldBeEmpty("the island reads its own state again; the page stays");
    }

    [Fact]
    public void Without_an_island_of_its_own_to_read_again_the_page_is_loaded_again()
    {
        PasskeyAnswers();
        _me.On("StartPasskeySessionAsync", ApiResult.Ok());
        var step = Render<PasskeyStep>();

        step.Find("#passkey-step-confirm").Click();

        step.WaitForAssertion(() => Navigation.History.ShouldNotBeEmpty());
        Navigation.History.First().Options.ForceLoad.ShouldBeTrue();
    }

    [Fact]
    public void Another_accounts_passkey_is_refused_with_the_servers_words_and_nothing_goes_on()
    {
        const string refused = "Šis prieigos raktas – kitos paskyros. Pasirinkite šios paskyros prieigos raktą.";
        PasskeyAnswers();
        _me.On("StartPasskeySessionAsync", ApiResult.Fail(new ApiProblem { Status = 403, Code = "passkey.other-account", Detail = refused }));
        var confirmed = 0;
        var step = Render<PasskeyStep>(p => p.Add(s => s.Confirmed, () => confirmed++));

        step.Find("#passkey-step-confirm").Click();

        step.WaitForAssertion(() => step.Find(".note[role=alert]").TextContent.ShouldBe(refused));
        confirmed.ShouldBe(0);
        Navigation.History.ShouldBeEmpty();
    }

    [Fact]
    public void Before_the_island_runs_the_button_cannot_be_used()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var step = Render<PasskeyStep>();

        step.Find("#passkey-step-confirm").HasAttribute("disabled").ShouldBeTrue();
        _me.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void For_staff_the_staff_passkey_starts_a_staff_session_and_the_page_is_loaded_again()
    {
        PasskeyAnswers();
        _me.On("StartStaffSessionAsync", ApiResult.Ok());
        var step = Render<PasskeyStep>(p => p.Add(s => s.Staff, true).Add(s => s.Account, StaffCodeSession));

        step.FindAll("#passkey-step-text").ShouldBeEmpty("the page's own refusal says why");
        step.Find("#passkey-step-confirm").Click();

        step.WaitForAssertion(() => Navigation.History.ShouldNotBeEmpty());
        _me.Calls.ShouldBe(["StartStaffSessionAsync"]);
    }

    [Fact]
    public void For_staff_the_button_shows_only_for_a_staff_account_whose_session_is_not_a_staff_session()
    {
        Render<PasskeyStep>(p => p.Add(s => s.Staff, true).Add(s => s.Account, MeFakes.Member)).FindAll("#passkey-step-confirm").ShouldBeEmpty();
        Render<PasskeyStep>(p => p.Add(s => s.Staff, true).Add(s => s.Account, StaffCodeSession with { ActiveStaffRoles = ["moderator"] }))
            .FindAll("#passkey-step-confirm").ShouldBeEmpty();

        // Without the account given, the step reads it once in the browser.
        _me.On("GetAsync", ApiResult<MeResponse>.Ok(StaffCodeSession));
        var step = Render<PasskeyStep>(p => p.Add(s => s.Staff, true));
        step.WaitForAssertion(() => step.FindAll("#passkey-step-confirm").ShouldHaveSingleItem());
    }
}
