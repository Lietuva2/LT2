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
    private readonly ApiFake _digests;

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
        var digests = DispatchProxy.Create<Lt2.Api.Contracts.Digests.IDigestsApi, ApiFake>();
        _digests = (ApiFake)(object)digests;
        Services.AddSingleton(digests);
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

    /// <summary>
    /// Design 8.12.2 (owner, 2026-11-18): once the threshold is reached the ruler gives way to the words, with what is true of
    /// the summaries: the next one's planned day (and, while sending is off, that it is not sent), the last one that went, or
    /// that the next day is not known; a closed record speaks in the past tense.
    /// </summary>
    [Fact]
    public void A_reached_threshold_shows_words_instead_of_the_ruler_with_the_next_summarys_day()
    {
        var reached = new ThresholdResponse(100, "default", null, 11080, true, false);
        var day = new DateOnly(2026, 11, 23);

        var planned = Render<ThresholdProgress>(p => p.Add(c => c.Number, Bill).Add(c => c.Threshold, reached).Add(c => c.NextDay, day));
        planned.FindAll(".ruler").ShouldBeEmpty();
        planned.Find("#threshold-reached").TextContent.ShouldBe("Riba pasiekta – balsuoti prašo 11 080 dalyvių.");
        planned.Find("#threshold-summary").TextContent.ShouldBe("Suvestinė Seimo nariams planuojama 2026-11-23.");

        var trial = Render<ThresholdProgress>(p => p.Add(c => c.Number, Bill).Add(c => c.Threshold, reached).Add(c => c.NextDay, day).Add(c => c.Trial, true));
        trial.Find("#threshold-summary").TextContent.ShouldBe("Suvestinė planuojama 2026-11-23; kol vyksta bandymas, Seimo nariams ji nesiunčiama.");

        var unknown = Render<ThresholdProgress>(p => p.Add(c => c.Number, Bill).Add(c => c.Threshold, reached with { Participants = 101 }));
        unknown.Find("#threshold-reached").TextContent.ShouldBe("Riba pasiekta – balsuoti prašo 101 dalyvis.");
        unknown.Find("#threshold-summary").TextContent.ShouldBe("Kitos suvestinės Seimo nariams data dar nežinoma.");

        var went = Render<ThresholdProgress>(p => p.Add(c => c.Number, Bill).Add(c => c.Threshold, reached with { Participants = 102 })
            .Add(c => c.LastDay, new DateOnly(2026, 11, 16)).Add(c => c.LastSent, true));
        went.Find("#threshold-reached").TextContent.ShouldBe("Riba pasiekta – balsuoti prašo 102 dalyviai.");
        went.Find("#threshold-summary").TextContent.ShouldBe("Suvestinė Seimo nariams išsiųsta 2026-11-16.");

        var closed = Render<ThresholdProgress>(p => p.Add(c => c.Number, Bill).Add(c => c.Threshold, reached).Add(c => c.Status, "closed")
            .Add(c => c.NextDay, day).Add(c => c.LastDay, new DateOnly(2026, 11, 16)));
        closed.Find("#threshold-reached").TextContent.ShouldBe("Riba pasiekta – balsuoti prašė 11 080 dalyvių.");
        closed.Find("#threshold-summary").TextContent.ShouldBe("Bandomoji suvestinė sudaryta 2026-11-16; Seimo nariams ji nesiųsta.");

        var under = Render<ThresholdProgress>(p => p.Add(c => c.Number, Bill).Add(c => c.Threshold, Threshold(40)));
        under.Find(".ruler").GetAttribute("aria-label").ShouldBe("40 dalyvių iš 100");
        under.FindAll("#threshold-reached").ShouldBeEmpty();
        _digests.Calls.ShouldBeEmpty("the day is the server's, given with the page");
    }

    /// <summary>A threshold first reached by a side taken on the page asks the day of the next summary then (design 8.12.2).</summary>
    [Fact]
    public void A_threshold_reached_on_the_page_asks_the_next_summarys_day()
    {
        _appeals.OnEach("GetMineAsync", Mine(null), Mine("for"));
        _appeals.On("TakePositionAsync", ApiResult.Ok());
        _appeals.On("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(new BillRecordResponse(
            Guid.NewGuid(), new BillRefResponse(Bill, "Gyventojų pajamų mokesčio įstatymo pataisa"), "open", "", null, null,
            new SideCountsResponse(100, 0, 0, 0), new ThresholdResponse(100, "default", null, 100, true, false), [])));
        _follows.OnEach("GetMineAsync", Follows(), Follows(Bill));
        _digests.On("GetRecordNextDigestAsync", ApiResult<Lt2.Api.Contracts.Digests.RecordNextDigestResponse>.Ok(new(new DateOnly(2026, 11, 23))));

        var threshold = Render<ThresholdProgress>(p => p.Add(c => c.Number, Bill).Add(c => c.Threshold, Threshold(99)));
        var picker = Render<SidePicker>(p => p.Add(c => c.Number, Bill));
        threshold.FindAll(".ruler").Count.ShouldBe(1);

        picker.Find("#side-for").Click();

        threshold.WaitForAssertion(() => threshold.Find("#threshold-summary").TextContent.ShouldBe("Suvestinė Seimo nariams planuojama 2026-11-23."));
        threshold.Find("#threshold-reached").TextContent.ShouldBe("Riba pasiekta – balsuoti prašo 100 dalyvių.");
        threshold.FindAll(".ruler").ShouldBeEmpty();
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

    /// <summary>
    /// Design 8.7, owner 2026-11-18: no "Atsiimti". The pressed button takes the side back, naming it (the server changes
    /// nothing if the person no longer has it); it is described by the line saying so, and its title says it too. The second
    /// click of a double click does nothing, so a side just taken is not taken back by the same gesture.
    /// </summary>
    [Fact]
    public void Pressing_ones_own_side_again_takes_it_back_and_a_double_clicks_second_press_does_nothing()
    {
        _appeals.OnEach("GetMineAsync", Mine("for"), Mine(null));
        _appeals.On("ClearPositionAsync", ApiResult.Ok());
        _appeals.On("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(new BillRecordResponse(
            Guid.NewGuid(), new BillRefResponse(Bill, "Gyventojų pajamų mokesčio įstatymo pataisa"), "open", "", null, null,
            new SideCountsResponse(0, 0, 0, 0), Threshold(0), [])));
        var picker = Render<SidePicker>(p => p.Add(c => c.Number, Bill));

        picker.Markup.ShouldNotContain("Atsiimti");
        picker.Find("#my-side").TextContent.ShouldBe("Jūs prašote balsuoti už. Tai matote tik jūs. Paspaudę dar kartą, atsiimsite.");
        picker.Find("#side-for").GetAttribute("aria-describedby").ShouldBe("my-side");
        picker.Find("#side-for").GetAttribute("title").ShouldBe("Paspaudę dar kartą, atsiimsite.");
        picker.Find("#side-against").HasAttribute("aria-describedby").ShouldBeFalse();

        picker.Find("#side-for").Click(new Microsoft.AspNetCore.Components.Web.MouseEventArgs { Detail = 2 });
        _appeals.Calls.ShouldNotContain("ClearPositionAsync");

        picker.Find("#side-for").Click();
        picker.WaitForAssertion(() => picker.Find("#side-for").GetAttribute("aria-pressed").ShouldBe("false"));
        ((PositionRequest)_appeals.Arguments[_appeals.Calls.IndexOf("ClearPositionAsync")][1]!).Side.ShouldBe("for");
        _appeals.Calls.ShouldNotContain("TakePositionAsync");
        picker.FindAll("#my-side").ShouldBeEmpty();
        SidePresses.Ignored(0).ShouldBeFalse("a key press");
        SidePresses.Ignored(1).ShouldBeFalse();
        SidePresses.Ignored(2).ShouldBeTrue();
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