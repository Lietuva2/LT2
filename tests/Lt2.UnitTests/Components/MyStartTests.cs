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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.28.3: the new member's start on the front page. The served title is "Sveiki" for every member; the browser
/// completes it, "Sveiki prisijungę" with the four steps for a member who has never taken a side (the positions consent not
/// given), "Sveiki sugrįžę" and no start for everyone else (an MP's own account included). The start goes away without a
/// reload when the member's first side is taken, and "Jūsų projektai" steps aside while the start is shown and nothing
/// followed is on the agenda. Design 8.28.5: "Profilis išsaugotas." once, from the address's mark.
/// </summary>
public sealed class MyStartTests : BunitContext
{
    private static readonly IReadOnlyList<string> Numbers = ["XVP-0412", "XVP-0533"];

    private readonly ApiFake _bills;
    private readonly ApiFake _follows;
    private readonly ApiFake _me;

    public MyStartTests()
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
        Services.AddSingleton(DispatchProxy.Create<IAppealsApi, ApiFake>());
        _me = MeFakes.Add(Services);
        AddAuthorization().SetAuthorized("member@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        _bills.On("GetAsync", Answer(consent: false));
        _follows.On("GetMineAsync", ApiResult<MyFollowsResponse>.Ok(new MyFollowsResponse([], [], [])));
    }

    private static ApiResult<MyBillsResponse> Answer(bool consent, bool mp = false) =>
        ApiResult<MyBillsResponse>.Ok(new MyBillsResponse([new MyBillResponse("XVP-0412", null, false)], consent, mp));

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    private IRenderedComponent<MyStart> Start(string sending = "off", IReadOnlyList<string>? numbers = null) =>
        Render<MyStart>(p => p.Add(c => c.Numbers, numbers ?? Numbers).Add(c => c.Sending, sending));

    private void Interactive() => SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

