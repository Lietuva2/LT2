using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.3 (owner, 2026-11-25): the moderators' page of tabs in the running island. The counts come first; the page opens
/// on the first work tab with something waiting; a work tab shows its count only when something waits, a reference tab
/// always in brackets; a tab opens in place, reading only its own list, and the address is replaced with the tab's.
/// </summary>
public sealed class ModerationPageTests : BunitContext
{
    private static readonly ModerationCountsResponse Counts = new(0, 2, 0, 0, 0, 0, 0, 7, 0, 1);

    public ModerationPageTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var moderation = DispatchProxy.Create<IModerationApi, ApiFake>();
        Moderation = (ApiFake)(object)moderation;
        Moderation.On("GetCountsAsync", ApiResult<ModerationCountsResponse>.Ok(Counts));
        Moderation.On("GetQueueAsync", ApiResult<ModerationQueueResponse>.Ok(new([], [], 0, 0)));
        Moderation.On("GetUndescribedAsync", ApiResult<UndescribedBillsResponse>.Ok(new([], 7)));
        Services.AddSingleton(moderation);
        var parliament = DispatchProxy.Create<Lt2.Api.Contracts.Parliament.IParliamentApi, ApiFake>();
        ((ApiFake)(object)parliament).On("GetMpsAsync", ApiResult<Lt2.Api.Contracts.Parliament.MpListResponse>.Ok(new([], 0, null)));
        Services.AddSingleton(parliament);
        AddAuthorization().SetAuthorized("moderatore@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private ApiFake Moderation { get; }

    [Fact]
    public void The_page_opens_on_the_first_work_tab_with_something_waiting_and_reads_only_its_list()
    {
        var page = Render<Moderation>();

        page.Find($"#tab-{ModerationTabs.Reasons}").GetAttribute("aria-current").ShouldBe("page");
        page.FindAll($"#tab-{ModerationTabs.Records}[aria-current]").ShouldBeEmpty();
        Moderation.Calls.ShouldBe(["GetCountsAsync", "GetQueueAsync"]);
        Moderation.Arguments[1][0].ShouldBe(QueueLists.Reasons);
        page.Find("#h-reasons").ShouldNotBeNull();
    }

    [Fact]
    public void A_work_tab_shows_its_count_only_when_something_waits_a_reference_tab_always_in_brackets()
    {
        var page = Render<Moderation>();

        page.Find($"#tab-count-{ModerationTabs.Reasons}").TextContent.ShouldBe("2");
        page.FindAll($"#tab-count-{ModerationTabs.Records}").ShouldBeEmpty();
        page.Find($"#tab-count-{ModerationTabs.Undescribed}").TextContent.ShouldBe("(7)");
        page.Find($"#tab-count-{ModerationTabs.Hidden}").TextContent.ShouldBe("(0)");
        page.Find("#moderation-tabs-reference").TextContent.ShouldContain("Be aprašymo");
    }

    [Fact]
    public void A_tab_opens_in_place_and_the_address_is_replaced_with_its_own()
    {
        var page = Render<Moderation>();

        var link = page.Find($"#tab-{ModerationTabs.Undescribed}");
        link.GetAttribute("href").ShouldBe("/moderavimas?skiltis=be-aprasymo");
        link.GetAttribute("data-enhance-nav").ShouldBe("false");
        link.Click();

        page.Find($"#tab-{ModerationTabs.Undescribed}").GetAttribute("aria-current").ShouldBe("page");
        page.FindAll($"#tab-{ModerationTabs.Reasons}[aria-current]").ShouldBeEmpty();
        Moderation.Calls.ShouldContain("GetUndescribedAsync");
        page.Find("#h-undescribed").ShouldNotBeNull();
        page.FindAll("#h-reasons").ShouldBeEmpty();
        JSInterop.Invocations.ShouldContain(i => i.Identifier == "history.replaceState" && Equals(i.Arguments[2], "/moderavimas?skiltis=be-aprasymo"));
    }

    [Fact]
    public void An_action_in_a_tab_reads_the_counts_again()
    {
        var id = Guid.NewGuid();
        Moderation.On<IReadOnlyList<RestrictionResponse>>("GetRestrictionsAsync", ApiResult<IReadOnlyList<RestrictionResponse>>.Ok(
            [new RestrictionResponse(id, "Rūta Mockutė", null, "Bandymas.", DateTimeOffset.UtcNow, true, null, null)]));
        Moderation.On("LiftRestrictionAsync", ApiResult.Ok());
        Moderation.OnEach("GetCountsAsync", ApiResult<ModerationCountsResponse>.Ok(Counts), ApiResult<ModerationCountsResponse>.Ok(Counts with { Restrictions = 0 }));
        var page = Render<Moderation>();
        page.Find($"#tab-{ModerationTabs.Restrictions}").Click();
        page.Find($"#tab-count-{ModerationTabs.Restrictions}").TextContent.ShouldBe("(1)");

        page.Find($"#lift-open-{id}").Click();
        page.Find($"#lift-statement-{id}").Change("Apribojimas baigiamas.");
        page.Find($"#lift-submit-{id}").Click();

        Moderation.Calls.Count(c => c == "GetCountsAsync").ShouldBe(2);
        page.Find($"#tab-count-{ModerationTabs.Restrictions}").TextContent.ShouldBe("(0)");
    }
}
