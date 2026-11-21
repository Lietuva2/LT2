using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.11.5 (owner, 2026-11-17): the share line's link is the bill's own address, so a link preview shows the bill, with
/// the member's invitation code after the "#"; a code switched off or used up gives the plain address and says why.
/// </summary>
public sealed class ShareLineTests : BunitContext
{
    private const string Bill = "XVP-0412";

    private readonly ApiFake _me;

    public ShareLineTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        Services.AddSingleton(DispatchProxy.Create<IAppealsApi, ApiFake>());
        _me = MeFakes.Add(Services);
        Services.AddSingleton(DispatchProxy.Create<IMyBillsApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IFollowsApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IProfilesApi, ApiFake>());
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private IRenderedComponent<ShareLine> Line(bool usable)
    {
        _me.On("GetInvitationAsync", ApiResult<MyInvitationResponse>.Ok(new MyInvitationResponse("ABCD-EFGH", "/prisijungti#kodas=ABCD-EFGH", 0, usable, null, usable)));
        return Render<ShareLine>(p => p.Add(c => c.Number, Bill).Add(c => c.Path, "/seimas/" + Bill).Add(c => c.ToGo, 10));
    }

    [Fact]
    public void The_link_is_the_bills_address_with_the_code_after_the_hash()
    {
        var line = Line(usable: true);
        var link = line.WaitForElement("#share-link");
        link.GetAttribute("href").ShouldBe("/seimas/XVP-0412#kodas=ABCD-EFGH");
        link.TextContent.ShouldEndWith("/seimas/XVP-0412#kodas=ABCD-EFGH");
        line.FindAll("#share-no-link").ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_is_missing_once_the_threshold_is_reached_also_below_it_after_a_sent_summary()
    {
        // Design 8.12.2: a bill that has been in a summary really sent to MPs counts as having reached its threshold,
        // whatever its participants now; the share line must not say "Iki ribos trūksta 96 dalyvių" under "Riba pasiekta".
        ShareLine.MissingOf(new ThresholdResponse(100, "default", null, 4, Reached: true, EnteredSummaries: true)).ShouldBe(0);
        ShareLine.MissingOf(new ThresholdResponse(100, "default", null, 4, Reached: false, EnteredSummaries: false)).ShouldBe(96);
        ShareLine.MissingOf(new ThresholdResponse(100, "default", null, 130, Reached: true, EnteredSummaries: false)).ShouldBe(0);
    }

    [Fact]
    public void A_code_that_is_off_or_used_up_gives_the_plain_address_and_says_why()
    {
        var line = Line(usable: false);
        line.WaitForElement("#share-link").GetAttribute("href").ShouldBe("/seimas/XVP-0412");
        line.Markup.ShouldNotContain("ABCD-EFGH");
        line.Find("#share-no-link").TextContent.ShouldContain("nuoroda atveria projektą be jo");
    }
}
