using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
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
/// Design 8.7 and 8.12.1: the bill page's side buttons read like the front page's row. With no side both are filled; with
/// a side the chosen one carries a decorative check mark (the state is <c>aria-pressed</c>) and the other is outlined
/// (the shared <c>.sides</c> rules).
/// </summary>
public sealed class SidePickerTests : BunitContext
{
    private readonly ApiFake _appeals;
    private readonly ApiFake _me;
    private readonly ApiFake _follows;

    public SidePickerTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var appeals = DispatchProxy.Create<IAppealsApi, ApiFake>();
        _appeals = (ApiFake)(object)appeals;
        Services.AddSingleton(appeals);
        _me = MeFakes.Add(Services);
        Services.AddSingleton(DispatchProxy.Create<IMyBillsApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
        var follows = DispatchProxy.Create<IFollowsApi, ApiFake>();
        _follows = (ApiFake)(object)follows;
        Services.AddSingleton(follows);
        Services.AddSingleton(DispatchProxy.Create<IProfilesApi, ApiFake>());
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private const string Bill = "XVP-0412";

    private static ApiResult<MyBillRecordResponse> Mine(string? side) => ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse(side, [], null, [], null, true));

    private static ThresholdResponse Threshold(int participants) => new(100, "default", null, participants, false, false);

    private static ApiResult<MyFollowsResponse> Follows(params string[] bills) =>
        ApiResult<MyFollowsResponse>.Ok(new MyFollowsResponse([.. bills.Select(b => new FollowedBillResponse(b, "Gyventojų pajamų mokesčio įstatymo pataisa", null, null))], [], []));

    /// <summary>
    /// Owner, 2026-11-17: taking a side does not reload the page. The counts, the surge note (design 6.6), the threshold's
    /// progress and the follow button (a first side follows the bill, design 8.11.2) read the bill's live answer in place.
    /// </summary>
    [Fact]
    public void A_first_side_shows_in_the_counts_the_threshold_and_the_follow_button_without_reloading_the_page()
    {
        _appeals.OnEach("GetMineAsync", Mine(null), Mine("for"));
        _appeals.On("TakePositionAsync", ApiResult.Ok());
        _appeals.On("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(new BillRecordResponse(
            Guid.NewGuid(), new BillRefResponse(Bill, "Gyventojų pajamų mokesčio įstatymo pataisa"), "open", "", null, null,
            new SideCountsResponse(1, 0, 0, 0), Threshold(1), [], Surge: new SurgeResponse(100, 24))));
        _follows.OnEach("GetMineAsync", Follows(), Follows(Bill));

        var figures = Render<SideFigures>(p => p.Add(c => c.Number, Bill).Add(c => c.Status, "open").Add(c => c.Counts, new SideCountsResponse(0, 0, 0, 0)));
        var threshold = Render<ThresholdProgress>(p => p.Add(c => c.Number, Bill).Add(c => c.Threshold, Threshold(0)));
        var follow = Render<FollowButton>(p => p.Add(c => c.Kind, "bill").Add(c => c.Key, Bill).Add(c => c.Followers, 2));
        var picker = Render<SidePicker>(p => p.Add(c => c.Number, Bill));
        figures.Find("#nobody-yet").ShouldNotBeNull();
        figures.FindAll("#surge-note").ShouldBeEmpty();

        picker.Find("#side-for").Click();

        picker.WaitForAssertion(() => picker.Find("#side-for").GetAttribute("aria-pressed").ShouldBe("true"));
        figures.WaitForAssertion(() => figures.Find("#count-for .figure__num").TextContent.ShouldBe("1"));
        figures.FindAll("#nobody-yet").ShouldBeEmpty();
        figures.Find("#surge-note").TextContent.ShouldStartWith("Per paskutines 24 valandas 100 %");
        threshold.WaitForAssertion(() => threshold.Find("p.num b").TextContent.ShouldBe("1"));
        follow.WaitForAssertion(() => follow.Find("#follow-toggle-bill").GetAttribute("aria-pressed").ShouldBe("true"));
        follow.Find("#followers-count").TextContent.ShouldBe("Sekėjai: 3");
        Services.GetRequiredService<BunitNavigationManager>().History.ShouldBeEmpty("the page is not reloaded");
    }

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

    /// <summary>
    /// Design 8.5.4 (owner, 2026-11-17): the MP's own account takes no side. Once the island runs the buttons give way to one
    /// line pointing to the MP's response; an office member keeps them.
    /// </summary>
    [Fact]
    public void The_mps_own_account_gets_one_line_to_the_response_in_place_of_the_sides()
    {
        _appeals.On("GetMineAsync", Mine(null));
        _me.On("GetAsync", ApiResult<MeResponse>.Ok(MeFakes.MpAccount));

        var picker = Render<SidePicker>(p => p.Add(c => c.Number, "XVP-0412"));

        picker.FindAll("#side-for").ShouldBeEmpty();
        picker.FindAll("#side-against").ShouldBeEmpty();
        var line = picker.Find("#side-mp-account");
        line.QuerySelector("a")!.GetAttribute("href").ShouldBe("/seimas/XVP-0412#atsakymai");
    }

    [Fact]
    public void An_office_member_takes_a_side_as_any_member()
    {
        _appeals.On("GetMineAsync", Mine(null));
        _me.On("GetAsync", ApiResult<MeResponse>.Ok(MeFakes.Member with { Mp = MeFakes.MpAccount.Mp! with { Part = "office" } }));

        var picker = Render<SidePicker>(p => p.Add(c => c.Number, "XVP-0412"));

        picker.FindAll("#side-for").ShouldHaveSingleItem();
        picker.FindAll("#side-mp-account").ShouldBeEmpty();
    }
}