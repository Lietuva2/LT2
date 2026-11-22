using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Agenda;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.12.5: "Tik mano" on the bills list. The box and the section "Mano projektai" are islands: while the server prerenders
/// nothing of a member is asked or shown and a visitor sees no box; ticked, the member's followed bills show with their own
/// side in the front page's rows and the page's own list is hidden by an attribute on the page's root element, which goes away
/// when the box is cleared or taken away.
/// </summary>
public sealed class MineOnlyTests : BunitContext
{
    private const string Followed = "XVP-0533";
    private const string Sided = "XVP-0412";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFake _bills;
    private readonly ApiFake _follows;

    public MineOnlyTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var bills = DispatchProxy.Create<IMyBillsApi, ApiFake>();
        _bills = (ApiFake)(object)bills;
        var follows = DispatchProxy.Create<IFollowsApi, ApiFake>();
        _follows = (ApiFake)(object)follows;
        Services.AddSingleton(bills);
        Services.AddSingleton(follows);
        Services.AddSingleton(DispatchProxy.Create<IMeApi, ApiFake>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        _bills.On("GetAsync", ApiResult<MyBillsResponse>.Ok(new MyBillsResponse([new MyBillResponse(Sided, "for", true)], true)));
        _follows.On("GetMineAsync", Follows(Bill(Followed, plannedIn: 2), Bill(Sided)));
    }

    private static FollowedBillResponse Bill(string number, int? plannedIn = null) => new(
        number, "Pataisa " + number, plannedIn is { } days ? new PlannedDateResponse(Today.AddDays(days), "consideration", null, false) : null, null);

    private static ApiResult<MyFollowsResponse> Follows(params FollowedBillResponse[] bills) => ApiResult<MyFollowsResponse>.Ok(new MyFollowsResponse(bills, [], []));

