using System.Reflection;
using System.Text.Json;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.SharedKernel;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 5.6 (decided 2026-11-13): the settings screen groups the settings under headings; each has a plain label, one
/// sentence, its unit, the default and the key; the rarely changed security limits sit apart and say so; the minimum
/// threshold says its number is a placeholder.
/// </summary>
public sealed class SettingsPageTests : BunitContext
{
    public SettingsPageTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var api = DispatchProxy.Create<IAdminApi, ApiFake>();
        var settings = SettingKeys.All
            .Select(k => new SettingResponse { Key = k.Name, Value = k.DefaultValue, Default = k.DefaultValue })
            .ToList();
        Admin = (ApiFake)(object)api;
        Admin.On("GetSettingsAsync", ApiResult<IReadOnlyList<SettingResponse>>.Ok(settings));
        Services.AddSingleton(api);
        var auth = DispatchProxy.Create<IAuthApi, ApiFake>();
        ((ApiFake)(object)auth).On("GetPasskeyRequestOptionsAsync", ApiResult<string>.Ok("{\"challenge\":\"AAAA\"}"));
        Services.AddSingleton(auth);
        var me = DispatchProxy.Create<IMeApi, ApiFake>();
        MeFake = (ApiFake)(object)me;
        MeFake.On("ConfirmPasskeyAsync", ApiResult.Ok());
        Services.AddSingleton(me);
        AddAuthorization().SetAuthorized("administratorius@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private ApiFake Admin { get; }

    private ApiFake MeFake { get; }

    private IStringLocalizer<AppealUi> L => Services.GetRequiredService<IStringLocalizer<AppealUi>>();

    private static ApiResult<SettingResponse> ConfirmationRequired() =>
        ApiResult<SettingResponse>.Fail(new ApiProblem { Status = 403, Code = "passkey.confirmation-required", Detail = "Patvirtinkite veiksmą prieigos raktu." });

    /// <summary>Design 6.9: the server asks for the passkey again; the page confirms it once and saves again.</summary>
    [Fact]
    public void A_save_the_server_refuses_for_a_stale_confirmation_asks_the_passkey_and_saves_again()
    {
        Admin.OnEach("UpdateSettingAsync", ConfirmationRequired(), ApiResult<SettingResponse>.Ok(new SettingResponse { Key = "Feedback:KeepMonths", Value = JsonSerializer.SerializeToElement(6) }));
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"staff-key\"}");
        var page = Render<Settings>();

        page.Find("#admin-passkey-note").TextContent.ShouldBe(L["Admin.PasskeyNote"].Value);
        page.Find("#setting-Feedback-KeepMonths").Change("6");
        page.Find("#row-setting-Feedback-KeepMonths button").Click();

        page.WaitForAssertion(() => Admin.Calls.Count(c => c == "UpdateSettingAsync").ShouldBe(2));
        MeFake.Calls.ShouldBe(["ConfirmPasskeyAsync"]);
        page.WaitForAssertion(() => page.Markup.ShouldContain(L.Format("Admin.SavedOne", L["Admin.Setting.Feedback:KeepMonths"].Value)));
    }

    [Fact]
    public void Cancelling_the_passkey_prompt_saves_nothing_and_says_how_to_try_again()
    {
        Admin.On("UpdateSettingAsync", ConfirmationRequired());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetException(new Microsoft.JSInterop.JSException("NotAllowedError"));
        var page = Render<Settings>();

        page.Find("#row-setting-Feedback-KeepMonths button").Click();

        page.WaitForAssertion(() => page.Markup.ShouldContain(L["Admin.Passkey.Cancelled"].Value));
        Admin.Calls.Count(c => c == "UpdateSettingAsync").ShouldBe(1, "no second save without the confirmation");
        MeFake.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Every_setting_has_a_label_a_sentence_and_a_unit_and_shows_its_default_and_key()
    {
        var page = Render<Settings>();
        foreach (var key in SettingKeys.All.Select(k => k.Name))
        {
            L["Admin.Setting." + key].ResourceNotFound.ShouldBeFalse(key);
            L["Admin.Help." + key].ResourceNotFound.ShouldBeFalse(key);
            L["Admin.Unit." + key].ResourceNotFound.ShouldBeFalse(key);
            var row = page.Find("#row-setting-" + key.Replace(':', '-'));
            row.TextContent.ShouldContain(L["Admin.Help." + key].Value);
            row.TextContent.ShouldContain(key);
            row.TextContent.ShouldContain(string.Format(System.Globalization.CultureInfo.InvariantCulture, L["Admin.DefaultIs"], "").TrimEnd());
        }

        page.FindAll("#group-other").ShouldBeEmpty("every setting the application knows has its group");
    }

    [Fact]
    public void The_security_limits_sit_in_their_own_group_which_says_they_are_rarely_changed()
    {
        var group = Render<Settings>().Find("#group-security");
        group.QuerySelector("h2")!.TextContent.ShouldBe("Saugumo ribos");
        group.TextContent.ShouldContain(L["Admin.GroupNote.security"].Value);
        foreach (var key in new[] { "SignIn:CodesPerHour", "SignIn:CodesPerDay", "Registration:PendingAccountDays" })
        {
            group.QuerySelector("#row-setting-" + key.Replace(':', '-')).ShouldNotBeNull(key);
        }

        group.TextContent.ShouldContain("pašto dėžutę nuo užtvindymo", Case.Sensitive, "codes per day: why it exists");
    }

    [Fact]
    public void The_minimum_threshold_says_its_number_is_a_placeholder()
    {
        var page = Render<Settings>();
        page.Find("#group-appeals #placeholder-minimum").TextContent.ShouldBe(L["Admin.MinimumPlaceholder"].Value);
        page.Find("#setting-Digest-ThresholdMinimum").GetAttribute("value").ShouldBe("50");
    }

    [Fact]
    public void Yes_and_no_are_shown_in_words_and_a_text_without_quotes()
    {
        var page = Render<Settings>();
        page.Find("#setting-Registration-RequireInvitation").GetAttribute("value").ShouldBe("taip");
        page.Find("#setting-Digest-Weekly").GetAttribute("value").ShouldBe("07:00");
        JsonSerializer.SerializeToElement(true).ValueKind.ShouldBe(JsonValueKind.True);
    }
}
