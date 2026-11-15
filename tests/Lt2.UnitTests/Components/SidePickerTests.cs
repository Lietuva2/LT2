using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.7 and 8.12.1: the bill page's side buttons read like the front page's row. With no side both are filled; with
/// a side the chosen one carries a decorative check mark (the state is <c>aria-pressed</c>) and the other is outlined
/// (the shared <c>.sides</c> rules).
/// </summary>
public sealed class SidePickerTests : BunitContext
{
    private readonly ApiFake _appeals;

    public SidePickerTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var appeals = DispatchProxy.Create<IAppealsApi, ApiFake>();
        _appeals = (ApiFake)(object)appeals;
        Services.AddSingleton(appeals);
        Services.AddSingleton(DispatchProxy.Create<IMeApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IMyBillsApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private static ApiResult<MyBillRecordResponse> Mine(string? side) => ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse(side, [], null, [], null, true));

    [Fact]
    public void The_chosen_side_has_a_decorative_check_mark_and_the_other_none()
    {
        _appeals.On("GetMineAsync", Mine("against"));

        var picker = Render<SidePicker>(p => p.Add(c => c.Number, "XVP-0412"));

        picker.Find(".pair").ClassList.ShouldContain("sides");
        picker.Find("#side-against").GetAttribute("aria-pressed").ShouldBe("true");
        picker.Find("#side-against .sides__check").GetAttribute("aria-hidden").ShouldBe("true");
        picker.FindAll("#side-for .sides__check").ShouldBeEmpty();
        picker.Find("#side-for").HasAttribute("disabled").ShouldBeFalse("the other side is still a button that changes it");
    }

    [Fact]
    public void Without_a_side_neither_has_a_check_mark()
    {
        _appeals.On("GetMineAsync", Mine(null));

        var picker = Render<SidePicker>(p => p.Add(c => c.Number, "XVP-0412"));

        picker.FindAll(".sides__check").ShouldBeEmpty();
        picker.FindAll(".sides button").Select(b => b.GetAttribute("aria-pressed")).ShouldBe(["false", "false"]);
    }
}
