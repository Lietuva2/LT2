using System.Reflection;
using Bunit;
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
/// Design 6.2, "El. pašto adresas" on "Saugumas ir duomenys": the address in use only in the browser, "Keisti adresą" opening the
/// form in place, the proof first in a session that is no passkey session (a passkey or a code to the address in use), then the
/// new address, then its code; the refusals under their fields.
/// </summary>
public sealed class MyEmailTests : BunitContext
{
    private static readonly MeResponse PasskeyMember = MeFakes.Member with { PasskeySession = true, PasskeyCount = 1 };

    private readonly ApiFake _me;

    public MyEmailTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        _me = MeFakes.Add(Services);
        var auth = DispatchProxy.Create<IAuthApi, ApiFake>();
        Services.AddSingleton(auth);
        AddAuthorization().SetAuthorized(MeFakes.Member.Email);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private IStringLocalizer<UiText> L => Services.GetRequiredService<IStringLocalizer<UiText>>();

    private IRenderedComponent<MyEmail> Render(MeResponse account, Action? changed = null)
    {
        _me.On("GetAsync", ApiResult<MeResponse>.Ok(account));
        return Render<MyEmail>(p => p.Add(e => e.Changed, () => changed?.Invoke()));
    }

    [Fact]
    public void Before_the_island_runs_the_page_holds_no_address_and_nothing_can_be_pressed()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var part = Render(PasskeyMember);

        part.Find("#email-address h3").TextContent.ShouldBe("El. pašto adresas");
        part.FindAll("#email-current").ShouldBeEmpty();
        part.Markup.ShouldNotContain(MeFakes.Member.Email);
        part.Find("#email-change-open").HasAttribute("disabled").ShouldBeTrue();
        _me.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void A_passkey_session_types_the_new_address_and_its_code_and_the_part_shows_the_new_address()
    {
        var changed = 0;
        var part = Render(PasskeyMember, () => changed++);
        _me.On("ChangeEmailAsync", ApiResult.Ok());
        _me.On("ConfirmEmailChangeAsync", ApiResult<EmailChangedResponse>.Ok(new("ruta.nauja@pavyzdys.test")));

        part.Find("#email-current").TextContent.ShouldBe(MeFakes.Member.Email);
        part.Find("#email-change-open").TextContent.ShouldBe("Keisti adresą");
        part.Find("#email-change-open").Click();

        part.FindAll("#email-prove").ShouldBeEmpty("a passkey session proves it");
        part.FindAll("#email-proof-code").ShouldBeEmpty();
        part.Find("#email-new").Input("ruta.nauja@pavyzdys.test");
        part.Find("#email-change-form").Submit();

        part.WaitForAssertion(() => part.Find("#email-code-sent").TextContent.ShouldContain("Kodą išsiuntėme adresu ruta.nauja@pavyzdys.test"));
        var request = (EmailChangeRequest)_me.Arguments[_me.Calls.IndexOf("ChangeEmailAsync")][0]!;
        request.Email.ShouldBe("ruta.nauja@pavyzdys.test");
        request.ProofCode.ShouldBeNull();

        part.Find("#email-code").Input("123456");
        part.Find("#email-confirm-form").Submit();

        part.WaitForAssertion(() => part.Find("#email-current").TextContent.ShouldBe("ruta.nauja@pavyzdys.test"));
        ((EmailConfirmRequest)_me.Arguments[_me.Calls.IndexOf("ConfirmEmailChangeAsync")][0]!).Code.ShouldBe("123456");
        part.Find(".note--quiet").TextContent.ShouldBe(L["EmailPart.Changed"].Value);
        part.FindAll("#email-confirm-form").ShouldBeEmpty("the form closes");
        changed.ShouldBe(1, "the page reads its sessions again");
    }

    [Fact]
    public void A_code_session_proves_it_first_with_a_passkey_or_a_code_sent_to_the_address_in_use()
    {
        var part = Render(MeFakes.Member with { PasskeyCount = 1 });
        _me.On("SendEmailProofCodeAsync", ApiResult.Ok());
        _me.On("ChangeEmailAsync", ApiResult.Ok());

        part.Find("#email-change-open").Click();
        part.Find("#email-prove-lead").TextContent.ShouldBe(L["EmailPart.ProveLead"].Value);
        part.Find("#email-passkey-step-text").TextContent.ShouldBe(L["EmailPart.ProvePasskey"].Value);
        part.FindAll("#email-new").ShouldBeEmpty("the address comes after the proof");

        part.Find("#email-proof-send").Click();
        part.WaitForAssertion(() => part.Find("#email-proof-sent").TextContent.ShouldContain(MeFakes.Member.Email));
        part.Find("#email-proof-code").Input("654321");
        part.Find("#email-new").Input("ruta.nauja@pavyzdys.test");
        part.Find("#email-change-form").Submit();

        part.WaitForAssertion(() => part.FindAll("#email-code-sent").ShouldHaveSingleItem());
        var request = (EmailChangeRequest)_me.Arguments[_me.Calls.IndexOf("ChangeEmailAsync")][0]!;
        request.ProofCode.ShouldBe("654321");
    }

    [Fact]
    public void Without_a_passkey_the_only_proof_offered_is_the_code()
    {
        var part = Render(MeFakes.Member);

        part.Find("#email-change-open").Click();

        part.FindAll("#email-passkey-step-confirm").ShouldBeEmpty();
        part.FindAll("#email-proof-send").ShouldHaveSingleItem();
    }

    [Fact]
    public void A_refused_address_is_said_under_its_field_and_the_form_stays()
    {
        const string same = "Šį adresą paskyra ir naudoja. Įveskite kitą adresą.";
        var part = Render(PasskeyMember);
        _me.On("ChangeEmailAsync", ApiResult.Fail(new ApiProblem
        {
            Status = 400,
            Code = "email.same",
            Errors = new Dictionary<string, string[]> { ["email"] = [same] },
        }));

        part.Find("#email-change-open").Click();
        part.Find("#email-new").Input(MeFakes.Member.Email.ToUpperInvariant());
        part.Find("#email-change-form").Submit();

        part.WaitForAssertion(() => part.Find("#email-new-error").TextContent.ShouldBe(same));
        part.Find("#email-new").GetAttribute("aria-invalid").ShouldBe("true");
        part.FindAll("#email-code-sent").ShouldBeEmpty();
    }

    [Fact]
    public void A_staff_account_outside_its_staff_session_is_offered_only_its_staff_passkey()
    {
        var part = Render(MeFakes.Member with { Roles = ["member", "moderator"], StaffAccount = true, PasskeyCount = 1 });

        part.Find("#email-change-open").Click();

        part.Find("#email-staff-passkey").TextContent.ShouldBe(L["EmailPart.StaffPasskey"].Value);
        part.FindAll("#email-passkey-step-confirm").ShouldHaveSingleItem();
        part.FindAll("#email-proof-send").ShouldBeEmpty("a code proves nothing for staff");
    }
}
