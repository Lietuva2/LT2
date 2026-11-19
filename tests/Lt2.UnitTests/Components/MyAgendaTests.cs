using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
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
/// Design 8.7: the front page's member islands. "Jūsų projektai" lists the member's own bills of the agenda, each with its
/// date, their own side or "Dar nenurodėte, ko prašote", the last vote and what is new (no share link: owner, 2026-11-19);
/// the marks under the titles carry "Sekate", their side and what is new on a followed bill (owner, 2026-11-19). A coming bill's side column ("Taking a side in the row") offers "už" and
/// "prieš": one press saves the side, the pressed button shows it and, pressed again, takes it back (owner, 2026-11-18: no
/// "Atsiimti"), the first side asks for the
/// positions consent in the row, and after the member's own action the row shows the bill's live numbers while the other
/// islands follow without a reload. Nothing shows or can be pressed before the member's own answer has loaded, and all the
/// islands share one request.
/// </summary>
public sealed class MyAgendaTests : BunitContext
{
    private const string Sided = "XVP-0412";
    private const string Followed = "XVP-0533";
    private const string Other = "XVP-0618";

    private static readonly IReadOnlyList<string> Numbers = [Followed, Sided, Other];

    /// <summary>A followed bill on the agenda that the page's rows do not show (owner, 2026-11-18: a few rows a day).</summary>
    private const string Beyond = "XVP-0700";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly DateTimeOffset TwoDaysAgo = DateTimeOffset.UtcNow.AddDays(-2);

    private static FollowedBillResponse Bill(string number, int? plannedIn = null, DateTimeOffset? voted = null) => new(
        number, "Pataisa " + number, plannedIn is { } days ? new PlannedDateResponse(Today.AddDays(days), "consideration", null, false) : null,
        voted is { } at ? new FollowedVoteResponse(77, "consideration", at, false, 69, 31, 18) : null);

    private static ApiResult<MyFollowsResponse> Follows(params FollowedBillResponse[] bills) => ApiResult<MyFollowsResponse>.Ok(new MyFollowsResponse(bills, [], []));

    /// <summary>The page's copy of a bill's numbers: nobody yet (up to a minute old).</summary>
    private static readonly AgendaRecordResponse NobodyYet = new(null, "open", new SideCountsResponse(0, 0, 0, 0), new ThresholdResponse(1000, "default", null, 0, false, false));

    private readonly ApiFake _bills;
    private readonly ApiFake _follows;
    private readonly ApiFake _me;
    private readonly ApiFake _appeals;

    public MyAgendaTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var bills = DispatchProxy.Create<IMyBillsApi, ApiFake>();
        _bills = (ApiFake)(object)bills;
        var follows = DispatchProxy.Create<IFollowsApi, ApiFake>();
        _follows = (ApiFake)(object)follows;
        Services.AddSingleton(follows);
        var me = DispatchProxy.Create<IMeApi, ApiFake>();
        _me = (ApiFake)(object)me;
        var appeals = DispatchProxy.Create<IAppealsApi, ApiFake>();
        _appeals = (ApiFake)(object)appeals;
        Services.AddSingleton(bills);
        Services.AddSingleton(me);
        Services.AddSingleton(appeals);
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _bills.On("GetAsync", Answer(consent: true, new MyBillResponse(Followed, null, true), new MyBillResponse(Sided, "for", true)));
        _follows.On("GetMineAsync", Follows(Bill(Followed, plannedIn: 1, voted: TwoDaysAgo), Bill(Sided, plannedIn: 3)));
        _me.On("GivePositionConsentAsync", ApiResult.Ok());
        _appeals.On("TakePositionAsync", ApiResult.Ok());
        _appeals.On("ClearPositionAsync", ApiResult.Ok());
        _appeals.On("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(Live(Other, 1, 0)));
    }

    private static ApiResult<MyBillsResponse> Answer(bool consent, params MyBillResponse[] rows) => ApiResult<MyBillsResponse>.Ok(new MyBillsResponse(rows, consent));

    private static BillRecordResponse Live(string number, int forCount, int againstCount) => new(
        Guid.CreateVersion7(), new BillRefResponse(number, "Atliekų tvarkymo įstatymo pataisa"), "open", "", null, null,
        new SideCountsResponse(forCount, 0, againstCount, 0), new ThresholdResponse(1000, "default", null, forCount + againstCount, false, false), []);

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    private IRenderedComponent<MyAgendaSide> Side(string number, string place, long? voteId = null) => Render<MyAgendaSide>(p => p
        .Add(c => c.Number, number).Add(c => c.Place, place).Add(c => c.Numbers, Numbers).Add(c => c.Record, NobodyYet).Add(c => c.VoteId, voteId));

