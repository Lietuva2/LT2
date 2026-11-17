using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Agenda;
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
/// under the titles carry "Sekate" and their side. A coming bill's side column ("Taking a side in the row") offers "už" and
/// "prieš": one press saves the side, the pressed button shows it, "Atsiimti" withdraws it, the first side asks for the
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

    private static readonly MyAgendaBill[] Bills =
    [
        new(Followed, "Vietinių rinkliavų įstatymo pataisa", "lapkričio 17 d.", "svarstymas, lapkričio 13 d.: už 69 · prieš 31 · susilaikė 18"),
        new(Sided, "Gyventojų pajamų mokesčio įstatymo pataisa", "lapkričio 19 d.", null),
        new(Other, "Atliekų tvarkymo įstatymo pataisa", "lapkričio 19 d.", null),
    ];

    /// <summary>The page's copy of a bill's numbers: nobody yet (up to a minute old).</summary>
    private static readonly AgendaRecordResponse NobodyYet = new(null, "open", new SideCountsResponse(0, 0, 0, 0), new ThresholdResponse(1000, "default", null, 0, false, false));

    private readonly ApiFake _bills;
    private readonly ApiFake _me;
    private readonly ApiFake _appeals;

    public MyAgendaTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var bills = DispatchProxy.Create<IMyBillsApi, ApiFake>();
        _bills = (ApiFake)(object)bills;
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
        _me.On("GetInvitationAsync", ApiResult<MyInvitationResponse>.Ok(new MyInvitationResponse("ABCD-EFGH", "/prisijungti#kodas=ABCD-EFGH", 0, true, null)));
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

    [Fact]
    public void While_the_server_prerenders_nothing_of_the_member_is_asked_or_shown_and_the_sides_are_disabled()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: false));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Bills, Bills).Add(c => c.Numbers, Numbers));
        var mark = Mark(Sided, "2026-11-19-XVP-0412");
        var side = Side(Sided, "2026-11-19-XVP-0412");

        panel.Markup.Trim().ShouldBeEmpty();
        mark.Markup.Trim().ShouldBeEmpty();
        side.Find("#home-nobody-2026-11-19-XVP-0412").TextContent.ShouldBe(Text("Agenda.Nobody"), "as for anyone");
        side.FindAll(".tl-ask button").Select(b => b.HasAttribute("disabled")).ShouldBe([true, true]);
        side.FindAll(".tl-ask button").Select(b => b.GetAttribute("aria-pressed")).ShouldBe(["false", "false"], "nobody's side");
        side.FindAll("#home-withdraw-2026-11-19-XVP-0412").ShouldBeEmpty();
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

    /// <summary>
    /// Owner, 2026-11-17 ("followed only"): a side alone puts no bill in "Jūsų projektai"; the row's own island still shows the
    /// side, from the same answer.
    /// </summary>
    [Fact]
    public void The_panel_lists_only_followed_bills_and_a_side_alone_stays_in_its_row()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _bills.On("GetAsync", Answer(consent: true, new MyBillResponse(Followed, null, true), new MyBillResponse(Sided, "for", false)));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Bills, Bills).Add(c => c.Numbers, Numbers));

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
        sidedSide.Find("#home-withdraw-2026-11-19-XVP-0412").GetAttribute("aria-label").ShouldBe("Atsiimti, ko prašote dėl projekto XVP-0412");
        sidedSide.Find("#home-action-2026-11-19-XVP-0412").GetAttribute("href").ShouldBe("/seimas/XVP-0412");
        sidedSide.Find("#home-action-2026-11-19-XVP-0412").TextContent.ShouldBe(Text("Agenda.Action.Side"));
        followedMark.Markup.ShouldContain(Text("Mine.NoSide"));
        followedSide.FindAll(".tl-ask button").Select(b => b.GetAttribute("aria-pressed")).ShouldBe(["false", "false"]);
        followedSide.FindAll(".sides__check").ShouldBeEmpty("no side yet: both filled, no check mark");
        followedSide.FindAll("#home-action-2026-11-17-XVP-0533").ShouldBeEmpty("the link to the arguments comes with a side");
        otherMark.Markup.Trim().ShouldBeEmpty("not the member's bill");
        _bills.Calls.ShouldBe(["GetAsync"], "one request for the whole page");
    }

    [Fact]
    public void Nobody_yet_is_left_out_only_for_a_member_whose_own_answer_has_a_side()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        // The page's agenda (a minute old) still says nobody, but this member has just taken a side.
        var sided = Side(Sided, "2026-11-19-XVP-0412");
        var followedOnly = Side(Followed, "2026-11-17-XVP-0533");
        var votedSided = Side(Sided, "voted-77", voteId: 77);

        sided.FindAll("#home-nobody-2026-11-19-XVP-0412").ShouldBeEmpty("the member's own side contradicts it");
        followedOnly.Find("#home-nobody-2026-11-17-XVP-0533").TextContent.ShouldBe(Text("Agenda.Nobody"), "followed, but no side of their own");
        votedSided.FindAll("#home-nobody-voted-77").ShouldBeEmpty();
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
        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Bills, Bills).Add(c => c.Numbers, Numbers));
        var mark = Mark(Other, "2026-11-19-XVP-0618");
        var side = Side(Other, "2026-11-19-XVP-0618");
        side.Find("#home-nobody-2026-11-19-XVP-0618").ShouldNotBeNull("the page's copy before the press");

        side.Find("#home-for-2026-11-19-XVP-0618").Click();

        _appeals.Calls.ShouldBe(["TakePositionAsync", "GetRecordAsync"], "the same request as the bill's page, then its public answer");
        _appeals.Arguments[0][0].ShouldBe(Other);
        ((PositionRequest)_appeals.Arguments[0][1]!).Side.ShouldBe("for");
        _me.Calls.ShouldNotContain("GivePositionConsentAsync");
        side.WaitForAssertion(() => side.Find("#home-for-2026-11-19-XVP-0618").GetAttribute("aria-pressed").ShouldBe("true"));
        side.Find("#home-counts-2026-11-19-XVP-0618").TextContent.ShouldContain("1", customMessage: "the bill's live numbers, the member among them");
        side.FindAll("#home-nobody-2026-11-19-XVP-0618").ShouldBeEmpty();
        side.Find("[role=status]").TextContent.ShouldContain(Text("Agenda.Followed"), customMessage: "a first side follows the bill (design 8.11.2)");
        mark.WaitForAssertion(() => mark.Find(".tag").TextContent.ShouldBe(Text("Mine.Following")));
        mark.Find(".choice--for").ShouldNotBeNull();
        panel.WaitForAssertion(() => panel.FindAll("#my-agenda li").Select(li => li.Id).ShouldBe(["my-agenda-XVP-0412", "my-agenda-XVP-0618"]));
    }

    [Fact]
    public void The_other_button_changes_the_side_and_Atsiimti_withdraws_it()
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

        side.Find("#home-for-2026-11-19-XVP-0412").Click();
        _appeals.Calls.ShouldBeEmpty("pressing the member's own side again changes nothing");

        side.Find("#home-against-2026-11-19-XVP-0412").Click();
        side.WaitForAssertion(() => side.Find("#home-against-2026-11-19-XVP-0412").GetAttribute("aria-pressed").ShouldBe("true"));
        side.Find("#home-for-2026-11-19-XVP-0412").GetAttribute("aria-pressed").ShouldBe("false");
        side.Find("[role=status]").TextContent.Trim().ShouldBeEmpty("already followed: nothing new to say");

        side.Find("#home-withdraw-2026-11-19-XVP-0412").Click();
        _appeals.Calls.ShouldBe(["TakePositionAsync", "GetRecordAsync", "ClearPositionAsync", "GetRecordAsync"]);
        side.WaitForAssertion(() => side.FindAll(".tl-ask button").Select(b => b.GetAttribute("aria-pressed")).ShouldBe(["false", "false"]));
        side.FindAll("#home-withdraw-2026-11-19-XVP-0412").ShouldBeEmpty();
        side.Find("#home-nobody-2026-11-19-XVP-0412").ShouldNotBeNull("the live answer: nobody now");
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
        // Design 8.7, "The row lines up": a side's word, count and bracket stay together, so do the participants with the
        // threshold, and "riba pasiekta"; the "·" ends a part, so no line starts with it.
        var record = new AgendaRecordResponse(null, "open", new SideCountsResponse(6800, 6195, 4280, 3917), new ThresholdResponse(10000, "default", null, 11080, true, false));
        var figures = Render<RecordFigures>(p => p.Add(c => c.Record, record).Add(c => c.Id, "2026-11-19-XVP-0412"));
        var forPart = $"{Text("Agenda.For")} {Display.Number(6800)} ({Display.Number(6195)})";
        var againstPart = $"{Text("Agenda.Against")} {Display.Number(4280)} ({Display.Number(3917)})";

        figures.FindAll("#home-counts-2026-11-19-XVP-0412 > .nowrap").Select(e => Flat(e.TextContent)).ShouldBe([Flat(forPart + " ·"), Flat(againstPart)]);
        var threshold = figures.FindAll(".nowrap").Skip(2).Select(e => Flat(e.TextContent)).ToList();
        threshold.Count.ShouldBe(2, "the participants with the threshold, and \"riba pasiekta\"");
        threshold[0].ShouldStartWith(Flat(Display.Number(11080) + " "));
        threshold[0].ShouldEndWith(Flat(Display.Number(10000) + " ·"));
        threshold[1].ShouldBe(Text("Agenda.Reached"));
        Flat(figures.Find("#home-counts-2026-11-19-XVP-0412").TextContent).ShouldBe(Flat(forPart + " · " + againstPart), "the same words as before, only kept together");
    }

    /// <summary>The text with every run of white space (the numbers' own spaces included) as one space.</summary>
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
}
