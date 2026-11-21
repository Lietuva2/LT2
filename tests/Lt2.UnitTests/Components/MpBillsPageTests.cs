using System.Reflection;
using Bunit;
using Lt2.Api.Contracts.Mps;
using Lt2.Web.Client;
using Lt2.Web.Client.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.16.5 (owner, 2026-11-21): "Projektai, dėl kurių į jus kreipiamasi" has no visible heading on its list's sheet, so
/// the page's title is its sticky head; the way back above it and the lead scroll away.
/// </summary>
public sealed class MpBillsPageTests : BunitContext
{
    public MpBillsPageTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        Services.AddSingleton(DispatchProxy.Create<IMpResponsesApi, ApiFake>());
        AddAuthorization().SetAuthorized("seimo.narys@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        // As the server prerenders it: the head is there before the list is asked for.
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Static", isInteractive: false));
    }

    [Fact]
    public void The_pages_title_is_its_sticky_head_without_the_way_back_or_the_lead()
    {
        var page = Render<MpBills>();

        var head = page.Find("#page-head");
        head.ClassList.ShouldContain("page-head");
        head.Children.Select(c => c.TagName).ShouldBe(["H1"]);
        head.PreviousElementSibling!.ClassList.ShouldContain("crumbs");
        head.NextElementSibling!.ClassList.ShouldContain("page-head__rest");
        head.NextElementSibling!.QuerySelector(".lead").ShouldNotBeNull();
    }
}