    private void SignedIn() => AddAuthorization().SetAuthorized("member@pavyzdys.test");

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    [Fact]
    public void While_the_server_prerenders_there_is_no_box_and_nothing_of_the_member_is_asked()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: false));

        var box = Render<MineOnlyBox>();
        var section = Render<MineBills>();

        box.Markup.Trim().ShouldBeEmpty();
        section.Markup.Trim().ShouldBeEmpty();
        _follows.Calls.ShouldBeEmpty();
        _bills.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void A_visitor_sees_no_box()
    {
        AddAuthorization();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var box = Render<MineOnlyBox>();

        box.FindAll("#mine-only").ShouldBeEmpty();
    }

    [Fact]
    public void A_member_sees_the_box_clear_and_ticking_it_hides_the_list_and_shows_the_own_bills_with_their_sides()
    {
        SignedIn();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        var box = Render<MineOnlyBox>();
        var section = Render<MineBills>();

        box.Find("#mine-only + span").TextContent.ShouldBe(Text("Mine.Only"));
        box.Find("#mine-only").HasAttribute("name").ShouldBeFalse("the form never sends it");
        section.FindAll("#mine-bills").ShouldBeEmpty("nothing before it is ticked");
        _follows.Calls.ShouldBeEmpty();

        box.Find("#mine-only").Change(true);

        JSInterop.VerifyInvoke("document.documentElement.setAttribute").Arguments.ShouldBe([MineOnlyBox.Attribute, ""]);
        section.WaitForAssertion(() => section.Find("#mine-bills h2").TextContent.ShouldBe(Text("Mine.OnlyTitle")));
        section.FindAll("#mine-bills li").Select(li => li.Id).ShouldBe(["mine-bill-XVP-0533", "mine-bill-XVP-0412"], "planned first");
        section.Find("#mine-bill-XVP-0412 .choice--for").TextContent.ShouldBe(Text("Mine.Side.for"));
        section.Find("#mine-bill-XVP-0533").TextContent.ShouldContain(Text("Mine.NoSideAction"));
        section.Find("#mine-bill-XVP-0533 a.tag").GetAttribute("href").ShouldBe("/seimas/XVP-0533");

        box.Find("#mine-only").Change(false);

        section.WaitForAssertion(() => section.FindAll("#mine-bills").ShouldBeEmpty("the public list is back"));
        JSInterop.VerifyInvoke("document.documentElement.removeAttribute", calledTimes: 2); // the box starts clear, then is cleared
    }

    [Fact]
    public void A_member_who_follows_nothing_is_told_so_and_nothing_else()
    {
        SignedIn();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _follows.On("GetMineAsync", Follows());
        var box = Render<MineOnlyBox>();
        var section = Render<MineBills>();

        box.Find("#mine-only").Change(true);

        section.WaitForAssertion(() => section.Find("#mine-bills-none").TextContent.ShouldBe(Text("Mine.OnlyNone")));
        section.FindAll("li").ShouldBeEmpty();
        _bills.Calls.ShouldBeEmpty("no bills, nothing to ask about");
    }

    [Fact]
    public void A_failed_read_says_so_in_the_usual_message()
    {
        SignedIn();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _follows.On("GetMineAsync", ApiResult<MyFollowsResponse>.Fail(new ApiProblem { Status = 500, Code = "server.error" }));
        var box = Render<MineOnlyBox>();
        var section = Render<MineBills>();

        box.Find("#mine-only").Change(true);

        section.WaitForAssertion(() => section.Find("#mine-bills-failed [role=alert]").TextContent.ShouldBe(Text("Error.Generic")));
    }

    [Fact]
    public async Task Taking_the_box_away_clears_the_attribute_and_the_state()
    {
        SignedIn();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        var box = Render<MineOnlyBox>();
        box.Find("#mine-only").Change(true);
        var state = Services.GetRequiredService<MineOnlyState>();
        state.On.ShouldBeTrue();
        var before = JSInterop.Invocations.Count(i => i.Identifier == "document.documentElement.removeAttribute");

        await DisposeComponentsAsync();

        state.On.ShouldBeFalse();
        JSInterop.Invocations.Count(i => i.Identifier == "document.documentElement.removeAttribute").ShouldBeGreaterThan(before);
    }

    /// <summary>Design 8.12.5: every followed bill, planned ones first by date, then by the last vote (newest first), then by number.</summary>
    [Fact]
    public void The_own_bills_are_ordered_planned_first_then_by_the_last_vote_then_by_number()
    {
        var now = DateTimeOffset.UtcNow;
        FollowedBillResponse Voted(string number, int daysAgo) => Bill(number) with { LastStageResult = new FollowedVoteResponse(1, "consideration", now.AddDays(-daysAgo), true, null, null, null) };

        var ordered = MyAgendaState.Ordered([Bill("XVP-0009"), Voted("XVP-0003", 30), Bill("XVP-0002", plannedIn: 5), Voted("XVP-0004", 1), Bill("XVP-0001", plannedIn: 1), Bill("XVP-0005")]);

        ordered.Select(b => b.Number).ShouldBe(["XVP-0001", "XVP-0002", "XVP-0004", "XVP-0003", "XVP-0005", "XVP-0009"]);
    }

    [Fact]
    public async Task The_own_bills_ask_for_their_sides_within_the_limit_of_a_request()
    {
        var many = Enumerable.Range(1, MyBillsQuery.MaxNumbers + 5).Select(i => Bill($"XVP-{i:0000}")).ToArray();
        _follows.On("GetMineAsync", Follows(many));

        var rows = await Services.GetRequiredService<MyAgendaState>().FollowedAsync();

        rows.ShouldNotBeNull().Count.ShouldBe(many.Length);
        _bills.Calls.ShouldBe(["GetAsync", "GetAsync"]);
        _bills.Arguments.Select(a => ((MyBillsQuery)a[0]!).Numbers.Count).OrderDescending().ShouldBe([MyBillsQuery.MaxNumbers, 5]);
    }
}
