using Bunit;
using Bunit.TestDoubles;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Lt2.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// The sign-in and signed-in pages show a note only when something happened, and then the localized text, never a
/// variable name. Rendered with bUnit against fake API clients, so no server or browser is needed.
/// </summary>
public sealed class SignInComponentTests : BunitContext
{
    private const string Email = "ruta.mockute@pavyzdys.test";

    private readonly FakeAuthApi _auth = new();
    private readonly FakeMeApi _me = new();
    private readonly BunitAuthorizationContext _authorization;

    public SignInComponentTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        Services.AddSingleton<IAuthApi>(_auth);
        Services.AddSingleton<IMeApi>(_me);
        Services.AddSingleton<Lt2.Api.Contracts.Follows.IFollowsApi>(new NoFollowsApi());

        // "Mano organizacijos" on the account page (design 8.14.14): none.
        var organisations = System.Reflection.DispatchProxy.Create<Lt2.Api.Contracts.Organisations.IOrganisationsApi, ApiFake>();
        ((ApiFake)(object)organisations).On("GetMineAsync", Lt2.Api.Contracts.ApiResult<IReadOnlyList<Lt2.Api.Contracts.Organisations.MyOrganisationResponse>>.Ok([]));
        Services.AddSingleton(organisations);
        _authorization = AddAuthorization();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    /// <summary>A signed-in browser whose account is <paramref name="account"/>.</summary>
    private void SignedIn(MeResponse account)
    {
        _me.Account = account;
        _authorization.SetAuthorized(account.Email);
    }

    [Fact]
    public void A_visitor_who_is_not_signed_in_does_not_cause_a_failed_request()
    {
        Render<SignIn>();
        _me.GetCalls.ShouldBe(0, "asking /api/v1/me would answer 401, which the browser logs as an error");
    }

    [Fact]
    public void While_only_prerendered_the_form_cannot_be_used()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var page = Render<SignIn>();

