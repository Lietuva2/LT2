using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 6.2, 8.21 (owner, 2026-11-16): the staff passkey page. A staff account's code session confirms the staff passkey and
/// goes back to the page it came from; the button waits for the island; a staff session and an account without a staff role
/// are sent on; a refusal is said.
/// </summary>
public sealed class StaffSignInPageTests : BunitContext
{
    private static readonly MeResponse CodeSession = new()
    {
        Email = "moderatore@pavyzdys.test",
        Roles = ["member", "moderator"],
        RegistrationComplete = true,
        StaffAccount = true,
    };

    public StaffSignInPageTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var auth = DispatchProxy.Create<IAuthApi, ApiFake>();
        ((ApiFake)(object)auth).On("GetPasskeyRequestOptionsAsync", ApiResult<string>.Ok("{\"challenge\":\"AAAA\"}"));
        Services.AddSingleton(auth);
        var me = DispatchProxy.Create<IMeApi, ApiFake>();
        Me = (ApiFake)(object)me;
        Services.AddSingleton(me);
        AddAuthorization().SetAuthorized(CodeSession.Email);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private ApiFake Me { get; }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    private IStringLocalizer<UiText> L => Services.GetRequiredService<IStringLocalizer<UiText>>();

    private void PasskeyAnswers()
    {
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"staff-key\"}");
    }

    [Fact]
    public void A_code_session_on_a_staff_account_confirms_the_staff_passkey_and_goes_back_where_it_came_from()
    {
        Me.On("GetAsync", ApiResult<MeResponse>.Ok(CodeSession));
        Me.On("StartStaffSessionAsync", ApiResult.Ok());
        PasskeyAnswers();
        Navigation.NavigateTo(StaffSessionPage.Link("/moderavimas"));
        var page = Render<StaffSignIn>();

        page.Find("h1").TextContent.ShouldBe(L["StaffKey.Title"].Value);
        page.Find("#staff-key-text").TextContent.ShouldBe(L["StaffKey.Text"].Value);
        page.WaitForAssertion(() => page.Find("#staff-key").HasAttribute("disabled").ShouldBeFalse());
        page.Find("#staff-key").Click();

        page.WaitForAssertion(() => Navigation.Uri.ShouldEndWith("/moderavimas"));
        Me.Calls.ShouldBe(["GetAsync", "StartStaffSessionAsync"]);
        ((PasskeyCredentialRequest)Me.Arguments[1][0]!).Credential.ShouldBe("{\"id\":\"staff-key\"}");
    }

    [Fact]
    public void A_refused_passkey_is_said_and_the_page_stays()
    {
        Me.On("GetAsync", ApiResult<MeResponse>.Ok(CodeSession));
        Me.On("StartStaffSessionAsync", ApiResult.Fail(new ApiProblem { Status = 403, Code = "passkey.staff-required", Detail = "Šiam veiksmui reikia prisijungti darbuotojo prieigos raktu." }));
        PasskeyAnswers();
        Navigation.NavigateTo(StaffSessionPage.Path);
        var page = Render<StaffSignIn>();

        page.WaitForAssertion(() => page.Find("#staff-key").HasAttribute("disabled").ShouldBeFalse());
        page.Find("#staff-key").Click();

        page.WaitForAssertion(() => page.Markup.ShouldContain("Šiam veiksmui reikia prisijungti darbuotojo prieigos raktu."));
        Navigation.Uri.ShouldEndWith(StaffSessionPage.Path);
    }

    [Fact]
    public void A_session_that_already_is_a_staff_session_is_sent_on()
    {
        Me.On("GetAsync", ApiResult<MeResponse>.Ok(CodeSession with { ActiveStaffRoles = ["moderator"] }));
        Navigation.NavigateTo(StaffSessionPage.Link("/moderavimas/atsiliepimai"));
        Render<StaffSignIn>();

        Navigation.Uri.ShouldEndWith("/moderavimas/atsiliepimai");
        Me.Calls.ShouldBe(["GetAsync"]);
    }

    [Fact]
    public void An_account_without_a_staff_role_goes_to_the_front_page()
    {
        Me.On("GetAsync", ApiResult<MeResponse>.Ok(CodeSession with { Roles = ["member"], StaffAccount = false }));
        Navigation.NavigateTo(StaffSessionPage.Link("/moderavimas"));
        Render<StaffSignIn>();

        new Uri(Navigation.Uri).AbsolutePath.ShouldBe("/");
    }

    [Fact]
    public void Another_site_is_never_the_page_to_go_back_to()
    {
        Me.On("GetAsync", ApiResult<MeResponse>.Ok(CodeSession with { ActiveStaffRoles = ["moderator"] }));
        Navigation.NavigateTo(StaffSessionPage.Path + "#grizti=https%3A%2F%2Fkitas.pavyzdys.test%2F");
        Render<StaffSignIn>();

        new Uri(Navigation.Uri).AbsolutePath.ShouldBe("/");
    }

    [Fact]
    public void Before_the_island_starts_the_button_cannot_be_used_and_nothing_is_asked()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var page = Render<StaffSignIn>();

        page.Find("#staff-key").HasAttribute("disabled").ShouldBeTrue();
        Me.Calls.ShouldBeEmpty("the page as served is the same for every member");
    }
}