    [Fact]
    public void The_served_page_says_Sveiki_with_no_start_and_asks_nothing()
    {
        SetRendererInfo(new RendererInfo("Server", isInteractive: false));

        var start = Start();

        start.Find("#home-title").TextContent.ShouldBe("Sveiki");
        start.FindAll("#home-start").ShouldBeEmpty();
        _bills.Calls.ShouldBeEmpty();
        _me.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void A_member_who_never_took_a_side_is_greeted_as_new_and_gets_the_four_steps_in_both_forms()
    {
        Interactive();

        var start = Start();

        start.Find("#home-title").TextContent.ShouldBe("Sveiki prisijungę");
        start.Find("#h-starter").TextContent.ShouldBe("Pradėkite nuo vieno projekto");
        start.FindAll("#home-start-full > li").Count.ShouldBe(4);
        start.FindAll("#home-start-short > li").Count.ShouldBe(4);
        start.Find("#home-start-how").GetAttribute("href").ShouldBe("/metodika#kaip-tai-veikia");
        _bills.Calls.ShouldBe(["GetAsync"], "the rows' own answer, no request of its own");
    }

    [Fact]
    public void The_steps_name_the_controls_by_their_own_texts()
    {
        Interactive();

        var steps = Start().FindAll("#home-start-full > li p").Select(p => p.TextContent).ToList();

        steps[1].ShouldBe($"Prie „{Text("Agenda.Ask")}“ paspauskite „{Text("Agenda.For")}“ arba „{Text("Agenda.Against")}“. Paspaudę dar kartą, atsiimsite.");
        steps[2].ShouldContain($"„{Text("Reasons.EndorseName")}“");
        steps[2].ShouldContain("„Man svarbu“");
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData("dry-run", false)]
    [InlineData("on", false)]
    public void Step_four_says_nothing_is_sent_only_while_that_is_so(string sending, bool says)
    {
        Interactive();

        var full = Start(sending).FindAll("#home-start-full > li p")[3].TextContent;
        var shortStep = Start(sending).FindAll("#home-start-short > li")[3].TextContent;

        full.StartsWith(Text("HowItWorks.4.Text"), StringComparison.Ordinal).ShouldBeTrue("the same text as Kaip tai veikia");
        full.Contains(Text("HowItWorks.4.Trial"), StringComparison.Ordinal).ShouldBe(says);
        shortStep.ShouldNotContain(Text("HowItWorks.4.Trial"));
    }

    [Fact]
    public void A_member_who_has_taken_a_side_is_greeted_back_with_no_start()
    {
        Interactive();
        _bills.On("GetAsync", Answer(consent: true));

        var start = Start();

        start.Find("#home-title").TextContent.ShouldBe("Sveiki sugrįžę");
        start.FindAll("#home-start").ShouldBeEmpty();
    }

    [Fact]
    public void The_mps_own_account_never_sees_the_start()
    {
        Interactive();
        _bills.On("GetAsync", Answer(consent: false, mp: true));

        var start = Start();

        start.Find("#home-title").TextContent.ShouldBe("Sveiki sugrįžę");
        start.FindAll("#home-start").ShouldBeEmpty();
    }

    [Fact]
    public void With_nothing_on_the_agenda_the_accounts_own_answer_decides()
    {
        Interactive();

        var start = Start(numbers: []);

        start.Find("#home-title").TextContent.ShouldBe("Sveiki prisijungę");
        _me.Calls.ShouldBe(["GetAsync"]);
        _bills.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void The_start_goes_away_when_the_first_side_is_taken_without_a_reload()
    {
        Interactive();
        _bills.OnEach("GetAsync", Answer(consent: false), Answer(consent: true));
        var start = Start();
        start.FindAll("#home-start").ShouldHaveSingleItem();

        start.InvokeAsync(() => Services.GetRequiredService<MyAgendaState>().RefreshAsync());

        start.WaitForAssertion(() => start.FindAll("#home-start").ShouldBeEmpty());
        start.Find("#home-title").TextContent.ShouldBe("Sveiki sugrįžę");
    }

    [Fact]
    public void Jusu_projektai_steps_aside_while_the_start_is_shown_and_nothing_followed_is_on_the_agenda()
    {
        Interactive();

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Numbers, Numbers));

        panel.Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void Jusu_projektai_stays_for_a_member_who_has_taken_a_side()
    {
        Interactive();
        _bills.On("GetAsync", Answer(consent: true));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Numbers, Numbers));

        panel.Find("#my-agenda-none").TextContent.ShouldBe(Text("Mine.None"));
    }

    [Fact]
    public void A_new_member_who_follows_a_bill_on_the_agenda_sees_the_start_and_then_the_panel()
    {
        Interactive();
        _follows.On("GetMineAsync", ApiResult<MyFollowsResponse>.Ok(new MyFollowsResponse(
            [new FollowedBillResponse("XVP-0412", "Gyventojų pajamų mokesčio įstatymo pataisa",
                new PlannedDateResponse(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), "consideration", null, false), null)], [], [])));

        var panel = Render<MyAgendaPanel>(p => p.Add(c => c.Numbers, Numbers));

        panel.FindAll("#my-agenda li").ShouldHaveSingleItem();
    }

    [Fact]
    public void Profilis_issaugotas_is_said_once_and_the_mark_leaves_the_address()
    {
        Interactive();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/#profilis-issaugotas");

        var start = Start();

        start.WaitForAssertion(() => start.Find("#home-profile-saved").TextContent.ShouldBe("Profilis išsaugotas."));
        start.Find("#home-profile-saved").Closest("[role=status]").ShouldNotBeNull();
        var replaced = JSInterop.VerifyInvoke("history.replaceState");
        replaced.Arguments[2].ShouldBe("http://localhost/");
    }

    [Fact]
    public void Without_the_mark_nothing_is_said()
    {
        Interactive();

        var start = Start();

        start.FindAll("#home-profile-saved").ShouldBeEmpty();
        JSInterop.Invocations.Where(i => i.Identifier == "history.replaceState").ShouldBeEmpty();
    }
}
