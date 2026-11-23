using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client;
using Lt2.Web.Client.Parliament;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.12.1, 7.3 (owner, 2026-11-23): the seat map of the bill's latest Seimas vote is an island that must cost nothing
/// while the page is only read. It asks js/lt2-switch.js to tell it when the "Seimas" radio is checked, reads the vote
/// (<c>GET /votes/{id}</c>) only then and once, draws the map from the ballots, and says so when the vote cannot be read.
/// </summary>
public sealed class SeimasSeatMapTests : BunitContext
{
    private const long VoteId = 99100006;

    private static readonly BallotResponse[] Ballots =
    [
        new(new MpLink("jonas-petraitis", "Jonas", "Petraitis"), "RPF", BallotChoices.Against),
        new(new MpLink("ona-jonaitiene", "Ona", "Jonaitienė"), "PSF", BallotChoices.For),
        new(new MpLink("darius-liepaitis", "Darius", "Liepaitis"), null, BallotChoices.DidNotVote),
    ];

    public SeimasSeatMapTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var proxy = DispatchProxy.Create<IParliamentApi, ApiFake>();
        Services.AddSingleton(proxy);
        Api = (ApiFake)(object)proxy;
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private ApiFake Api { get; }

    private IStringLocalizer<UiText> L => Services.GetRequiredService<IStringLocalizer<UiText>>();

    private static VoteResponse Vote(params BallotResponse[] ballots) => new(
        new VoteSummaryResponse(
            new VoteLink(VoteId, new DateTimeOffset(2026, 5, 19, 10, 0, 0, TimeSpan.Zero), "Pritarti projektui", VoteKinds.StageResult, BillStages.Consideration, false, true, null),
            new VoteTotalsResponse(74, 44, 10, 128, 141), null),
        new DateTimeOffset(2026, 5, 19, 9, 0, 0, TimeSpan.Zero), "Gyventojų pajamų mokesčio įstatymo pataisa", [], ballots);

    private IRenderedComponent<SeimasSeatMap> Open() => Render<SeimasSeatMap>(p => p.Add(c => c.VoteId, VoteId));

    private static Task Choose(IRenderedComponent<SeimasSeatMap> map) => map.InvokeAsync(() => map.Instance.Selected());

    [Fact]
    public void Nothing_is_read_and_nothing_is_drawn_until_the_Seimas_radio_is_chosen_and_the_script_is_asked_to_say_when()
    {
        Api.On("GetVoteAsync", ApiResult<VoteResponse>.Ok(Vote(Ballots)));

        var map = Open();

        Api.Calls.ShouldBeEmpty("a page that is only read reads no vote");
        map.Markup.ShouldNotContain("seatmap");
        map.Markup.ShouldNotContain(L["Loading"].Value);
        var watch = JSInterop.Invocations["lt2Switch.watch"].Single();
        watch.Arguments[0].ShouldBe("view-seimas");
    }

    [Fact]
    public async Task Once_the_radio_is_chosen_the_vote_is_read_and_the_seat_map_is_drawn_with_its_heading()
    {
        Api.On("GetVoteAsync", ApiResult<VoteResponse>.Ok(Vote(Ballots)));
        var map = Open();

        await Choose(map);

        Api.Calls.ShouldBe(["GetVoteAsync"]);
        ((long)Api.Arguments[0][0]!).ShouldBe(VoteId);
        map.Find("#seimas-seats h3").TextContent.ShouldBe(L["SeatMap.Heading"].Value);
        map.FindAll("svg.seatmap rect.seat").Count.ShouldBe(3);
        map.Markup.ShouldContain("Jonas Petraitis (RPF): " + L["SeatMap.Choice.against"].Value);
        map.Markup.ShouldContain("Darius Liepaitis (" + L["SeatMap.NoFaction"].Value + "): " + L["SeatMap.Choice.did-not-vote"].Value);
        map.FindAll("#seimas-seats-loading").ShouldBeEmpty("the quiet line goes when the map is there");
    }

    [Fact]
    public async Task The_vote_is_read_once_however_often_the_radio_is_chosen()
    {
        Api.On("GetVoteAsync", ApiResult<VoteResponse>.Ok(Vote(Ballots)));
        var map = Open();

        await Choose(map);
        await Choose(map);
        await Choose(map);

        Api.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task While_the_vote_is_being_read_a_quiet_line_says_so()
    {
        var release = new TaskCompletionSource<ApiResult<VoteResponse>>();
        Api.OnTask("GetVoteAsync", release.Task);

        var map = Open();
        var reading = map.InvokeAsync(() => map.Instance.Selected());

        map.WaitForAssertion(() => map.Find("#seimas-seats-loading").TextContent.ShouldBe(L["Loading"].Value));
        release.SetResult(ApiResult<VoteResponse>.Ok(Vote(Ballots)));
        await reading;
        map.FindAll("#seimas-seats-loading").ShouldBeEmpty();
        map.FindAll("rect.seat").Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_vote_that_cannot_be_read_says_so_draws_nothing_and_the_next_choosing_tries_again()
    {
        Api.OnEach(
            "GetVoteAsync",
            ApiResult<VoteResponse>.Fail(new ApiProblem { Status = 500, Code = "server.error", Detail = "Nepavyko perskaityti balsavimo." }),
            ApiResult<VoteResponse>.Ok(Vote(Ballots)));
        var map = Open();

        await Choose(map);

        map.Find("p.note[role=alert]").TextContent.ShouldBe("Nepavyko perskaityti balsavimo.");
        map.FindAll("svg.seatmap").ShouldBeEmpty();
        map.FindAll("#seimas-seats-loading").ShouldBeEmpty();

        await Choose(map);

        map.FindAll("p.note[role=alert]").ShouldBeEmpty("the message goes with the next try");
        map.FindAll("rect.seat").Count.ShouldBe(3);
        Api.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_lost_connection_is_said_with_the_network_message()
    {
        Api.OnCall<VoteResponse>("GetVoteAsync", _ => throw new HttpRequestException("down"));
        var map = Open();

        await Choose(map);

        map.Find("p.note[role=alert]").TextContent.ShouldBe(L["Error.Network"].Value);
        map.FindAll("svg.seatmap").ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_the_script_the_island_stays_quiet_and_reads_nothing()
    {
        JSInterop.SetupVoid("lt2Switch.watch", _ => true).SetException(new JSException("lt2Switch is not defined"));
        Api.On("GetVoteAsync", ApiResult<VoteResponse>.Ok(Vote(Ballots)));

        var map = Open();
        await Task.Yield();

        Api.Calls.ShouldBeEmpty();
        map.Markup.ShouldNotContain("seatmap");
        map.FindAll("p.note[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_script_is_told_to_stop_when_the_island_goes()
    {
        var map = Open();

        await map.Instance.DisposeAsync();

        JSInterop.Invocations["lt2Switch.unwatch"].Single().Arguments[0].ShouldBe("view-seimas");
    }

    [Fact]
    public void Before_the_browser_has_taken_over_the_server_render_asks_the_script_nothing()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        Open();

        JSInterop.Invocations.Where(i => i.Identifier == "lt2Switch.watch").ShouldBeEmpty();
    }
}
