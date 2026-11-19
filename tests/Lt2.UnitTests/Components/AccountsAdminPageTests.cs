using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.3 (the full lock), on the administrators' "Paskyros" screen: an address and a statement, "Užrakinti paskyrą" and
/// "Atrakinti paskyrą"; the staff passkey asked for again on the same press when the server says so, what was typed kept until
/// the action goes through; nothing for anyone who is not an administrator in a staff session.
/// </summary>
public sealed class AccountsAdminPageTests : BunitContext
{
    private const string Address = "narys@pavyzdys.test";
    private const string Statement = "Pakartotinis piktnaudžiavimas.";

    public AccountsAdminPageTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        Services.AddLt2Validators();
        Admin = AddFake<IAdminApi>();
        Me = AddFake<IMeApi>();
        Me.On("ConfirmPasskeyAsync", ApiResult.Ok());
        AddFake<IAuthApi>().On("GetPasskeyRequestOptionsAsync", ApiResult<string>.Ok("{\"challenge\":\"AAAA\"}"));
        AddAuthorization().SetAuthorized("administratorius@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private ApiFake Admin { get; }

    private ApiFake Me { get; }

    private IStringLocalizer<AppealUi> L => Services.GetRequiredService<IStringLocalizer<AppealUi>>();

    private ApiFake AddFake<T>()
        where T : class
    {
        var proxy = DispatchProxy.Create<T, ApiFake>();
        Services.AddSingleton(proxy);
        return (ApiFake)(object)proxy;
    }

    private void SignedInAs(params string[] activeRoles) =>
        Me.On("GetAsync", ApiResult<MeResponse>.Ok(new MeResponse { Email = "x@pavyzdys.test", ActiveStaffRoles = activeRoles }));

    private static ApiResult ConfirmationRequired() =>
        ApiResult.Fail(new ApiProblem { Status = 403, Code = "passkey.confirmation-required", Detail = "Patvirtinkite veiksmą prieigos raktu." });

    private IRenderedComponent<AccountsAdmin> Open()
    {
        var page = Render<AccountsAdmin>();
        page.WaitForAssertion(() => page.FindAll("#account-lock").Count.ShouldBe(1));
        return page;
    }

    private static void Fill(IRenderedComponent<AccountsAdmin> page, string address, string statement)
    {
        page.Find("#acc-lock-address").Change(address);
        page.Find("#acc-lock-statement").Input(statement);
    }

    [Fact]
    public void An_administrator_locks_with_the_address_and_the_statement_and_the_fields_are_cleared()
    {
        SignedInAs("administrator");
        Admin.On("LockAccountAsync", ApiResult.Ok());
        var page = Open();

        Fill(page, Address, Statement);
        page.Find("#acc-lock").Click();

        page.WaitForAssertion(() => page.Markup.ShouldContain(L["Acc.Lock.Locked"].Value));
        var request = (AccountLockRequest)Admin.Arguments.Single()[0]!;
        (request.Email, request.Statement).ShouldBe((Address, Statement));
        page.Find("#acc-lock-address").GetAttribute("value").ShouldBeNullOrEmpty();
        page.Markup.ShouldNotContain(Address, Case.Insensitive, "the address is not shown again");
    }

    [Fact]
    public void Unlocking_goes_the_same_way()
    {
        SignedInAs("administrator");
        Admin.On("UnlockAccountAsync", ApiResult.Ok());
        var page = Open();

        Fill(page, Address, "Klaida ištaisyta.");
        page.Find("#acc-unlock").Click();

        page.WaitForAssertion(() => page.Markup.ShouldContain(L["Acc.Lock.Unlocked"].Value));
        Admin.Calls.ShouldBe(["UnlockAccountAsync"]);
    }

    [Fact]
    public void A_stale_confirmation_asks_the_passkey_on_the_same_press_and_sends_again_with_the_same_text()
    {
        SignedInAs("administrator");
        Admin.OnEach("LockAccountAsync", ConfirmationRequired(), ApiResult.Ok());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"staff-key\"}");
        var page = Open();

        Fill(page, Address, Statement);
        page.Find("#acc-lock").Click();

        page.WaitForAssertion(() => Admin.Calls.Count(c => c == "LockAccountAsync").ShouldBe(2));
        Me.Calls.ShouldContain("ConfirmPasskeyAsync");
        ((AccountLockRequest)Admin.Arguments[1][0]!).Statement.ShouldBe(Statement);
        page.WaitForAssertion(() => page.Markup.ShouldContain(L["Acc.Lock.Locked"].Value));
    }

    [Fact]
    public void Cancelling_the_passkey_prompt_locks_nothing_and_keeps_what_was_typed()
    {
        SignedInAs("administrator");
        Admin.On("LockAccountAsync", ConfirmationRequired());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetException(new Microsoft.JSInterop.JSException("NotAllowedError"));
        var page = Open();

        Fill(page, Address, Statement);
        page.Find("#acc-lock").Click();

        page.WaitForAssertion(() => page.Markup.ShouldContain(L["Acc.Passkey.Cancelled"].Value));
        Admin.Calls.Count(c => c == "LockAccountAsync").ShouldBe(1);
        page.Find("#acc-lock-statement").GetAttribute("value").ShouldBe(Statement);
    }

    [Fact]
    public void A_missing_statement_is_said_under_its_field_and_nothing_is_sent()
    {
        SignedInAs("administrator");
        var page = Open();

        Fill(page, Address, "  ");
        page.Find("#acc-lock").Click();

        page.WaitForAssertion(() => page.FindAll("#acc-lock-statement-error, [for='acc-lock-statement'] ~ .field__error, .field--error").Count.ShouldBeGreaterThan(0));
        Admin.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("moderator")]
    [InlineData("")]
    public void Anyone_who_is_not_an_administrator_in_a_staff_session_is_told_and_gets_no_form(string role)
    {
        SignedInAs(role.Length == 0 ? [] : [role]);

        var page = Render<AccountsAdmin>();

        page.WaitForAssertion(() => page.Find("#accounts-refused").TextContent.ShouldBe(L["Acc.Refused"].Value));
        page.FindAll("#account-lock").ShouldBeEmpty();
        page.FindAll("#acc-lock").ShouldBeEmpty();
    }
}