    private IRenderedComponent<MyAgendaMark> Mark(string number, string place) => Render<MyAgendaMark>(p => p
        .Add(c => c.Number, number).Add(c => c.Place, place).Add(c => c.Numbers, Numbers));

    /// <summary>
    /// Owner, 2026-11-19 ("the same like start page"): the row's mark says what is new on a followed bill, as "Jūsų projektai"
    /// does (design 8.11.8), as a small link to the bill's page, from the same answer: no request of its own.
    /// </summary>
    [Fact]
    public void A_rows_mark_says_what_is_new_on_a_followed_bill_as_a_link_to_it()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _bills.On("GetAsync", Answer(consent: true,
            new MyBillResponse(Followed, null, true, new BillNewsCountsResponse(1, 3, 0, 0)), new MyBillResponse(Sided, "for", true)));

        var followed = Mark(Followed, "2026-11-19-XVP-0533");
        var quiet = Mark(Sided, "2026-11-19-XVP-0412");

        var news = followed.Find("#home-new-2026-11-19-XVP-0533");
        news.TextContent.ShouldBe("1 naujas argumentas · 3 nauji komentarai");
        news.GetAttribute("href").ShouldBe("/seimas/XVP-0533");
        quiet.FindAll("a").ShouldBeEmpty("nothing new, no line");
        _bills.Calls.ShouldBe(["GetAsync"], "the marks share the page's one answer");
    }

    [Fact]
    public void While_the_server_prerenders_nothing_of_the_member_is_asked_or_shown_and_the_sides_are_disabled()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: false));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Numbers, Numbers));
        var mark = Mark(Sided, "2026-11-19-XVP-0412");
        var side = Side(Sided, "2026-11-19-XVP-0412");

        panel.Markup.Trim().ShouldBeEmpty();
        _follows.Calls.ShouldBeEmpty();
        mark.Markup.Trim().ShouldBeEmpty();
        side.FindAll("#home-nobody-2026-11-19-XVP-0412").ShouldBeEmpty("the sides stand there instead, as for anyone (owner, 2026-11-19)");
        side.FindAll(".tl-ask button").Select(b => b.HasAttribute("disabled")).ShouldBe([true, true]);
        side.FindAll(".tl-ask button").Select(b => b.GetAttribute("aria-pressed")).ShouldBe(["false", "false"], "nobody's side");
        side.FindAll(".tl-ask button[title]").ShouldBeEmpty();
        _bills.Calls.ShouldBeEmpty();
        _me.Calls.ShouldBeEmpty();
        _appeals.Calls.ShouldBeEmpty();
    }

    /// <summary>Design 8.5.4 (owner, 2026-11-17): the MP's own account takes no side; its rows keep the numbers only.</summary>
    [Fact]
    public void The_mps_own_account_gets_no_sides_in_the_rows()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _bills.On("GetAsync", ApiResult<MyBillsResponse>.Ok(new MyBillsResponse([], false, MpAccount: true)));

        var side = Side(Other, "2026-11-19-XVP-0618");

        side.FindAll(".tl-ask").ShouldBeEmpty();
        side.FindAll("button").ShouldBeEmpty();
        side.Find("#home-nobody-2026-11-19-XVP-0618").TextContent.ShouldBe(Text("Agenda.Nobody"), "the numbers stay");
    }

    [Fact]
    public void Each_side_button_names_the_whole_request_with_the_bills_number()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var side = Side(Other, "2026-11-19-XVP-0618");

        side.Find("#home-for-2026-11-19-XVP-0618").GetAttribute("aria-label").ShouldBe("Prašau balsuoti už projektą XVP-0618");
        side.Find("#home-against-2026-11-19-XVP-0618").GetAttribute("aria-label").ShouldBe("Prašau balsuoti prieš projektą XVP-0618");
        side.Find("#home-for-2026-11-19-XVP-0618").TextContent.ShouldBe(Text("Agenda.For"));
        side.Find(".tl-ask .small").TextContent.ShouldBe(Text("Agenda.Ask"));
    }

    [Fact]
    public void The_panel_lists_only_the_members_own_bills_with_their_side_and_the_last_vote_and_no_share_link()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Numbers, Numbers));

        panel.FindAll("#my-agenda li").Select(li => li.Id).ShouldBe(["my-agenda-XVP-0533", "my-agenda-XVP-0412"], "the member's own, by planned date");
        var followed = panel.Find("#my-agenda-XVP-0533");
        followed.TextContent.ShouldContain(Text("Mine.NoSideAction"));
        followed.TextContent.ShouldContain($"{Display.Day(Today.AddDays(1))}");
        followed.TextContent.ShouldContain($"{Display.Day(TwoDaysAgo)}: už 69, prieš 31, susilaikė 18");
        var sided = panel.Find("#my-agenda-XVP-0412");
        sided.QuerySelector(".choice--for")!.TextContent.ShouldBe(Text("Mine.Side.for"));
        panel.FindAll("#my-agenda button").ShouldBeEmpty("no share link, a side or not (owner, 2026-11-19: the bill's page keeps it)");
        _me.Calls.ShouldNotContain("GetInvitationAsync", "the panel reads no invitation");
        _bills.Calls.ShouldBe(["GetAsync"], "every listed bill is among the rows shown: one request");
        ((MyBillsQuery)_bills.Arguments[0][0]!).Numbers.ShouldBe(Numbers);
    }

    /// <summary>
    /// Owner, 2026-11-18: the timeline shows a few rows a day, so "Jūsų projektai" finds the member's followed bills on the
    /// agenda or voted on in the last week from their own follows, not from the rows shown; their sides come in a request of
    /// their own, and a followed bill neither planned nor voted on this week stays out.
    /// </summary>
    [Fact]
    public void The_panel_finds_followed_bills_of_the_agenda_beyond_the_rows_shown()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _follows.On("GetMineAsync", Follows(
            Bill(Beyond, plannedIn: 5), Bill("XVP-0800", voted: DateTimeOffset.UtcNow.AddDays(-30)), Bill(Followed, voted: TwoDaysAgo), Bill(Sided, plannedIn: 3)));
        _bills.OnEach("GetAsync",
            Answer(consent: true, new MyBillResponse(Followed, null, true), new MyBillResponse(Sided, "for", true)),
            Answer(consent: true, new MyBillResponse(Beyond, "against", true)));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Numbers, Numbers));

        panel.WaitForAssertion(() => panel.FindAll("#my-agenda li").Select(li => li.Id)
            .ShouldBe(["my-agenda-XVP-0412", "my-agenda-XVP-0700", "my-agenda-XVP-0533"], "planned by date, then voted"));
        panel.Find("#my-agenda-XVP-0700 .choice--against").TextContent.ShouldBe(Text("Mine.Side.against"));
        _bills.Calls.ShouldBe(["GetAsync", "GetAsync"]);
        ((MyBillsQuery)_bills.Arguments[1][0]!).Numbers.ShouldBe([Beyond], "only the bills the rows did not ask about");
    }

    [Fact]
    public void The_panels_scope_is_the_agenda_and_the_last_week_and_its_requests_keep_within_the_limit()
    {
        var now = DateTimeOffset.UtcNow;
        var listed = MyAgendaState.OnTheAgenda(
            [Bill("XVP-0003", voted: now.AddDays(-1)), Bill("XVP-0002", plannedIn: 4), Bill("XVP-0009", voted: now.AddDays(-8)), Bill("XVP-0001", plannedIn: 2, voted: now.AddDays(-20))],
            now);
        listed.Select(b => b.Number).ShouldBe(["XVP-0001", "XVP-0002", "XVP-0003"]);

        var many = Enumerable.Range(1, MyBillsQuery.MaxNumbers * 2 + 1).Select(i => $"XVP-{i:0000}").ToList();
        var requests = MyAgendaState.Requests(many).ToList();
        requests.Select(r => r.Count).ShouldBe([MyBillsQuery.MaxNumbers, MyBillsQuery.MaxNumbers, 1]);
        requests.SelectMany(r => r).ShouldBe(many);
    }

    /// <summary>
    /// Owner, 2026-11-17 ("followed only"): a side alone puts no bill in "Jūsų projektai"; the row's own island still shows the
    /// side, from the same answer.
    /// </summary>
    [Fact]
    public void The_panel_lists_only_followed_bills_and_a_side_alone_stays_in_its_row()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _bills.On("GetAsync", Answer(consent: true, new MyBillResponse(Followed, null, true), new MyBillResponse(Sided, "for", false)));
        _follows.On("GetMineAsync", Follows(Bill(Followed, plannedIn: 1)));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Numbers, Numbers));

        panel.FindAll("#my-agenda li").Select(li => li.Id).ShouldBe(["my-agenda-XVP-0533"]);
        var mark = Mark(Sided, "2026-11-19-XVP-0412");
        mark.Find(".choice--for").TextContent.ShouldBe(Text("Mine.Side.for"));
    }

    [Fact]
    public void The_islands_share_the_one_request_and_the_pressed_button_shows_the_members_side()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var sidedMark = Mark(Sided, "2026-11-19-XVP-0412");
        var sidedSide = Side(Sided, "2026-11-19-XVP-0412");
        var followedMark = Mark(Followed, "2026-11-17-XVP-0533");
        var followedSide = Side(Followed, "2026-11-17-XVP-0533");
        var otherMark = Mark(Other, "2026-11-19-XVP-0618");

        sidedMark.Find(".tag").TextContent.ShouldBe(Text("Mine.Following"));
        sidedMark.Find(".choice--for").TextContent.ShouldBe(Text("Mine.Side.for"));
        sidedSide.Find("#home-for-2026-11-19-XVP-0412").GetAttribute("aria-pressed").ShouldBe("true");
        sidedSide.Find("#home-against-2026-11-19-XVP-0412").GetAttribute("aria-pressed").ShouldBe("false");
        sidedSide.Find("#home-for-2026-11-19-XVP-0412 .sides__check").GetAttribute("aria-hidden").ShouldBe("true", "the check mark before the chosen side is decorative");
        sidedSide.FindAll("#home-against-2026-11-19-XVP-0412 .sides__check").ShouldBeEmpty();
        sidedSide.Find(".tl-ask__sides").ClassList.ShouldContain("sides", customMessage: "styled as the bill page's sides");
        sidedSide.FindAll(".tl-ask button").ShouldAllBe(b => !b.HasAttribute("disabled"));
        sidedSide.FindAll("#home-withdraw-2026-11-19-XVP-0412").ShouldBeEmpty("no \"Atsiimti\" (owner, 2026-11-18)");
        // Owner, 2026-11-19: no line under the buttons on taking the side back (a later onboarding is to explain it), only the
        // pressed button's title; and no link to the arguments: the title leads to the bill.
        sidedSide.Find("#home-for-2026-11-19-XVP-0412").GetAttribute("title").ShouldBe("Paspaudę dar kartą, atsiimsite.");
        sidedSide.Find("#home-against-2026-11-19-XVP-0412").HasAttribute("title").ShouldBeFalse();
        sidedSide.FindAll(".tl-ask button[aria-describedby]").ShouldBeEmpty();
        sidedSide.Find(".tl-item__side").TextContent.ShouldNotContain("Paspaudę dar kartą");
        sidedSide.FindAll(".tl-item__side a").ShouldBeEmpty("no \"Argumentai ir pozicijos\"");
        followedMark.Markup.ShouldContain(Text("Mine.NoSide"));
        followedSide.FindAll(".tl-ask button").Select(b => b.GetAttribute("aria-pressed")).ShouldBe(["false", "false"]);
        followedSide.FindAll(".sides__check").ShouldBeEmpty("no side yet: both filled, no check mark");

        otherMark.Markup.Trim().ShouldBeEmpty("not the member's bill");
        _bills.Calls.ShouldBe(["GetAsync"], "one request for the whole page");
    }

    /// <summary>
    /// Owner, 2026-11-19 ("minimise non-essential texts"): beside the side buttons no line says nobody took part yet; a row
    /// without them (a vote's) keeps it, unless the member's own answer has a side (the page's agenda is a minute old).
    /// </summary>
    [Fact]
    public void Nobody_yet_is_left_out_beside_the_sides_and_for_a_member_whose_own_answer_has_a_side()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        // The page's agenda (a minute old) still says nobody, but this member has just taken a side.
        var sided = Side(Sided, "2026-11-19-XVP-0412");
        var followedOnly = Side(Followed, "2026-11-17-XVP-0533");
        var votedSided = Side(Sided, "voted-77", voteId: 77);
        var votedFollowedOnly = Side(Followed, "voted-78", voteId: 78);

        sided.FindAll("#home-nobody-2026-11-19-XVP-0412").ShouldBeEmpty("the member's own side contradicts it");
        followedOnly.FindAll("#home-nobody-2026-11-17-XVP-0533").ShouldBeEmpty("the sides stand there instead");
        followedOnly.FindAll(".tl-ask button").Count.ShouldBe(2);
        votedSided.FindAll("#home-nobody-voted-77").ShouldBeEmpty();
        votedFollowedOnly.Find("#home-nobody-voted-78").TextContent.ShouldBe(Text("Agenda.Nobody"), "a vote's row has no buttons: it says so");
        votedSided.FindAll(".tl-ask").ShouldBeEmpty("a vote's row offers no sides");
        votedSided.Find("a.cta").GetAttribute("href").ShouldBe("/seimas/balsavimai/77");
    }

    [Fact]
    public void A_row_whose_copy_says_nobody_but_the_member_has_a_side_reads_the_live_numbers_once_on_start()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _appeals.On("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(Live(Sided, 3, 1)));

        var sided = Side(Sided, "2026-11-19-XVP-0412");

        _appeals.Calls.ShouldBe(["GetRecordAsync"]);
        _appeals.Arguments[0][0].ShouldBe(Sided);
        sided.Find("#home-counts-2026-11-19-XVP-0412").TextContent.ShouldContain("3");
        sided.FindAll("#home-nobody-2026-11-19-XVP-0412").ShouldBeEmpty();

        // No request in any other case: no side of their own, or a copy that already has numbers.
        _appeals.Calls.Clear();
        Side(Followed, "2026-11-17-XVP-0533");
        Render<MyAgendaSide>(p => p.Add(c => c.Number, Sided).Add(c => c.Place, "2026-11-20-XVP-0412").Add(c => c.Numbers, Numbers)
            .Add(c => c.Record, NobodyYet with { Counts = new SideCountsResponse(5, 4, 2, 2) }));
        _appeals.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void One_press_saves_the_side_shows_the_live_numbers_and_the_other_islands_follow_without_a_reload()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _bills.OnEach("GetAsync",
            Answer(consent: true, new MyBillResponse(Sided, "for", true)),
            Answer(consent: true, new MyBillResponse(Sided, "for", true), new MyBillResponse(Other, "for", true)));
        _follows.OnEach("GetMineAsync", Follows(Bill(Sided, plannedIn: 1)), Follows(Bill(Sided, plannedIn: 1), Bill(Other, plannedIn: 2)));
        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Numbers, Numbers));
        var mark = Mark(Other, "2026-11-19-XVP-0618");
        var side = Side(Other, "2026-11-19-XVP-0618");
        side.FindAll("#home-counts-2026-11-19-XVP-0618").ShouldBeEmpty("the page's copy before the press: nobody yet");

        side.Find("#home-for-2026-11-19-XVP-0618").Click();

        _appeals.Calls.ShouldBe(["TakePositionAsync", "GetRecordAsync"], "the same request as the bill's page, then its public answer");
        _appeals.Arguments[0][0].ShouldBe(Other);
        ((PositionRequest)_appeals.Arguments[0][1]!).Side.ShouldBe("for");
        _me.Calls.ShouldNotContain("GivePositionConsentAsync");
        side.WaitForAssertion(() => side.Find("#home-for-2026-11-19-XVP-0618").GetAttribute("aria-pressed").ShouldBe("true"));
        side.Find("#home-counts-2026-11-19-XVP-0618").TextContent.ShouldContain("1", customMessage: "the bill's live numbers, the member among them");
        side.FindAll("#home-nobody-2026-11-19-XVP-0618").ShouldBeEmpty();
        side.FindAll("[role=status]").Select(s => s.TextContent.Trim()).ShouldAllBe(s => s.Length == 0, "no line says the bill is followed now: the mark's \"Sekate\" does (owner, 2026-11-19)");
        side.Find(".tl-item__side").TextContent.ShouldNotContain("Paspaudę dar kartą", customMessage: "no line after a press either (owner, 2026-11-19)");
        side.Find("#home-for-2026-11-19-XVP-0618").GetAttribute("title").ShouldBe("Paspaudę dar kartą, atsiimsite.");
        mark.WaitForAssertion(() => mark.Find(".tag").TextContent.ShouldBe(Text("Mine.Following")));
        mark.Find(".choice--for").ShouldNotBeNull();
        panel.WaitForAssertion(() => panel.FindAll("#my-agenda li").Select(li => li.Id).ShouldBe(["my-agenda-XVP-0412", "my-agenda-XVP-0618"]));
    }

    [Fact]
    public void The_other_button_changes_the_side_and_the_pressed_one_takes_it_back()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _bills.OnEach("GetAsync",
            Answer(consent: true, new MyBillResponse(Sided, "for", true)),
            Answer(consent: true, new MyBillResponse(Sided, "against", true)),
            Answer(consent: true, new MyBillResponse(Sided, null, true)));
        _appeals.OnEach("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(Live(Sided, 1, 0)), ApiResult<BillRecordResponse>.Ok(Live(Sided, 0, 1)), ApiResult<BillRecordResponse>.Ok(Live(Sided, 0, 0)));
        var side = Side(Sided, "2026-11-19-XVP-0412");
        _appeals.Calls.ShouldBe(["GetRecordAsync"], "the page's copy says nobody, the member has a side: the live numbers once");
        _appeals.Calls.Clear();

        // The second click of a double click on the pressed side does nothing (owner, 2026-11-18).
        side.Find("#home-for-2026-11-19-XVP-0412").Click(new MouseEventArgs { Detail = 2 });
        _appeals.Calls.ShouldBeEmpty("a double click's second press takes nothing back");

        side.Find("#home-against-2026-11-19-XVP-0412").Click();
        side.WaitForAssertion(() => side.Find("#home-against-2026-11-19-XVP-0412").GetAttribute("aria-pressed").ShouldBe("true"));
        side.Find("#home-for-2026-11-19-XVP-0412").GetAttribute("aria-pressed").ShouldBe("false");
        side.Find("[role=status]").TextContent.Trim().ShouldBeEmpty("already followed: nothing new to say");

        side.Find("#home-against-2026-11-19-XVP-0412").Click();
        _appeals.Calls.ShouldBe(["TakePositionAsync", "GetRecordAsync", "ClearPositionAsync", "GetRecordAsync"]);
        _appeals.Arguments.Select(a => a.ElementAtOrDefault(1)).OfType<PositionRequest>().Last().Side.ShouldBe("against", "the side being taken back is named: the server changes nothing if it is no longer the member's");
        side.WaitForAssertion(() => side.FindAll(".tl-ask button").Select(b => b.GetAttribute("aria-pressed")).ShouldBe(["false", "false"]));
        side.FindAll(".tl-ask button[title]").ShouldBeEmpty();
        side.FindAll("#home-counts-2026-11-19-XVP-0412").ShouldBeEmpty("the live answer: nobody now");
        side.FindAll("#home-nobody-2026-11-19-XVP-0412").ShouldBeEmpty("beside the sides it is not said");
    }

    [Fact]
    public void A_first_side_asks_for_the_consent_in_the_row_and_saves_nothing_before_it()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _bills.OnEach("GetAsync", Answer(consent: false), Answer(consent: true, new MyBillResponse(Other, "against", true)));
        var side = Side(Other, "2026-11-19-XVP-0618");

        side.Find("#home-against-2026-11-19-XVP-0618").Click();

        var consent = side.Find("#home-consent-2026-11-19-XVP-0618");
        consent.ClassList.ShouldContain("tl-item__consent", customMessage: "across the row's whole width");
        consent.QuerySelector("#home-2026-11-19-XVP-0618-consent-title")!.TextContent.ShouldBe(Text("Consent.Title"));
        consent.TextContent.ShouldContain(Text("Consent.Text"));
        side.Find("#home-2026-11-19-XVP-0618-consent-submit").HasAttribute("disabled").ShouldBeTrue("not before the box is checked");
        _appeals.Calls.ShouldBeEmpty("nothing is saved before the consent");
        _me.Calls.ShouldNotContain("GivePositionConsentAsync");

        // "Atšaukti" leaves the row as it was.
        side.Find("#home-2026-11-19-XVP-0618-consent-cancel").Click();
        side.FindAll("#home-consent-2026-11-19-XVP-0618").ShouldBeEmpty();
        side.FindAll(".tl-ask button").Select(b => b.GetAttribute("aria-pressed")).ShouldBe(["false", "false"]);
        _appeals.Calls.ShouldBeEmpty();

        side.Find("#home-against-2026-11-19-XVP-0618").Click();
        side.Find("#home-2026-11-19-XVP-0618-consent-check").Change(true);
        side.Find("#home-2026-11-19-XVP-0618-consent-submit").TextContent.ShouldBe(Text("Consent.Submit.against"));
        side.Find("#home-2026-11-19-XVP-0618-consent-submit").Click();

        _me.Calls.ShouldContain("GivePositionConsentAsync");
        ((PositionConsentRequest)_me.Arguments[_me.Calls.IndexOf("GivePositionConsentAsync")][0]!).TextVersion.ShouldBe(PositionConsentText.Version);
        _appeals.Calls.ShouldBe(["TakePositionAsync", "GetRecordAsync"]);
        side.WaitForAssertion(() => side.Find("#home-against-2026-11-19-XVP-0618").GetAttribute("aria-pressed").ShouldBe("true"));
        side.FindAll("#home-consent-2026-11-19-XVP-0618").ShouldBeEmpty();
    }

    [Fact]
    public void A_side_the_server_refuses_for_a_missing_consent_asks_for_it_in_the_row()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _appeals.On("TakePositionAsync", ApiResult.Fail(new ApiProblem { Status = 409, Code = "consent.required", Detail = "Reikia sutikimo." }));
        var side = Side(Other, "2026-11-19-XVP-0618");

        side.Find("#home-for-2026-11-19-XVP-0618").Click();

        side.Find("#home-consent-2026-11-19-XVP-0618").ShouldNotBeNull("the consent was withdrawn elsewhere: asked again");
        side.FindAll("[role=alert]").ShouldBeEmpty();
        _appeals.Calls.ShouldBe(["TakePositionAsync"]);
    }

    [Fact]
    public void A_refusal_shows_in_the_row()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _appeals.On("TakePositionAsync", ApiResult.Fail(new ApiProblem { Status = 409, Code = "record.closed", Detail = "Projektas jau uždarytas." }));
        var side = Side(Other, "2026-11-19-XVP-0618");

        side.Find("#home-for-2026-11-19-XVP-0618").Click();

        side.Find("[role=alert]").TextContent.ShouldBe("Projektas jau uždarytas.");
        side.Find("#home-for-2026-11-19-XVP-0618").GetAttribute("aria-pressed").ShouldBe("false");
    }

    [Fact]
    public void A_line_of_numbers_breaks_only_between_its_parts()
    {
        // Design 8.7, "The row lines up": a side's word, count and bracket stay together, so do the participants, and "riba
        // pasiekta"; the "·" ends a part, so no line starts with it. Reached, the line no longer names the threshold's number
        // (design 8.12.2, owner 2026-11-18); under it, the participants keep the threshold beside them.
        var record = new AgendaRecordResponse(null, "open", new SideCountsResponse(6800, 6195, 4280, 3917), new ThresholdResponse(10000, "default", null, 11080, true, false));
        var figures = Render<RecordFigures>(p => p.Add(c => c.Record, record).Add(c => c.Id, "2026-11-19-XVP-0412"));
        var forPart = $"{Text("Agenda.For")} {Display.Number(6800)} ({Display.Number(6195)})";
        var againstPart = $"{Text("Agenda.Against")} {Display.Number(4280)} ({Display.Number(3917)})";

        figures.FindAll("#home-counts-2026-11-19-XVP-0412 > .nowrap").Select(e => Flat(e.TextContent)).ShouldBe([Flat(forPart + " ·"), Flat(againstPart)]);
        var threshold = figures.FindAll(".nowrap").Skip(2).Select(e => Flat(e.TextContent)).ToList();
        threshold.Count.ShouldBe(2, "the participants, and \"riba pasiekta\"");
        threshold[0].ShouldBe(Flat(Display.Number(11080) + " " + Text("Agenda.Participants.Many") + " ·"));
        threshold[1].ShouldBe(Text("Agenda.Reached"));
        Flat(figures.Find("#home-counts-2026-11-19-XVP-0412").TextContent).ShouldBe(Flat(forPart + " · " + againstPart), "the same words as before, only kept together");

        var under = Render<RecordFigures>(p => p.Add(c => c.Record, record with { Threshold = record.Threshold with { Participants = 9000, Reached = false } }));
        var progress = under.FindAll(".nowrap").Skip(2).Select(e => Flat(e.TextContent)).ToList();
        progress.ShouldHaveSingleItem().ShouldStartWith(Flat(Display.Number(9000) + " "));
        progress[0].ShouldEndWith(Flat(Display.Number(10000)));
    }

    /// <summary>The text with every run of white space (the numbers' own spaces included) as one space.</summary>
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
}