        page.Find("#email").HasAttribute("disabled").ShouldBeTrue();
        page.FindAll("form button[type=submit]").ShouldAllBe(b => b.HasAttribute("disabled"));
    }

    [Fact]
    public void Visitors_without_javascript_are_told_why_the_form_does_not_work()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var page = Render<SignIn>();

        var note = page.Find("noscript .note");
        note.TextContent.ShouldBe(Text("NoScript"));
        note.TextContent.ShouldContain("JavaScript");
        note.HasAttribute("role").ShouldBeFalse("it is static text, not an alert about something that happened");
    }

    [Fact]
    public void Once_interactive_the_form_can_be_used()
    {
        var page = Render<SignIn>();

        page.Find("#email").HasAttribute("disabled").ShouldBeFalse();
        page.FindAll("form button[type=submit]").ShouldAllBe(b => !b.HasAttribute("disabled"));
    }

    private string Text(string key)
    {
        var text = Services.GetRequiredService<IStringLocalizer<UiText>>()[key];
        text.ResourceNotFound.ShouldBeFalse(key);
        return text.Value;
    }

    [Fact]
    public void No_note_is_shown_before_anything_happened()
    {
        var page = Render<SignIn>();

        page.FindAll(".note[role]").ShouldBeEmpty();
        page.Markup.ShouldNotContain("_message");
        page.Markup.ShouldNotContain("_info");
    }

    [Fact]
    public void No_note_is_shown_on_the_registration_or_code_steps_either()
    {
        var page = Render<SignIn>();
        page.FindAll("button.linkish")[0].Click();
        page.Find("#reg-email").ShouldNotBeNull();
        page.FindAll(".note[role]").ShouldBeEmpty();

        page.FindAll("button.linkish")[0].Click();
        page.Find("#email").Input(Email);
        page.Find("form").Submit();
        page.Find("#code").ShouldNotBeNull();
        page.FindAll(".note[role]").ShouldBeEmpty();
    }

    [Fact]
    public void A_request_that_cannot_reach_the_server_shows_the_network_error()
    {
        _auth.RequestCode = _ => throw new HttpRequestException("offline");
        var page = Render<SignIn>();

        page.Find("#email").Input(Email);
        page.Find("form").Submit();

        var note = page.WaitForElement(".note[role=alert]");
        note.TextContent.ShouldBe(Text("Error.Network"));
        page.FindAll(".note--quiet").ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_request_shows_the_servers_explanation()
    {
        const string detail = "Per daug bandymų. Palaukite ir bandykite vėliau.";
        _auth.RequestCode = _ => Task.FromResult(ApiResult.Fail(new ApiProblem { Status = 429, Code = "rate-limited", Detail = detail }));
        var page = Render<SignIn>();

        page.Find("#email").Input(Email);
        page.Find("form").Submit();

        page.WaitForElement(".note[role=alert]").TextContent.ShouldBe(detail);
    }

    [Fact]
    public void Resending_the_code_confirms_it_quietly()
    {
        var page = Render<SignIn>();
        page.Find("#email").Input(Email);
        page.Find("form").Submit();

        page.FindAll("button.linkish")[0].Click();

        page.WaitForElement(".note--quiet").TextContent.ShouldBe(Text("Code.Resent"));
        page.FindAll(".note[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void The_signed_in_page_shows_no_note_until_an_action_fails()
    {
        _me.Sessions =
        [
            new SessionResponse { Id = Guid.NewGuid(), Method = "otp", IsCurrent = true },
            new SessionResponse { Id = Guid.NewGuid(), Method = "otp", IsCurrent = false },
        ];
        SignedIn(FakeMeApi.Member);
        var page = Render<Hello>();
        page.WaitForElement("#hello-title").TextContent.ShouldBe("Sveiki, Rūta M.!");
        page.FindAll(".note[role]").ShouldBeEmpty();
        page.Markup.ShouldNotContain("_message");

        _me.EndSession = _ => throw new HttpRequestException("offline");
        page.Find(".rows__side button").Click();

        page.WaitForElement(".note[role=alert]").TextContent.ShouldBe(Text("Error.Network"));
    }

    [Fact]
    public void The_registration_form_asks_only_for_the_address_and_the_invitation_code()
    {
        var page = Render<SignIn>();
        page.FindAll("button.linkish")[0].Click();

        page.Find("#reg-email").ShouldNotBeNull();
        page.Find("#reg-invite").ShouldNotBeNull();
        page.FindAll("#reg-name").ShouldBeEmpty();
        page.FindAll("#reg-age").ShouldBeEmpty();
    }

    [Fact]
    public void The_code_step_says_only_the_newest_code_works()
    {
        var page = Render<SignIn>();
        page.Find("#email").Input(Email);
        page.Find("form").Submit();

        page.Find("#code").ShouldNotBeNull();
        page.Markup.ShouldContain(Text("Code.NewestOnly"));
    }

    [Fact]
    public void A_signed_in_pending_account_is_asked_for_the_name_and_the_declaration_with_nothing_filled_in()
    {
        SignedIn(FakeMeApi.Pending);
        var page = Render<SignIn>();

        page.Find("#form-title").TextContent.ShouldBe(Text("Complete.Title"));
        page.Find("#reg-name").GetAttribute("value").ShouldBeNullOrEmpty();
        page.Find("#reg-age").HasAttribute("checked").ShouldBeFalse();
        page.FindAll("#complete-invite").ShouldBeEmpty("the invitation is asked again only if it ran out");
        page.Find("#reg-name-hint").TextContent
            .ShouldBe("Šį vardą kiti matys prie jūsų parašytų argumentų. Ar prašote balsuoti už, ar prieš – nemato niekas.");
        page.Markup.ShouldContain("r•••@pavyzdys.test");
    }

    [Fact]
    public void Completion_sends_the_name_and_the_declaration()
    {
        SignedIn(FakeMeApi.Pending);
        var page = Render<SignIn>();

        page.Find("#reg-name").Input("Rūta M.");
        page.Find("#reg-age").Change(true);
        page.Find("form").Submit();

        page.WaitForAssertion(() => _me.Completions.ShouldHaveSingleItem());
        _me.Completions[0].DisplayName.ShouldBe("Rūta M.");
        _me.Completions[0].ConfirmsAdult.ShouldBeTrue();
        _me.Completions[0].InvitationCode.ShouldBeNull();
    }

    [Fact]
    public void Completion_without_the_declaration_is_stopped_in_the_browser()
    {
        SignedIn(FakeMeApi.Pending);
        var page = Render<SignIn>();

        page.Find("form").Submit();

        page.Find("#reg-age-error").TextContent.ShouldBe("Registruotis gali tik 18 metų ir vyresni asmenys.");
        _me.Completions.ShouldBeEmpty();
    }

    [Fact]
    public void When_the_invitation_ran_out_the_form_asks_for_another_code()
    {
        const string usedUp = "Šio kvietimo kodo vietos jau užimtos. Įveskite kitą kvietimo kodą.";
        SignedIn(FakeMeApi.Pending);
        _me.Complete = request => Task.FromResult(request.InvitationCode is null
            ? ApiResult.Fail(new ApiProblem
            {
                Status = 400,
                Code = "validation",
                Errors = new Dictionary<string, string[]> { ["invitationCode"] = [usedUp] },
            })
            : ApiResult.Ok());
        var page = Render<SignIn>();
        page.Find("#reg-age").Change(true);
        page.Find("form").Submit();

        page.WaitForElement("#complete-invite-error").TextContent.ShouldBe(usedUp);
        page.Find("#complete-invite").Input("BANDYMAS-2026");
        page.Find("form").Submit();

        page.WaitForAssertion(() => _me.Completions.Count.ShouldBe(2));
        _me.Completions[1].InvitationCode.ShouldBe("BANDYMAS-2026");
    }

    [Fact]
    public void The_hello_page_has_a_place_for_the_activation_code_only_while_a_role_waits_for_it()
    {
        SignedIn(FakeMeApi.Member);
        Render<Hello>().FindAll("#activation-code").ShouldBeEmpty();

        SignedIn(FakeMeApi.Member with { Roles = ["administrator", "member"], AwaitingActivation = true });
        var page = Render<Hello>();
        page.Find("#activation-title").TextContent.ShouldBe(Text("Activation.Title"));
        page.Find("#activation-code").Input(" 7kq2-m9xd-4trb-hc0p ");
        page.Find("section[aria-labelledby=activation-title] form").Submit();

        page.WaitForAssertion(() => _me.Activations.ShouldHaveSingleItem().Code.ShouldBe("7kq2-m9xd-4trb-hc0p"));
    }

    /// <summary>Design 6.9: a staff account is deleted only from its staff passkey session; the page says so in a code session.</summary>
    [Fact]
    public void A_staff_member_sees_how_to_delete_the_account_in_a_code_session_and_what_stays_in_the_staff_session()
    {
        SignedIn(FakeMeApi.Member with { Roles = ["member", "moderator"], StaffAccount = true });
        var codeSession = Render<Hello>();
        codeSession.Find("#delete-staff-passkey").TextContent.ShouldBe(Text("Data.DeleteStaffNeedsPasskey"));
        codeSession.FindAll("#delete-account").ShouldBeEmpty();
        codeSession.FindAll("#export").ShouldNotBeEmpty("the export is unchanged");

        SignedIn(FakeMeApi.Member with { Roles = ["member", "moderator"], StaffAccount = true, ActiveStaffRoles = ["moderator"] });
        var staffSession = Render<Hello>();
        staffSession.FindAll("#delete-staff-passkey").ShouldBeEmpty();
        staffSession.Find("#delete-account").Click();
        staffSession.Find("#delete-staff-kept").TextContent.ShouldContain(Text("Data.DeleteStaffKept"));
        staffSession.Find("#delete-staff-kept a").GetAttribute("href").ShouldBe("/metodika#privatumas");

        SignedIn(FakeMeApi.Member);
        var member = Render<Hello>();
        member.Find("#delete-account").Click();
        member.FindAll("#delete-staff-kept").ShouldBeEmpty("an ordinary account is unchanged");
        member.FindAll("#delete-confirm").ShouldNotBeEmpty();
    }

    [Fact]
    public void A_refused_activation_code_shows_the_servers_explanation()
    {
        const string invalid = "Aktyvinimo kodas netinka arba nebegalioja.";
        SignedIn(FakeMeApi.Member with { AwaitingActivation = true });
        _me.Activate = _ => Task.FromResult(ApiResult.Fail(new ApiProblem
        {
            Status = 400,
            Code = "role-activation.invalid",
            Errors = new Dictionary<string, string[]> { ["code"] = [invalid] },
        }));
        var page = Render<Hello>();

        page.Find("#activation-code").Input("AAAA-BBBB-CCCC-DDDD");
        page.Find("section[aria-labelledby=activation-title] form").Submit();

        page.WaitForElement(".note[role=alert]").TextContent.ShouldBe(invalid);
    }

    [Fact]
    public void A_refused_passkey_sign_in_is_never_silent_even_when_the_error_is_about_a_field_the_step_does_not_show()
    {
        const string invalid = "Prieigos rakto patikrinti nepavyko.";
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"x\"}");
        _auth.PasskeySignIn = () => Task.FromResult(ApiResult.Fail(new ApiProblem
        {
            Status = 400,
            Code = "passkey.invalid",
            Detail = invalid,
            Errors = new Dictionary<string, string[]> { ["credential"] = [invalid] },
        }));
        var page = Render<SignIn>();

        page.FindAll("button").Single(b => b.TextContent == Text("SignIn.Passkey")).Click();

        page.WaitForElement(".note[role=alert]").TextContent.ShouldBe(invalid);
    }

    [Fact]
    public void An_answer_the_client_cannot_read_still_ends_in_a_message()
    {
        _auth.RequestCode = _ => throw new System.Text.Json.JsonException("not JSON");
        var page = Render<SignIn>();

        page.Find("#email").Input(Email);
        page.Find("form").Submit();

        page.WaitForElement(".note[role=alert]").TextContent.ShouldBe(Text("Error.Generic"));
    }

    [Fact]
    public void A_failed_sign_out_says_so_and_keeps_the_person_on_the_page()
    {
        const string failed = "Įvyko klaida";
        SignedIn(FakeMeApi.Member);
        _auth.SignOut = () => Task.FromResult(ApiResult.Fail(new ApiProblem { Status = 502, Code = "http.502", Detail = failed }));
        var page = Render<Hello>();

        page.FindAll("button").Single(b => b.TextContent == Text("SignOut")).Click();

        page.WaitForElement(".note[role=alert]").TextContent.ShouldBe(failed);
        Services.GetRequiredService<BunitNavigationManager>().History.ShouldBeEmpty();
    }

    [Fact]
    public void The_note_component_renders_nothing_without_text()
    {
        Render<Messages>().FindAll(".note").ShouldBeEmpty("the status region is there, empty, so what appears in it is announced");
        Render<Messages>(p => p.Add(m => m.Message, "Klaida")).Find(".note").TextContent.ShouldBe("Klaida");
    }

    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<RequestCodeRequest, Task<ApiResult>> RequestCode { get; set; } = _ => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> RequestCodeAsync(RequestCodeRequest request, CancellationToken cancellationToken = default) =>
            RequestCode(request);

        public Task<ApiResult> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Func<Task<ApiResult>> SignOut { get; set; } = () => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default) => SignOut();

        public Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<string>.Ok("{}"));

        public Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<string>.Ok("{}"));

        public Func<Task<ApiResult>> PasskeySignIn { get; set; } = () => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
            PasskeySignIn();
    }

    /// <summary>The account page's follow list (design 8.11.2) with nothing followed.</summary>
    private sealed class NoFollowsApi : Lt2.Api.Contracts.Follows.IFollowsApi
    {
        public Task<ApiResult<Lt2.Api.Contracts.Follows.MyFollowsResponse>> GetMineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<Lt2.Api.Contracts.Follows.MyFollowsResponse>.Ok(new([], [], 200, [])));

        public Task<ApiResult> FollowBillAsync(string number, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> UnfollowBillAsync(string number, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> FollowMpAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> UnfollowMpAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> FollowOrganisationAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> UnfollowOrganisationAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<Lt2.Api.Contracts.Follows.NoticeSettingsResponse>> GetNoticesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<Lt2.Api.Contracts.Follows.NoticeSettingsResponse>.Ok(new(true, true)));

        public Task<ApiResult> SetNoticesAsync(Lt2.Api.Contracts.Follows.NoticeSettingsRequest request, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());
    }

    private sealed class FakeMeApi : IMeApi
    {
        public static readonly MeResponse Member = new()
        {
            Id = Guid.NewGuid(),
            Email = Email,
            DisplayName = "Rūta M.",
            VerificationLevel = "email",
            Roles = ["member"],
            RegistrationComplete = true,
        };

        public static readonly MeResponse Pending = new()
        {
            Id = Guid.NewGuid(),
            Email = Email,
            VerificationLevel = "email",
            RegistrationComplete = false,
        };

        /// <summary>The signed-in account; <c>null</c> for a visitor (the API answers 401).</summary>
        public MeResponse? Account { get; set; }

        public IReadOnlyList<SessionResponse> Sessions { get; set; } = [];

        public Func<Guid, Task<ApiResult>> EndSession { get; set; } = _ => Task.FromResult(ApiResult.Ok());

        public List<CompleteRegistrationRequest> Completions { get; } = [];

        public Func<CompleteRegistrationRequest, Task<ApiResult>> Complete { get; set; } = _ => Task.FromResult(ApiResult.Ok());

        public int GetCalls { get; private set; }

        public Task<ApiResult<MeResponse>> GetAsync(CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult(Account is null
                ? ApiResult<MeResponse>.Fail(new ApiProblem { Status = 401, Code = "auth.required" })
                : ApiResult<MeResponse>.Ok(Account));
        }

        public Task<ApiResult> CompleteRegistrationAsync(CompleteRegistrationRequest request, CancellationToken cancellationToken = default)
        {
            Completions.Add(request);
            return Complete(request);
        }

        public List<RoleActivationRequest> Activations { get; } = [];

        public Func<RoleActivationRequest, Task<ApiResult>> Activate { get; set; } = _ => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> ConfirmPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> ActivateRoleAsync(RoleActivationRequest request, CancellationToken cancellationToken = default)
        {
            Activations.Add(request);
            return Activate(request);
        }

        public Task<ApiResult> GivePositionConsentAsync(PositionConsentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> WithdrawPositionConsentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<PartySupportResponse>> GetPartySupportAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<PartySupportResponse>.Ok(new PartySupportResponse(null, [])));

        public Task<ApiResult> SetPartySupportAsync(PartySupportRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> DeleteAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<MyInvitationResponse>> GetInvitationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<MyInvitationResponse>.Ok(new MyInvitationResponse("ABCD-EFGH", "/prisijungti#kodas=ABCD-EFGH", 0, true, null)));

        public Task<ApiResult<MyInvitationResponse>> ReplaceInvitationAsync(CancellationToken cancellationToken = default) => GetInvitationAsync(cancellationToken);

        public Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<IReadOnlyList<SessionResponse>>.Ok(Sessions));

        public Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) => EndSession(sessionId);
    }
}
