using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Mps;
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
        Admin.On("GetLockedAccountsAsync", ApiResult<IReadOnlyList<LockedAccountResponse>>.Ok(Locked));
        Admin.OnEach("GetMpLinksAsync", ApiResult<IReadOnlyList<AdminMpLinkResponse>>.Ok(Linked), ApiResult<IReadOnlyList<AdminMpLinkResponse>>.Ok([Linked[1]]));
        Me = AddFake<IMeApi>();
        Me.On("ConfirmPasskeyAsync", ApiResult.Ok());
        AddFake<IAuthApi>().On("GetPasskeyRequestOptionsAsync", ApiResult<string>.Ok("{\"challenge\":\"AAAA\"}"));
        AddAuthorization().SetAuthorized("administratorius@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private static readonly AdminMpLinkResponse[] Linked =
    [
        new("darius-liepaitis", "Darius", "Liepaitis", new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero)),
        new("rasa-ievaite", "Rasa", "Ievaitė", new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero)),
    ];

    private static readonly LockedAccountResponse[] Locked =
    [
        new(Guid.Parse("0193a1b2-0000-7000-8000-000000000001"), "Tomas Kazlauskas", new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero), "Pakartotinai skelbė kitų asmenų duomenis."),
        new(Guid.Parse("0193a1b2-0000-7000-8000-000000000002"), null, new DateTimeOffset(2026, 11, 12, 7, 0, 0, TimeSpan.Zero), "Piktnaudžiavimas."),
    ];

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
        var request = (AccountLockRequest)Admin.Arguments[Admin.Calls.IndexOf("LockAccountAsync")][0]!;
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
        Admin.Calls.Count(c => c == "UnlockAccountAsync").ShouldBe(1);
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
        ((AccountLockRequest)Admin.Arguments[Admin.Calls.LastIndexOf("LockAccountAsync")][0]!).Statement.ShouldBe(Statement);
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

        page.WaitForAssertion(() => page.FindAll(".field--error").Count.ShouldBeGreaterThan(0));
        Admin.Calls.ShouldNotContain("LockAccountAsync");
    }

    [Fact]
    public void The_linked_mps_are_listed_with_the_day_and_no_statement_field_is_open()
    {
        SignedInAs("administrator");

        var page = Open();

        page.WaitForAssertion(() => page.FindAll("#acc-mps > li").Count.ShouldBe(2));
        var first = page.Find("#acc-mp-darius-liepaitis");
        first.TextContent.ShouldContain("Darius Liepaitis");
        first.TextContent.ShouldContain("2026-11-03");
        first.QuerySelector("a")!.GetAttribute("href").ShouldBe("/p/darius-liepaitis");
        page.FindAll("[id^='acc-mp-form-']").ShouldBeEmpty();
    }

    [Fact]
    public void The_locked_accounts_are_listed_with_their_name_the_day_and_the_statement_and_never_an_address()
    {
        SignedInAs("administrator");

        var page = Open();

        page.WaitForAssertion(() => page.FindAll("#acc-locked > li").Count.ShouldBe(2));
        var first = page.Find("#acc-locked-0193a1b2-0000-7000-8000-000000000001");
        first.TextContent.ShouldContain("Tomas Kazlauskas");
        first.TextContent.ShouldContain("Pakartotinai skelbė kitų asmenų duomenis.");
        first.TextContent.ShouldContain(Services.GetRequiredService<IStringLocalizer<AppealUi>>().Format("Acc.Locked.Since", Display.DayTime(Locked[0].LockedAt)));
        page.Find("#acc-locked-0193a1b2-0000-7000-8000-000000000002").TextContent.ShouldContain(L["Reasons.Anonymous"].Value, Case.Sensitive, "a member without a name");
        page.Markup.ShouldNotContain("@pavyzdys.test");
        page.FindAll("[id^='acc-unlock-form-']").ShouldBeEmpty();
        page.FindAll("#account-lock").Count.ShouldBe(1, "the address form stays below the list as the other way");
        page.Markup.IndexOf("id=\"locked-accounts\"", StringComparison.Ordinal).ShouldBeLessThan(page.Markup.IndexOf("id=\"account-lock\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_locked_account_the_list_says_so()
    {
        SignedInAs("administrator");
        Admin.On("GetLockedAccountsAsync", ApiResult<IReadOnlyList<LockedAccountResponse>>.Ok([]));

        var page = Open();

        page.WaitForAssertion(() => page.Find("#acc-locked-none").TextContent.ShouldBe(L["Acc.Locked.None"].Value));
    }

    [Fact]
    public void Atrakinti_asks_the_passkey_on_the_same_press_sends_the_statement_by_id_and_reads_the_list_again()
    {
        SignedInAs("administrator");
        Admin.OnEach("UnlockAccountByIdAsync", ConfirmationRequired(), ApiResult.Ok());
        Admin.OnEach("GetLockedAccountsAsync", ApiResult<IReadOnlyList<LockedAccountResponse>>.Ok(Locked), ApiResult<IReadOnlyList<LockedAccountResponse>>.Ok([Locked[1]]));
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"staff-key\"}");
        var page = Open();
        page.WaitForAssertion(() => page.FindAll("#acc-locked > li").Count.ShouldBe(2));

        page.Find("#acc-unlock-0193a1b2-0000-7000-8000-000000000001").Click();
        page.FindAll("[id^='acc-unlock-form-']").Count.ShouldBe(1);
        page.Find("#acc-unlock-statement-0193a1b2-0000-7000-8000-000000000001").Input("Klaida ištaisyta.");
        page.Find("#acc-unlock-form-0193a1b2-0000-7000-8000-000000000001").Submit();

        page.WaitForAssertion(() => Admin.Calls.Count(c => c == "UnlockAccountByIdAsync").ShouldBe(2));
        Me.Calls.ShouldContain("ConfirmPasskeyAsync");
        Admin.Arguments[Admin.Calls.LastIndexOf("UnlockAccountByIdAsync")][0].ShouldBe(Locked[0].Id);
        ((StatementRequest)Admin.Arguments[Admin.Calls.LastIndexOf("UnlockAccountByIdAsync")][1]!).Statement.ShouldBe("Klaida ištaisyta.");
        page.WaitForAssertion(() => page.FindAll("#acc-locked > li").Count.ShouldBe(1));
        page.Markup.ShouldContain(L["Acc.Lock.Unlocked"].Value);
    }

    [Fact]
    public void Cancelling_the_passkey_prompt_unlocks_nothing_and_keeps_the_statement()
    {
        SignedInAs("administrator");
        Admin.On("UnlockAccountByIdAsync", ConfirmationRequired());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetException(new Microsoft.JSInterop.JSException("NotAllowedError"));
        var page = Open();
        page.WaitForAssertion(() => page.FindAll("#acc-locked > li").Count.ShouldBe(2));

        page.Find("#acc-unlock-0193a1b2-0000-7000-8000-000000000001").Click();
        page.Find("#acc-unlock-statement-0193a1b2-0000-7000-8000-000000000001").Input("Klaida ištaisyta.");
        page.Find("#acc-unlock-form-0193a1b2-0000-7000-8000-000000000001").Submit();

        page.WaitForAssertion(() => page.Markup.ShouldContain(L["Acc.Passkey.Cancelled"].Value));
        Admin.Calls.Count(c => c == "UnlockAccountByIdAsync").ShouldBe(1);
        page.Find("#acc-unlock-statement-0193a1b2-0000-7000-8000-000000000001").GetAttribute("value").ShouldBe("Klaida ištaisyta.");
    }

    [Fact]
    public void Unlocking_without_a_statement_is_refused_under_the_field_and_nothing_is_sent()
    {
        SignedInAs("administrator");
        var page = Open();
        page.WaitForAssertion(() => page.FindAll("#acc-locked > li").Count.ShouldBe(2));

        page.Find("#acc-unlock-0193a1b2-0000-7000-8000-000000000001").Click();
        page.Find("#acc-unlock-form-0193a1b2-0000-7000-8000-000000000001").Submit();

        page.WaitForAssertion(() => page.FindAll("#acc-unlock-form-0193a1b2-0000-7000-8000-000000000001 .field--error").Count.ShouldBe(1));
        Admin.Calls.ShouldNotContain("UnlockAccountByIdAsync");
    }

    [Fact]
    public void Baigti_rysi_opens_the_statement_field_in_place_one_at_a_time_and_cancel_closes_it()
    {
        SignedInAs("administrator");
        var page = Open();
        page.WaitForAssertion(() => page.FindAll("#acc-mps > li").Count.ShouldBe(2));

        page.Find("#acc-mp-end-darius-liepaitis").Click();
        page.FindAll("#acc-mp-form-darius-liepaitis textarea").Count.ShouldBe(1);
        page.FindAll("#acc-mp-end-darius-liepaitis").ShouldBeEmpty("the opened row's button is replaced by its form");

        page.Find("#acc-mp-end-rasa-ievaite").Click();
        page.FindAll("[id^='acc-mp-form-']").Count.ShouldBe(1, "one action open at a time");
        page.FindAll("#acc-mp-form-rasa-ievaite").Count.ShouldBe(1);
        page.FindAll("#acc-mp-end-darius-liepaitis").Count.ShouldBe(1, "the first row's button is back");

        page.Find("#acc-mp-form-rasa-ievaite button[type=button]").Click();
        page.FindAll("[id^='acc-mp-form-']").ShouldBeEmpty();
        Admin.Calls.ShouldNotContain("EndMpLinkAsync");
    }

    [Fact]
    public void Ending_a_link_asks_the_passkey_on_the_same_press_sends_the_statement_again_and_reads_the_list_again()
    {
        SignedInAs("administrator");
        Admin.OnEach("EndMpLinkAsync", ConfirmationRequired(), ApiResult.Ok());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"staff-key\"}");
        var page = Open();
        page.WaitForAssertion(() => page.FindAll("#acc-mps > li").Count.ShouldBe(2));

        page.Find("#acc-mp-end-darius-liepaitis").Click();
        page.Find("#acc-mp-statement-darius-liepaitis").Input("Susieta per klaidą.");
        page.Find("#acc-mp-form-darius-liepaitis").Submit();

        page.WaitForAssertion(() => Admin.Calls.Count(c => c == "EndMpLinkAsync").ShouldBe(2));
        Me.Calls.ShouldContain("ConfirmPasskeyAsync");
        Admin.Arguments[Admin.Calls.IndexOf("EndMpLinkAsync")][0].ShouldBe("darius-liepaitis");
        ((StatementRequest)Admin.Arguments[Admin.Calls.LastIndexOf("EndMpLinkAsync")][1]!).Statement.ShouldBe("Susieta per klaidą.");
        page.WaitForAssertion(() => page.FindAll("#acc-mps > li").Count.ShouldBe(1));
        page.Markup.ShouldContain(L.Format("Acc.Mps.Ended", "Darius Liepaitis"));
        page.FindAll("[id^='acc-mp-form-']").ShouldBeEmpty();
    }

    [Fact]
    public void Cancelling_the_passkey_prompt_ends_nothing_and_keeps_the_open_form_and_its_statement()
    {
        SignedInAs("administrator");
        Admin.On("EndMpLinkAsync", ConfirmationRequired());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetException(new Microsoft.JSInterop.JSException("NotAllowedError"));
        var page = Open();
        page.WaitForAssertion(() => page.FindAll("#acc-mps > li").Count.ShouldBe(2));

        page.Find("#acc-mp-end-darius-liepaitis").Click();
        page.Find("#acc-mp-statement-darius-liepaitis").Input("Susieta per klaidą.");
        page.Find("#acc-mp-form-darius-liepaitis").Submit();

        page.WaitForAssertion(() => page.Markup.ShouldContain(L["Acc.Passkey.Cancelled"].Value));
        Admin.Calls.Count(c => c == "EndMpLinkAsync").ShouldBe(1);
        page.FindAll("#acc-mp-form-darius-liepaitis").Count.ShouldBe(1);
        page.Find("#acc-mp-statement-darius-liepaitis").GetAttribute("value").ShouldBe("Susieta per klaidą.");
        page.FindAll("#acc-mps > li").Count.ShouldBe(2);
    }

    [Fact]
    public void A_link_ended_without_a_statement_is_refused_under_the_field_and_nothing_is_sent()
    {
        SignedInAs("administrator");
        var page = Open();
        page.WaitForAssertion(() => page.FindAll("#acc-mps > li").Count.ShouldBe(2));

        page.Find("#acc-mp-end-darius-liepaitis").Click();
        page.Find("#acc-mp-form-darius-liepaitis").Submit();

        page.WaitForAssertion(() => page.FindAll("#acc-mp-form-darius-liepaitis .field--error").Count.ShouldBe(1));
        Admin.Calls.ShouldNotContain("EndMpLinkAsync");
    }

    [Fact]
    public void Without_a_taken_over_profile_the_list_says_so()
    {
        SignedInAs("administrator");
        Admin.On("GetMpLinksAsync", ApiResult<IReadOnlyList<AdminMpLinkResponse>>.Ok([]));

        var page = Open();

        page.WaitForAssertion(() => page.Find("#acc-mps-none").TextContent.ShouldBe(L["Acc.Mps.None"].Value));
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
        page.FindAll("#mp-links").ShouldBeEmpty();
        Admin.Calls.ShouldBeEmpty("nothing is read for anyone else");
    }
}
