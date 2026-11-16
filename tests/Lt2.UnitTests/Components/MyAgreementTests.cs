using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Analytics;
using Lt2.Web.Client;
using Lt2.Web.Client.Analytics;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.26.4: "Kas balsuoja taip kaip jūs" is filled only in the browser and only for the caller: nothing is asked or
/// shown while the server prerenders, a visitor gets the sign-in link back to the page, and a member's rows show a share
/// only from 20 common votes, "maža imtis" below.
/// </summary>
public sealed class MyAgreementTests : BunitContext
{
    private readonly ApiFake _api;

    public MyAgreementTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var api = DispatchProxy.Create<IAnalyticsApi, ApiFake>();
        _api = (ApiFake)(object)api;
        Services.AddSingleton(api);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static MyAgreementResponse Answer(int bills, params AgreementRowResponse[] mps) =>
        new(bills, 0, 20, [new AgreementRowResponse("reformu-partijos-frakcija", "Reformų partijos frakcija", null, 30, false, 15, 20, 1, 0.75m)], mps);

    [Fact]
    public void While_the_server_prerenders_nothing_is_asked_or_shown()
    {
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        SetRendererInfo(new RendererInfo("Server", isInteractive: false));

        var part = Render<MyAgreement>();

        part.Markup.Trim().ShouldBeEmpty();
        _api.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void While_the_server_prerenders_a_visitor_already_gets_the_sign_in_link_so_it_works_without_javascript()
    {
        AddAuthorization();
        SetRendererInfo(new RendererInfo("Server", isInteractive: false));

        var part = Render<MyAgreement>();

        part.Find("#mine-sign-in a").GetAttribute("href").ShouldBe("/prisijungti#tikslas=%2Fseimas%2Fanalitika");
        _api.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void A_visitor_gets_the_sign_in_link_back_to_the_page_and_nothing_is_asked()
    {
        AddAuthorization();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var part = Render<MyAgreement>();

        part.Find("#mine-sign-in a").GetAttribute("href").ShouldBe("/prisijungti#tikslas=%2Fseimas%2Fanalitika");
        _api.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void A_member_sees_a_share_from_twenty_common_votes_and_counts_with_a_small_sample_below()
    {
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _api.On("GetMyAgreementAsync", ApiResult<MyAgreementResponse>.Ok(Answer(21,
            new AgreementRowResponse("ona-jonaitiene", "Ona Jonaitienė", "Pilietinės sąjungos frakcija", null, false, 18, 20, 0, 0.9m),
            new AgreementRowResponse("jonas-petraitis", "Jonas Petraitis", null, null, true, 3, 4, 0, null))));

        var part = Render<MyAgreement>();

        part.Find("#mine-basis").TextContent.ShouldContain("21");
        var ona = part.Find("#mine-mp-ona-jonaitiene");
        ona.TextContent.ShouldContain(Display.Percent(0.9m)!);
        ona.TextContent.ShouldContain("sutapo 18 iš 20");
        ona.QuerySelector("svg.meter").ShouldNotBeNull();
        var jonas = part.Find("#mine-mp-jonas-petraitis");
        jonas.TextContent.ShouldContain("sutapo 3 iš 4");
        jonas.TextContent.ShouldContain("maža imtis");
        jonas.TextContent.ShouldContain("mandatas baigėsi");
        jonas.QuerySelector("svg.meter").ShouldBeNull("no bar under twenty");
        part.Find("#mine-faction-reformu-partijos-frakcija").TextContent.ShouldContain(Display.Percent(0.75m)!);
    }

    [Fact]
    public void A_member_without_a_side_on_a_closed_bill_is_sent_to_the_projects()
    {
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _api.On("GetMyAgreementAsync", ApiResult<MyAgreementResponse>.Ok(new MyAgreementResponse(0, 0, 20, [], [])));

        var part = Render<MyAgreement>();

        part.Find("#mine-none a").GetAttribute("href").ShouldBe("/projektai");
    }

    [Fact]
    public void Ten_mps_show_and_the_rest_open_with_the_browsers_own_disclosure()
    {
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        var mps = Enumerable.Range(1, 12).Select(i => new AgreementRowResponse($"mp-{i}", $"Seimo narys {i}", null, null, false, 1, 2, 0, null)).ToArray();
        _api.On("GetMyAgreementAsync", ApiResult<MyAgreementResponse>.Ok(Answer(2, mps)));

        var part = Render<MyAgreement>();

        part.FindAll("#mine-mps > li").Count.ShouldBe(MyAgreement.FirstMps);
        part.FindAll("#mine-all-mps li").Count.ShouldBe(2);
        part.Find("#mine-all-mps").HasAttribute("open").ShouldBeFalse();
    }
}
