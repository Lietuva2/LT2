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
    private readonly ApiFake _profiles;

    public SignInComponentTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        Services.AddSingleton<IAuthApi>(_auth);
        Services.AddSingleton<IMeApi>(_me);
        Services.AddSingleton<Lt2.Api.Contracts.Follows.IFollowsApi>(new NoFollowsApi());

        // Design 8.24: how often emails come, beside the switches: the default.
        var frequency = System.Reflection.DispatchProxy.Create<Lt2.Api.Contracts.Follows.INoticeFrequencyApi, ApiFake>();
        ((ApiFake)(object)frequency).On("GetAsync", ApiResult<Lt2.Api.Contracts.Follows.NoticeFrequencyResponse>.Ok(new("immediate")));
        Services.AddSingleton(frequency);

        // "Mano organizacijos" on the account page (design 8.14.14): none.
        var organisations = System.Reflection.DispatchProxy.Create<Lt2.Api.Contracts.Organisations.IOrganisationsApi, ApiFake>();
        ((ApiFake)(object)organisations).On("GetMineAsync", Lt2.Api.Contracts.ApiResult<IReadOnlyList<Lt2.Api.Contracts.Organisations.MyOrganisationResponse>>.Ok([]));
        Services.AddSingleton(organisations);

        // Design 8.5: the account page's MP part asks nothing of an account with neither offer nor link.
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<Lt2.Api.Contracts.Mps.IMpApi, ApiFake>());

        // Design 8.19.4: and nothing of the summaries' choice, which only a linked account in a passkey session reads.
        Services.AddSingleton(System.Reflection.DispatchProxy.Create<Lt2.Api.Contracts.Digests.IMpMailApi, ApiFake>());

        // Design 8.25: the "Profilis" screen's "Apie mane" and "Viešas profilis": a member without a public profile.
        var profiles = System.Reflection.DispatchProxy.Create<IProfilesApi, ApiFake>();
        _profiles = (ApiFake)(object)profiles;
        _profiles.On("GetMineAsync", ApiResult<MyProfileResponse>.Ok(Form));
        Services.AddSingleton(profiles);
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
        var page = Render<AccountSecurity>();
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
        page.Find("#reg-terms").HasAttribute("checked").ShouldBeFalse();

        // Design 8.22.2: the terms box links the terms; beside it, one sentence links the privacy notice (no box for it).
        page.Find("#reg-terms-link").GetAttribute("href").ShouldBe("/taisykles");
        page.Find("#reg-privacy-link").GetAttribute("href").ShouldBe("/privatumas");
        page.Find("#reg-privacy").TextContent.ShouldBe("Kaip tvarkome jūsų duomenis, rašoma privatumo pranešime.");
        page.FindAll("input[type=checkbox]").Count.ShouldBe(2, "the age and the terms; the privacy notice has no box");
        page.FindAll("#complete-invite").ShouldBeEmpty("the invitation is asked again only if it ran out");
        page.Find("#reg-name-hint").TextContent
            .ShouldBe("Šį vardą kiti matys prie jūsų parašytų argumentų. Ar prašote balsuoti už, ar prieš – nemato niekas.");
        page.Markup.ShouldContain("r•••@pavyzdys.test");
    }

    [Fact]
    public void Completion_sends_the_name_the_declaration_and_the_accepted_terms()
    {
        SignedIn(FakeMeApi.Pending);
        var page = Render<SignIn>();

        page.Find("#reg-name").Input("Rūta M.");
        page.Find("#reg-age").Change(true);
        page.Find("#reg-terms").Change(true);
        page.Find("form").Submit();

        page.WaitForAssertion(() => _me.Completions.ShouldHaveSingleItem());
        _me.Completions[0].DisplayName.ShouldBe("Rūta M.");
        _me.Completions[0].ConfirmsAdult.ShouldBeTrue();
        _me.Completions[0].AcceptsTerms.ShouldBeTrue();
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
    public void Completion_without_the_terms_is_stopped_in_the_browser()
    {
        SignedIn(FakeMeApi.Pending);
        var page = Render<SignIn>();

        page.Find("#reg-age").Change(true);
        page.Find("form").Submit();

        page.Find("#reg-terms-error").TextContent.ShouldBe("Kad galėtumėte užsiregistruoti, sutikite su naudojimo taisyklėmis.");
        page.FindAll("#reg-age-error").ShouldBeEmpty();
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
        page.Find("#reg-terms").Change(true);
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
        Render<AccountTaking>().FindAll("#activation-code").ShouldBeEmpty();

        SignedIn(FakeMeApi.Member with { Roles = ["administrator", "member"], AwaitingActivation = true });
        var page = Render<AccountTaking>();
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
        var codeSession = Render<AccountSecurity>();
        codeSession.Find("#delete-staff-passkey span").TextContent.ShouldBe(Text("Data.DeleteStaffNeedsPasskey"));

        // Design 6.2 (owner, 2026-11-17): the way to a staff session without signing out, in place.
        codeSession.Find("#delete-staff-passkey-step-confirm").TextContent.ShouldBe(Text("PasskeyStep.Confirm"));
        codeSession.FindAll("#delete-account").ShouldBeEmpty();
        codeSession.FindAll("#export").ShouldNotBeEmpty("the export is unchanged");

        SignedIn(FakeMeApi.Member with { Roles = ["member", "moderator"], StaffAccount = true, ActiveStaffRoles = ["moderator"] });
        var staffSession = Render<AccountSecurity>();
        staffSession.FindAll("#delete-staff-passkey").ShouldBeEmpty();
        staffSession.Find("#delete-account").Click();
        staffSession.Find("#delete-staff-kept").TextContent.ShouldContain(Text("Data.DeleteStaffKept"));
        // Design 8.15: what is kept and what is deleted is answered in place (a hint), not by leaving the confirmation.
        staffSession.Find("#delete-staff-kept #delete-staff-more").TextContent.ShouldBe(Text("Data.DeleteStaffMore"));
        staffSession.Find("#delete-staff-more-panel .hint__answer").TextContent.ShouldBe(Text("Data.DeleteStaffMoreAnswer"));
        staffSession.Find("#delete-staff-more-panel a").GetAttribute("href").ShouldBe("/metodika#istrynimas");

        SignedIn(FakeMeApi.Member);
        var member = Render<AccountSecurity>();
        member.Find("#delete-account").Click();
        member.FindAll("#delete-staff-kept").ShouldBeEmpty("an ordinary account is unchanged");
        member.FindAll("#delete-confirm").ShouldNotBeEmpty();
    }

    /// <summary>
    /// Design 8.21 (owner, 2026-11-16: "split into several forms"): four account screens, each its own page, joined by one row of
    /// plain links with the current screen marked; each holds its own forms and the anchors emails and texts link to.
    /// </summary>
    [Fact]
    public void The_account_is_four_screens_joined_by_one_row_of_links_each_with_its_own_forms_and_anchors()
    {
        SignedIn(FakeMeApi.Member);
        var screens = new (Func<IRenderedComponent<IComponent>> Render, string Path, string Part, string[] Holds, string[] NotHere)[]
        {
            (() => Render<Hello>(), "/paskyra", "profilis", ["#vardas", "#viesas-profilis", "#about-text", "#profile-public-switch"], ["#sekami", "#laisku-daznis", "#my-data", "#my-invitation"]),
            (() => Render<AccountFollows>(), "/paskyra/sekimas", "sekimas-ir-laiskai", ["#sekami", "#follow-notices", "#laisku-daznis", "#rhythm-choices"], ["#vardas", "#my-data", "#my-invitation"]),
            (() => Render<AccountTaking>(), "/paskyra/dalyvavimas", "dalyvavimas", ["#my-invitation", "#my-organisations"], ["#vardas", "#sekami", "#laisku-daznis", "#my-data"]),
            (() => Render<AccountSecurity>(), "/paskyra/saugumas", "saugumas-ir-duomenys", ["#zinomos-narsykles", "#my-data", "#export"], ["#vardas", "#sekami", "#laisku-daznis", "#my-invitation"]),
        };

        foreach (var (render, path, part, holds, notHere) in screens)
        {
            var page = render();
            page.FindAll("#account-parts a").Select(a => a.GetAttribute("href"))
                .ShouldBe(["/paskyra", "/paskyra/sekimas", "/paskyra/dalyvavimas", "/paskyra/saugumas"], path);
            page.FindAll("#account-parts a").Select(a => a.TextContent)
                .ShouldBe([Text("Parts.Profile"), Text("Parts.Follows"), Text("Parts.Taking"), Text("Parts.Security")]);
            page.FindAll("#account-parts a[aria-current=page]").ShouldHaveSingleItem(path).GetAttribute("href").ShouldBe(path);
            page.Find("#hello-title").TextContent.ShouldBe("Sveiki, Rūta M.!");
            page.Find($"#{part} > h2").TextContent.ShouldNotBeNullOrWhiteSpace(part);
            page.FindAll("noscript").ShouldNotBeEmpty($"{path} carries <NeedsScript />");
            foreach (var id in holds)
            {
                page.Find($"#{part} {id}").ShouldNotBeNull($"{path} holds {id}");
            }

            foreach (var id in notHere)
            {
                page.FindAll(id).ShouldBeEmpty($"{id} is on another screen than {path}");
            }

            page.FindAll(".sheet .sheet").ShouldBeEmpty("design 8.16: a sheet is never inside a sheet");
        }
    }

    /// <summary>Design 8.21: the screens' addresses and the anchors emails and legal texts link to are one list.</summary>
    [Fact]
    public void The_anchors_emails_and_texts_link_to_live_on_their_screens()
    {
        AccountScreens.FollowsList.ShouldBe("/paskyra/sekimas#sekami");
        AccountScreens.EmailRhythm.ShouldBe("/paskyra/sekimas#laisku-daznis", "the footer of every email of the rhythm (design 8.24.1)");
        AccountScreens.KnownBrowsers.ShouldBe("/paskyra/saugumas#zinomos-narsykles");
        AccountScreens.MyData.ShouldBe("/paskyra/saugumas#my-data");
        AccountScreens.MyInvitation.ShouldBe("/paskyra/dalyvavimas#my-invitation");
        Lt2.SharedKernel.AccountScreens.All.ShouldBe(AccountScreens.All, "the browser's copy and the modules' are one file");
    }

    /// <summary>The "Profilis" form's state the fake answers: an ordinary member without a public profile or a party.</summary>
    private static readonly MyProfileResponse Form = new()
    {
        DisplayName = "Rūta M.",
        TextMaxLength = 2000,
        PartyChoices = ["Reformų partija", "Pilietinė sąjunga"],
        PartyGroupMinimum = Lt2.SharedKernel.SettingKeys.DigestPartyGroupMinimum.Default,
    };

    /// <summary>The form's state <c>GET /me/profile</c> answers.</summary>
    private void Profile(MyProfileResponse state) => _profiles.On("GetMineAsync", ApiResult<MyProfileResponse>.Ok(state));

    /// <summary>The request the form sent last.</summary>
    private ProfileRequest Sent() => (ProfileRequest)_profiles.Arguments[_profiles.Calls.LastIndexOf("SaveAsync")][0]!;

    /// <summary>
    /// Design 8.25.1, 8.28.5: "Viešas profilis" says everything the consent covers before it is saved on, the one form sends the
    /// version of the text it showed with the rest, and a member without a name is told one is needed.
    /// </summary>
    [Fact]
    public void The_public_profile_switch_says_what_it_covers_before_the_consent_is_sent()
    {
        SignedIn(FakeMeApi.Member with { DisplayName = null });
        Profile(Form with { DisplayName = null });
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Ok(Form with
        {
            DisplayName = "Rūta Mockutė", About = "Mokytoja iš Kauno. https://pavyzdys.test/ruta", ProfileId = "abcdefgh23", PublicSince = DateTimeOffset.UnixEpoch,
            Address = "/nariai/ruta-mockute-abcdefgh23",
        }));
        var page = Render<Hello>();

        page.FindAll("#profile-consent-text").ShouldBeEmpty("nothing is said before the switch is chosen");
        page.Find("#profile-public-switch").Change(true);
        page.Find("#profile-consent-text").TextContent.ShouldBe(Text("PublicProfile.Consent"));
        page.Find("#profile-name-needed").TextContent.ShouldBe(Text("PublicProfile.NameNeeded"));

        page.Find("#display-name").Input("Rūta Mockutė");
        page.FindAll("#profile-name-needed").ShouldBeEmpty("a name is typed above");
        page.Find("#about-text").Input("Mokytoja iš Kauno. https://pavyzdys.test/ruta");
        page.FindAll("#profile-form button[type=submit]").ShouldHaveSingleItem("one button for the whole form").TextContent.ShouldBe(Text("Profile.Save"));
        page.Find("#profile-form").Submit();

        page.WaitForAssertion(() => _profiles.Calls.ShouldContain("SaveAsync"));
        _profiles.Calls.Count(c => c == "SaveAsync").ShouldBe(1, "one request");
        var sent = Sent();
        (sent.DisplayName, sent.About, sent.Public, sent.PublicConsentVersion).ShouldBe(("Rūta Mockutė", "Mokytoja iš Kauno. https://pavyzdys.test/ruta", true, ProfileLimits.ConsentVersion));
        (sent.Party, sent.PartyConsent, sent.PartyPublic, sent.PartyPublicConsentVersion).ShouldBe(((string?)null, false, false, (string?)null));
        page.WaitForAssertion(() => page.Find("#profile-link").GetAttribute("href").ShouldBe("/nariai/ruta-mockute-abcdefgh23"));
        page.Find("#profile-form").TextContent.ShouldContain(Text("PublicProfile.SavedOn"));
        page.Find("#hello-title").TextContent.ShouldContain("Rūta Mockutė", Case.Sensitive, "the greeting follows the saved name");
    }

    /// <summary>
    /// Design 8.25.9 (owner, 2026-11-17), 8.28.5: "Rodyti viešame profilyje" is offered under a party only while the form has the
    /// profile public, says what it covers before the consent is sent, and goes unticked to withdraw it.
    /// </summary>
    [Fact]
    public void The_party_box_is_offered_only_with_a_public_profile_and_says_what_it_covers_before_it_is_sent()
    {
        SignedIn(FakeMeApi.Member);
        Profile(Form with { Party = "Reformų partija" });
        var off = Render<Hello>();
        off.FindAll("#party-public").ShouldBeEmpty("nothing to show it on while the profile is off");
        off.Find("#party-public-needs-profile").TextContent.ShouldBe(Text("Party.PublicNeedsProfile"));
        off.FindAll("#party-consent").ShouldBeEmpty("the saved party's consent is in force");
        off.Find("#profile-public-switch").Change(true);
        off.FindAll("#party-public").ShouldNotBeEmpty("offered once the switch is ticked in the form");

        var open = Form with { Party = "Reformų partija", ProfileId = "abcdefgh23", Address = "/nariai/ruta-m-abcdefgh23" };
        Profile(open);
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Ok(open with { PartyPublic = true }));
        var page = Render<Hello>();
        page.Find("#party-public").HasAttribute("checked").ShouldBeFalse("off unless ticked");
        page.FindAll("#party-public-text").ShouldBeEmpty();
        page.Find("#party-public").Change(true);
        page.Find("#party-public-text").TextContent.ShouldBe(Text("Party.PublicText"));
        page.Find("#profile-form").Submit();
        page.WaitForAssertion(() => _profiles.Calls.ShouldContain("SaveAsync"));
        (Sent().PartyPublic, Sent().PartyPublicConsentVersion, Sent().Party).ShouldBe((true, PartyPublicLimits.ConsentVersion, "Reformų partija"));
        page.WaitForAssertion(() => page.FindAll("#party-public-text").ShouldBeEmpty("given: nothing more to say"));

        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Ok(open));
        page.Find("#party-public").Change(false);
        page.Find("#profile-form").Submit();
        page.WaitForAssertion(() => _profiles.Calls.Count(c => c == "SaveAsync").ShouldBe(2));
        (Sent().PartyPublic, Sent().PartyPublicConsentVersion).ShouldBe((false, (string?)null));
    }

    /// <summary>Design 5.2, 8.28.5: a new party is sent with its own box, unticked until ticked; the button does not stand for it.</summary>
    [Fact]
    public void A_new_party_is_sent_with_its_own_consent_box()
    {
        SignedIn(FakeMeApi.Member);
        Profile(Form);
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Ok(Form with { Party = "Pilietinė sąjunga" }));
        var page = Render<Hello>();
        page.FindAll("#party-consent").ShouldBeEmpty();
        page.Find("#party-choice").Change("Pilietinė sąjunga");
        page.Find("#party-consent").HasAttribute("checked").ShouldBeFalse("unticked until the person ticks it");
        page.Find("#party-consent").Change(true);
        page.Find("#profile-form").Submit();
        page.WaitForAssertion(() => _profiles.Calls.ShouldContain("SaveAsync"));
        (Sent().Party, Sent().PartyConsent).ShouldBe(("Pilietinė sąjunga", true));
    }

    /// <summary>
    /// Design 8.4 (owner, 2026-11-17): the party's part says the smallest group a summary shows as the setting holds now, and in
    /// words when there is no floor (0 or 1).
    /// </summary>
    [Fact]
    public void The_party_form_states_the_group_minimum_as_it_holds_now_and_in_words_without_a_floor()
    {
        SignedIn(FakeMeApi.Member);
        var minimum = Lt2.SharedKernel.SettingKeys.DigestPartyGroupMinimum.Default;
        Profile(Form with { PartyGroupMinimum = minimum });
        Render<Hello>().Find("#party-text").TextContent.ShouldBe(Text("Party.Text").Replace("{0}", Display.Number(minimum), StringComparison.Ordinal));

        Profile(Form with { PartyGroupMinimum = 1000 });
        Render<Hello>().Find("#party-text").TextContent.ShouldContain("bent " + Display.Number(1000) + ".");

        foreach (var none in new[] { 0, 1 })
        {
            Profile(Form with { PartyGroupMinimum = none });
            Render<Hello>().Find("#party-text").TextContent.ShouldBe(Text("Party.TextAny"), none.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Design 6.2 (owner, 2026-11-16, 2026-11-17): where the staff links stand, a code session on a staff account gets its staff
    /// passkey, in place.
    /// </summary>
    [Fact]
    public void A_staff_account_in_a_code_session_is_offered_its_staff_passkey_where_the_staff_links_stand()
    {
        SignedIn(FakeMeApi.Member with { Roles = ["member", "moderator"], StaffAccount = true });
        var codeSession = Render<AccountTaking>();
        codeSession.Find("#dalyvavimas #staff-key-note span").TextContent.ShouldBe(Text("Staff.KeyText"));
        codeSession.Find("#staff-key-passkey-step-confirm").TextContent.ShouldBe(Text("PasskeyStep.Confirm"));
        codeSession.FindAll("#staff-links").ShouldBeEmpty();
        Render<AccountSecurity>().FindAll("#delete-staff-passkey-step-confirm").ShouldNotBeEmpty("the one beside the deletion note stays, on its own screen");

        SignedIn(FakeMeApi.Member with { Roles = ["member", "moderator"], StaffAccount = true, ActiveStaffRoles = ["moderator"] });
        var staffSession = Render<AccountTaking>();
        staffSession.Find("#dalyvavimas #staff-links").ShouldNotBeNull();
        staffSession.FindAll("#staff-key-note").ShouldBeEmpty();

        SignedIn(FakeMeApi.Member);
        var member = Render<AccountTaking>();
        member.FindAll("#staff-key-note").ShouldBeEmpty();
        member.FindAll("#staff-links").ShouldBeEmpty();
    }

    /// <summary>Design 6.2, 8.28.5: the display name is changed with the one form; the greeting follows.</summary>
    [Fact]
    public void A_member_changes_the_display_name_and_the_greeting_follows()
    {
        SignedIn(FakeMeApi.Member);
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Ok(Form with { DisplayName = "Rūta Mockutė" }));
        var page = Render<Hello>();
        page.Find("#display-name").GetAttribute("value").ShouldBe("Rūta M.");
        page.Find("#display-name-hint").TextContent.ShouldBe(Text("Name.Hint"));

        page.Find("#display-name").Input("  Rūta Mockutė ");
        page.Find("#profile-form").Submit();

        page.WaitForAssertion(() => Sent().DisplayName.ShouldBe("Rūta Mockutė"));
        page.WaitForAssertion(() => page.Find("#hello-title").TextContent.ShouldContain("Rūta Mockutė"));
        page.Find("#profile-form").TextContent.ShouldContain(Text("Profile.Saved"));
    }

    /// <summary>
    /// Design 8.28.5: a refused save keeps what was typed; each refused field says why under itself, and the form's foot says
    /// nothing was saved.
    /// </summary>
    [Fact]
    public void Every_refused_field_says_why_under_itself_and_what_was_typed_stays()
    {
        const string tooLong = "Vardas per ilgas: ne daugiau kaip 60 ženklų.";
        const string code = "Tekste „Apie mane“ yra asmens kodas. Jo skelbti negalima – ištrinkite jį.";
        SignedIn(FakeMeApi.Member);
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Fail(new ApiProblem
        {
            Status = 400,
            Code = "validation",
            Detail = "Patikrinkite įvestus duomenis.",
            Errors = new Dictionary<string, string[]> { ["displayName"] = [tooLong], ["about"] = [code] },
        }));
        var page = Render<Hello>();

        page.Find("#display-name").Input(new string('a', 61));
        page.Find("#about-text").Input("Asmens kodas 39001010000");
        page.Find("#profile-form").Submit();

        page.WaitForAssertion(() => page.Find("#display-name").GetAttribute("aria-invalid").ShouldBe("true"));
        page.Find("#vardas").TextContent.ShouldContain(tooLong);
        page.Find("#viesas-profilis").TextContent.ShouldContain(code);
        page.Find("#about-text").GetAttribute("aria-invalid").ShouldBe("true");
        page.Find("#profile-form .note[role=alert]").TextContent.ShouldBe(Text("Profile.NotSaved"));
        page.Find("#display-name").GetAttribute("value").ShouldBe(new string('a', 61), "what was typed stays");
        page.Find("#hello-title").TextContent.ShouldContain("Rūta M.");
    }

    /// <summary>A writing restriction's refusal of a changed name is said under the name.</summary>
    [Fact]
    public void The_servers_refusal_of_a_name_change_is_said_beside_the_field()
    {
        const string restricted = "Moderatorius apribojo jūsų galimybę rašyti, todėl šiuo metu to padaryti negalite.";
        SignedIn(FakeMeApi.Member);
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Fail(new ApiProblem
        {
            Status = 403, Code = "writing.restricted", Detail = restricted, Errors = new Dictionary<string, string[]> { ["displayName"] = [restricted] },
        }));
        var page = Render<Hello>();

        page.Find("#display-name").Input("Rūta Mockutė");
        page.Find("#profile-form").Submit();

        page.WaitForAssertion(() => page.Find("#vardas .field--error").TextContent.ShouldContain(restricted));
        page.Find("#hello-title").TextContent.ShouldContain("Rūta M.");
    }

    /// <summary>
    /// Design 8.28.5: on the visit after a registration (the mark after "#") the one button reads "Išsaugoti ir tęsti" and a save,
    /// also of an empty form, opens the front page with its mark; a refusal leads nowhere; without the mark it reads "Išsaugoti"
    /// and the person stays.
    /// </summary>
    [Fact]
    public void After_the_registration_the_button_saves_and_leads_on_to_the_front_page_and_otherwise_stays()
    {
        SignedIn(FakeMeApi.Member with { DisplayName = null });
        Profile(Form with { DisplayName = null });
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Ok(Form with { DisplayName = null }));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(AccountScreens.AfterRegistration);
        var page = Render<Hello>();
        var button = page.Find("#profile-save");
        button.TextContent.ShouldBe(Text("Profile.SaveAndGo"));
        page.Find("#profile-form").Submit();
        page.WaitForAssertion(() => navigation.Uri.ShouldBe(navigation.BaseUri + AccountScreens.ProfileSavedHome.TrimStart('/')));
        Sent().ShouldBe(new ProfileRequest(), "nothing filled is a valid form");

        // Refused: nothing leads on.
        navigation.NavigateTo(AccountScreens.AfterRegistration);
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Fail(new ApiProblem
        {
            Status = 400, Code = "about.personal-code", Detail = "Kodas.", Errors = new Dictionary<string, string[]> { ["about"] = ["Kodas."] },
        }));
        var refused = Render<Hello>();
        refused.Find("#profile-form").Submit();
        refused.WaitForAssertion(() => refused.Find("#about-text").GetAttribute("aria-invalid").ShouldBe("true"));
        navigation.Uri.ShouldEndWith(AccountScreens.AfterRegistration);

        // A later visit: "Išsaugoti", and the person stays.
        navigation.NavigateTo(AccountScreens.Profile);
        _profiles.On("SaveAsync", ApiResult<MyProfileResponse>.Ok(Form with { DisplayName = null }));
        var later = Render<Hello>();
        later.Find("#profile-save").TextContent.ShouldBe(Text("Profile.Save"));
        later.Find("#profile-form").Submit();
        later.WaitForAssertion(() => later.Find("#profile-form").TextContent.ShouldContain(Text("Profile.Saved")));
        navigation.Uri.ShouldEndWith(AccountScreens.Profile);
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
        var page = Render<AccountTaking>();

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
        _auth.PasskeySignIn = () => Task.FromResult(ApiResult<SignInResponse>.Fail(new ApiProblem
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

    /// <summary>
    /// Design 6.2 (owner, 2026-11-17): a sign-in in a browser signed in to another account ends that session; the page says so,
    /// with the new account's name after a colon, and goes on only when asked.
    /// </summary>
    [Fact]
    public void A_passkey_sign_in_that_ended_another_accounts_session_says_so_with_the_name_before_going_on()
    {
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"x\"}");
        _auth.PasskeySignIn = () =>
        {
            _me.Account = FakeMeApi.Member with { DisplayName = "Rūta Mockutė" };
            return Task.FromResult(ApiResult<SignInResponse>.Ok(new SignInResponse(true)));
        };
        var navigation = Services.GetRequiredService<BunitNavigationManager>();
        var page = Render<SignIn>();

        page.FindAll("button").Single(b => b.TextContent == Text("SignIn.Passkey")).Click();

        page.WaitForAssertion(() => page.Find("#other-account-note").TextContent.ShouldBe("Prisijungėte kaip kitas narys: Rūta Mockutė."));
        navigation.History.ShouldBeEmpty("nothing goes on before the person has read it");
        page.Find("#other-account-continue").TextContent.ShouldBe(Text("SignIn.Continue"));
        page.Find("#other-account-continue").Click();
        page.WaitForAssertion(() => new Uri(navigation.Uri).AbsolutePath.ShouldBe("/"));
    }

    [Fact]
    public void A_code_sign_in_that_ended_another_accounts_session_says_so_without_a_name_when_there_is_none()
    {
        _auth.CodeSignIn = () =>
        {
            _me.Account = FakeMeApi.Member with { DisplayName = null };
            return Task.FromResult(ApiResult<SignInResponse>.Ok(new SignInResponse(true)));
        };
        var page = Render<SignIn>();
        page.Find("#email").Input(Email);
        page.Find("form").Submit();
        page.WaitForElement("#code").Input("123456");
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("#other-account-note").TextContent.ShouldBe(Text("SignIn.OtherAccount")));
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
        var page = Render<AccountSecurity>();

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

        /// <summary>The code sign-in's answer: by default the browser held no other account.</summary>
        public Func<Task<ApiResult<SignInResponse>>> CodeSignIn { get; set; } = () => Task.FromResult(ApiResult<SignInResponse>.Ok(new SignInResponse(false)));

        public Task<ApiResult<SignInResponse>> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
            CodeSignIn();

        public Func<Task<ApiResult>> SignOut { get; set; } = () => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> SignOutAsync(CancellationToken cancellationToken = default) => SignOut();

        public Task<ApiResult<string>> GetPasskeyCreationOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<string>.Ok("{}"));

        public Task<ApiResult> AddPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<string>.Ok("{}"));

        public Func<Task<ApiResult<SignInResponse>>> PasskeySignIn { get; set; } = () => Task.FromResult(ApiResult<SignInResponse>.Ok(new SignInResponse(false)));

        public Task<ApiResult<SignInResponse>> SignInWithPasskeyAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) =>
            PasskeySignIn();
    }

    /// <summary>The account page's follow list (design 8.11.2) with nothing followed.</summary>
    private sealed class NoFollowsApi : Lt2.Api.Contracts.Follows.IFollowsApi
    {
        public Task<ApiResult<Lt2.Api.Contracts.Follows.MyFollowsResponse>> GetMineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<Lt2.Api.Contracts.Follows.MyFollowsResponse>.Ok(new([], [], [])));

        public Task<ApiResult> FollowBillAsync(string number, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> UnfollowBillAsync(string number, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> FollowMpAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> UnfollowMpAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> FollowOrganisationAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> UnfollowOrganisationAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> FollowMemberAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> UnfollowMemberAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<Lt2.Api.Contracts.Me.FollowersResponse>> GetBillFollowersAsync(string number, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<Lt2.Api.Contracts.Me.FollowersResponse>.Ok(new(0)));

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

        public Task<ApiResult> StartStaffSessionAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> StartPasskeySessionAsync(PasskeyCredentialRequest request, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> ActivateRoleAsync(RoleActivationRequest request, CancellationToken cancellationToken = default)
        {
            Activations.Add(request);
            return Activate(request);
        }

        public Task<ApiResult> GivePositionConsentAsync(PositionConsentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public List<TermsAcceptanceRequest> Acceptances { get; } = [];

        public Task<ApiResult> AcceptTermsAsync(TermsAcceptanceRequest request, CancellationToken cancellationToken = default)
        {
            Acceptances.Add(request);
            return Task.FromResult(ApiResult.Ok());
        }

        public Task<ApiResult> WithdrawPositionConsentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult.Ok());

        public Task<ApiResult> DeleteAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());

        public Task<ApiResult<MyInvitationResponse>> GetInvitationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<MyInvitationResponse>.Ok(new MyInvitationResponse("ABCD-EFGH", "/prisijungti#kodas=ABCD-EFGH", 0, true, null)));

        public Task<ApiResult<MyInvitationResponse>> ReplaceInvitationAsync(CancellationToken cancellationToken = default) => GetInvitationAsync(cancellationToken);

        public Task<ApiResult<IReadOnlyList<SessionResponse>>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<IReadOnlyList<SessionResponse>>.Ok(Sessions));

        public Task<ApiResult> EndSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) => EndSession(sessionId);

        public Task<ApiResult<IReadOnlyList<KnownBrowserResponse>>> GetKnownBrowsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<IReadOnlyList<KnownBrowserResponse>>.Ok([]));

        public Task<ApiResult> ForgetKnownBrowserAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(ApiResult.Ok());
    }
}
