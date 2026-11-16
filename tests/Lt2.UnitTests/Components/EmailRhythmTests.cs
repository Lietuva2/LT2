using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Follows;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.24: the account page's choice of how often emails come. Three choices with the short explanation of each, the saved
/// one picked, saved when another is picked, disabled until the island is interactive, a refusal said and the saved one kept.
/// </summary>
public sealed class EmailRhythmTests : BunitContext
{
    private readonly ApiFake _api;

    public EmailRhythmTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var api = System.Reflection.DispatchProxy.Create<INoticeFrequencyApi, ApiFake>();
        _api = (ApiFake)(object)api;
        _api.On("GetAsync", ApiResult<NoticeFrequencyResponse>.Ok(new NoticeFrequencyResponse(NoticeFrequencies.Weekly)));
        Services.AddSingleton(api);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_three_choices_are_shown_with_the_saved_one_picked_and_what_each_does()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        var cut = Render<EmailRhythm>();
        cut.Find("#rhythm-title").TextContent.ShouldBe("Kaip dažnai siųsti laiškus");
        cut.FindAll("input[type=radio]").Select(i => i.Id).ShouldBe(["rhythm-immediate", "rhythm-daily", "rhythm-weekly"]);
        cut.Find("#rhythm-weekly").HasAttribute("checked").ShouldBeTrue();
        cut.Find("#rhythm-immediate").HasAttribute("checked").ShouldBeFalse();
        cut.Find("label:has(#rhythm-immediate)").TextContent.ShouldContain("Iš karto");
        cut.Find("label:has(#rhythm-daily)").TextContent.ShouldContain("Kartą per dieną");
        cut.Find("label:has(#rhythm-weekly)").TextContent.ShouldContain("Kartą per savaitę ir artėjant balsavimui");
        cut.Find("#rhythm-weekly-hint").TextContent.ShouldContain("pirmadienio rytą");
        cut.Find("#rhythm-weekly").GetAttribute("aria-describedby").ShouldBe("rhythm-weekly-hint");
    }

    [Fact]
    public void The_choices_stay_disabled_until_the_island_is_interactive()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var cut = Render<EmailRhythm>();
        cut.FindAll("input[type=radio]").ShouldAllBe(i => i.HasAttribute("disabled"));
    }

    [Fact]
    public void Picking_a_choice_saves_it_and_says_so()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _api.On("SetAsync", ApiResult.Ok());
        var cut = Render<EmailRhythm>();
        cut.Find("#rhythm-daily").Change(true);
        _api.Calls.ShouldContain("SetAsync");
        ((NoticeFrequencyRequest)_api.Arguments[_api.Calls.IndexOf("SetAsync")][0]!).Frequency.ShouldBe(NoticeFrequencies.Daily);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Išsaugota."));
        cut.Find("#rhythm-daily").HasAttribute("checked").ShouldBeTrue();
    }

    [Fact]
    public void A_refusal_is_said_and_the_saved_choice_stays_picked()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _api.On("SetAsync", ApiResult.Fail(new ApiProblem { Code = "notices.frequency-invalid", Detail = "Pasirinkite, kaip dažnai siųsti laiškus." }));
        var cut = Render<EmailRhythm>();
        cut.Find("#rhythm-immediate").Change(true);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Pasirinkite, kaip dažnai siųsti laiškus."));
        cut.Find("#rhythm-weekly").HasAttribute("checked").ShouldBeTrue();
    }
}
