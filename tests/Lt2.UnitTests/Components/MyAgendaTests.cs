using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Agenda;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.7: the front page's member islands. "Jūsų projektai" lists the member's own bills of the agenda, each with its
/// date, their own side or "Dar nenurodėte, ko prašote", the last vote and the share link when they took a side; the marks
/// in the timeline's rows carry "Sekate" and their side, and the action reads "Argumentai ir pozicijos" on a bill with a side.
/// Nothing shows before the member's own answer has loaded, and all the islands share one request.
/// </summary>
public sealed class MyAgendaTests : BunitContext
{
    private static readonly IReadOnlyList<string> Numbers = ["XVP-0533", "XVP-0412", "XVP-0618"];

    private static readonly MyAgendaBill[] Bills =
    [
        new("XVP-0533", "Vietinių rinkliavų įstatymo pataisa", "lapkričio 17 d.", "svarstymas, lapkričio 13 d.: už 69 · prieš 31 · susilaikė 18"),
        new("XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa", "lapkričio 19 d.", null),
        new("XVP-0618", "Atliekų tvarkymo įstatymo pataisa", "lapkričio 19 d.", null),
    ];

    private readonly ApiFake _bills;
    private readonly ApiFake _me;

    public MyAgendaTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var bills = DispatchProxy.Create<IMyBillsApi, ApiFake>();
        _bills = (ApiFake)(object)bills;
        var me = DispatchProxy.Create<IMeApi, ApiFake>();
        _me = (ApiFake)(object)me;
        Services.AddSingleton(bills);
        Services.AddSingleton(me);
        Services.AddSingleton(DispatchProxy.Create<IAppealsApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _bills.On("GetAsync", ApiResult<MyBillsResponse>.Ok(new MyBillsResponse([new("XVP-0533", null, true), new("XVP-0412", "for", true)])));
        _me.On("GetInvitationAsync", ApiResult<MyInvitationResponse>.Ok(new MyInvitationResponse("ABCD-EFGH", "/prisijungti#kodas=ABCD-EFGH", 0, true, null)));
    }

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    [Fact]
    public void While_the_server_prerenders_nothing_of_the_member_is_asked_or_shown()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: false));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Bills, Bills).Add(c => c.Numbers, Numbers));
        var mark = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0412").Add(c => c.Place, "2026-11-19-XVP-0412").Add(c => c.Numbers, Numbers));
        var action = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0412").Add(c => c.Place, "2026-11-19-XVP-0412").Add(c => c.Numbers, Numbers).Add(c => c.Part, "action"));

        panel.Markup.Trim().ShouldBeEmpty();
        mark.Markup.Trim().ShouldBeEmpty();
        action.Find("a").TextContent.ShouldBe(Text("Agenda.Action.TakePart"), "the action reads as for anyone");
        _bills.Calls.ShouldBeEmpty();
        _me.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void The_panel_lists_only_the_members_own_bills_with_their_side_the_last_vote_and_the_share_link()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Bills, Bills).Add(c => c.Numbers, Numbers));

        panel.FindAll("#my-agenda li").Select(li => li.Id).ShouldBe(["my-agenda-XVP-0533", "my-agenda-XVP-0412"], "the member's own, in the page's order");
        var followed = panel.Find("#my-agenda-XVP-0533");
        followed.TextContent.ShouldContain(Text("Mine.NoSideAction"));
        followed.TextContent.ShouldContain("svarstymas, lapkričio 13 d.: už 69 · prieš 31 · susilaikė 18");
        followed.QuerySelectorAll("button").ShouldBeEmpty("no share link without a side");
        var sided = panel.Find("#my-agenda-XVP-0412");
        sided.QuerySelector(".choice--for")!.TextContent.ShouldBe(Text("Mine.Side.for"));
        sided.QuerySelector("#my-agenda-share-XVP-0412").ShouldNotBeNull("the share link once they took a side");
        _bills.Calls.ShouldBe(["GetAsync"]);
        ((MyBillsQuery)_bills.Arguments[0][0]!).Numbers.ShouldBe(Numbers);
    }

    [Fact]
    public void The_marks_in_the_rows_share_the_one_request_and_the_action_reads_differently_on_a_bill_with_a_side()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var sidedMark = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0412").Add(c => c.Place, "2026-11-19-XVP-0412").Add(c => c.Numbers, Numbers));
        var sidedAction = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0412").Add(c => c.Place, "2026-11-19-XVP-0412").Add(c => c.Numbers, Numbers).Add(c => c.Part, "action"));
        var followedMark = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0533").Add(c => c.Place, "2026-11-17-XVP-0533").Add(c => c.Numbers, Numbers));
        var followedAction = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0533").Add(c => c.Place, "2026-11-17-XVP-0533").Add(c => c.Numbers, Numbers).Add(c => c.Part, "action"));
        var otherMark = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0618").Add(c => c.Place, "2026-11-19-XVP-0618").Add(c => c.Numbers, Numbers));

        sidedMark.Find(".tag").TextContent.ShouldBe(Text("Mine.Following"));
        sidedMark.Find(".choice--for").TextContent.ShouldBe(Text("Mine.Side.for"));
        sidedAction.Find("a").TextContent.ShouldBe(Text("Agenda.Action.Side"));
        followedMark.Markup.ShouldContain(Text("Mine.NoSide"));
        followedAction.Find("a").TextContent.ShouldBe(Text("Agenda.Action.TakePart"));
        otherMark.Markup.Trim().ShouldBeEmpty("not the member's bill");
        _bills.Calls.ShouldBe(["GetAsync"], "one request for the whole page");
    }

    [Fact]
    public void Nobody_yet_is_left_out_only_for_a_member_whose_own_answer_has_a_side()
    {
        // While the server prerenders: the public line, as for anyone, so the HTML is the same for every member.
        SetRendererInfo(new RendererInfo("Server", isInteractive: false));
        var prerendered = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0412").Add(c => c.Place, "2026-11-19-XVP-0412").Add(c => c.Numbers, Numbers).Add(c => c.Part, "nobody"));
        prerendered.Find("#home-nobody-2026-11-19-XVP-0412").TextContent.ShouldBe(Text("Agenda.Nobody"));

        // In the browser: the page's agenda (a minute old) still says nobody, but this member has just taken a side.
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        var sided = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0412").Add(c => c.Place, "2026-11-19-XVP-0412").Add(c => c.Numbers, Numbers).Add(c => c.Part, "nobody"));
        var followedOnly = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0533").Add(c => c.Place, "2026-11-17-XVP-0533").Add(c => c.Numbers, Numbers).Add(c => c.Part, "nobody"));
        var notTheirs = Render<MyAgendaMark>(p => p.Add(c => c.Number, "XVP-0618").Add(c => c.Place, "2026-11-19-XVP-0618").Add(c => c.Numbers, Numbers).Add(c => c.Part, "nobody"));

        sided.Markup.Trim().ShouldBeEmpty("the member's own side contradicts it");
        followedOnly.Find("span").TextContent.ShouldBe(Text("Agenda.Nobody"), "followed, but no side of their own");
        notTheirs.Find("span").TextContent.ShouldBe(Text("Agenda.Nobody"));
    }
}
