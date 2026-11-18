using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Comments;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// What is new since the last visit on a followed bill (design 8.11.8): one write per page from the browser, "Nauja" beside what
/// the server named new, and "Rodyti tik naujus", which leaves only the new texts and those with new comments, those discussions
/// open, and shows everything again when switched off.
/// </summary>
public sealed class WhatsNewTests : BunitContext
{
    private const string Bill = "XVP-0412";
    private static readonly Guid First = Guid.Parse("0193a1b2-0000-7000-8000-00000000001a");
    private static readonly Guid Second = Guid.Parse("0193a1b2-0000-7000-8000-00000000001b");
    private static readonly Guid Third = Guid.Parse("0193a1b2-0000-7000-8000-00000000001c");
    private static readonly Guid NewComment = Guid.Parse("0193a1b2-0000-7000-8000-00000000002a");
    private static readonly DateTimeOffset LastVisit = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFake _myBills;
    private readonly ApiFake _comments;
    private readonly BunitAuthorizationContext _auth;

    public WhatsNewTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        MeFakes.Add(Services);
        (_comments, _) = CommentFakes.Add(Services);
        var appeals = DispatchProxy.Create<IAppealsApi, ApiFake>();
        var a = (ApiFake)(object)appeals;
        ReasonFakes.Serve(a, Bill, Reason(First), Reason(Second), Reason(Third));
        a.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse("for", [], null, [], null, true)));
        a.On("GetMyRestrictionAsync", ApiResult<MyRestrictionResponse>.Ok(new MyRestrictionResponse(false, null, null)));
        Services.AddSingleton(appeals);
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IProfilesApi, ApiFake>());
        var myBills = DispatchProxy.Create<IMyBillsApi, ApiFake>();
        _myBills = (ApiFake)(object)myBills;
        Services.AddSingleton(myBills);
        _auth = AddAuthorization();
        _auth.SetAuthorized("narys@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>The third argument is new; the second's discussion has a new comment; the first is neither.</summary>
    private void SeenAnswers() => _myBills.On("SeenAsync", ApiResult<BillSeenResponse>.Ok(
        new BillSeenResponse(true, LastVisit, [Third], [new NewCommentResponse(NewComment, "reason", Second)], [], [])));

    [Fact]
    public void Once_running_the_page_says_it_was_opened_once_and_marks_what_the_server_named_new()
    {
        SeenAnswers();
        var toggle = Render<WhatsNew>(p => p.Add(c => c.Number, Bill));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));
        reasons.Find("#reasons-more-for").Click();
        reasons.WaitForAssertion(() => reasons.FindAll($"#reason-{Third}").ShouldNotBeEmpty());

        _myBills.Calls.ShouldBe(["SeenAsync"]);
        _myBills.Arguments[0][0].ShouldBe(Bill);
        Services.GetRequiredService<MyBillState>().NewSince(Bill).ShouldBe(LastVisit);
        reasons.Find($"#reason-{Third} #new-{Third}").TextContent.ShouldBe("Nauja");
        reasons.FindAll($"#reason-{First} .tag--new").ShouldBeEmpty();
        reasons.FindAll($"#reason-{Second} .tag--new").ShouldBeEmpty("a new comment marks the comment, not its argument");
        toggle.Find("#only-new").GetAttribute("type").ShouldBe("checkbox");
        toggle.Find("label[for=only-new]").TextContent.ShouldBe("Rodyti tik naujus");
    }

    [Fact]
    public void Only_new_leaves_the_new_arguments_and_those_with_new_comments_with_that_discussion_open_and_off_shows_all_again()
    {
        SeenAnswers();
        var toggle = Render<WhatsNew>(p => p.Add(c => c.Number, Bill));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));
        reasons.FindAll("#reasons-for .said").Select(s => s.Id).ShouldBe([$"reason-{First}", $"reason-{Second}"], "two per side before \"Rodyti daugiau argumentų\"");

        toggle.Find("#only-new").Change(true);

        reasons.WaitForAssertion(() => reasons.FindAll("#reasons-for .said").Select(s => s.Id).ShouldBe([$"reason-{Second}", $"reason-{Third}"]));
        reasons.Find($"#aptarimas-{Second:N}").HasAttribute("open").ShouldBeTrue("its discussion holds a new comment");
        reasons.Find($"#aptarimas-{Third:N}").HasAttribute("open").ShouldBeFalse();
        _comments.Calls.ShouldContain("GetThreadAsync");

        toggle.Find("#only-new").Change(false);

        // Owner, 2026-11-19: the new argument beyond the first page was read for the switch, and stays at the end of its column.
        reasons.WaitForAssertion(() => reasons.FindAll("#reasons-for .said").Select(s => s.Id).ShouldBe([$"reason-{First}", $"reason-{Second}", $"reason-{Third}"]));
        reasons.Find("#reasons-more-for-shown").TextContent.ShouldBe("Rodoma 3 iš 3");
        reasons.Find($"#aptarimas-{Second:N}").HasAttribute("open").ShouldBeFalse();
    }

    [Fact]
    public void A_bill_not_followed_or_with_nothing_new_offers_no_switch_and_marks_nothing()
    {
        _myBills.On("SeenAsync", ApiResult<BillSeenResponse>.Ok(BillSeenResponse.NotFollowing));
        var toggle = Render<WhatsNew>(p => p.Add(c => c.Number, Bill));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        toggle.FindAll("#only-new").ShouldBeEmpty();
        reasons.FindAll(".tag--new").ShouldBeEmpty();

        _myBills.On("SeenAsync", ApiResult<BillSeenResponse>.Ok(new BillSeenResponse(true, LastVisit, [], [], [], [])));
        Render<WhatsNew>(p => p.Add(c => c.Number, Bill)).FindAll("#only-new").ShouldBeEmpty("nothing new: nothing to filter");
    }

    [Fact]
    public void While_the_server_prerenders_and_for_a_visitor_nothing_is_sent_and_nothing_rendered()
    {
        SeenAnswers();
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        Render<WhatsNew>(p => p.Add(c => c.Number, Bill)).Markup.Trim().ShouldBeEmpty();
        _myBills.Calls.ShouldBeEmpty();

        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _auth.SetNotAuthorized();
        Render<WhatsNew>(p => p.Add(c => c.Number, Bill)).Markup.Trim().ShouldBeEmpty();
        _myBills.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void A_new_comment_is_marked_in_its_discussion()
    {
        Services.GetRequiredService<MyBillState>().SetNews(Bill, new NewTexts(
            new BillSeenResponse(true, LastVisit, [], [new NewCommentResponse(NewComment, "reason", Second)], [], [])));
        var old = Guid.NewGuid();
        var thread = new CommentThreadResponse("reason", Second, Bill, 2, true, true,
        [
            new(old, new CommentAuthorResponse("person", "Rūta Mockutė", null, null, null), "Senas.", LastVisit.AddHours(-1), []),
            new(NewComment, new CommentAuthorResponse("person", "Tomas", null, null, null), "Naujas.", LastVisit.AddHours(1), []),
        ]);
        var rendered = Render<CommentThread>(p => p.Add(c => c.Kind, "reason").Add(c => c.TargetId, Second).Add(c => c.Number, Bill)
            .Add(c => c.Thread, thread).Add(c => c.Count, 2).Add(c => c.PagePath, "/seimas/" + Bill));

        rendered.Find($"#{CommentLimits.ElementId(NewComment)} .tag--new").TextContent.ShouldBe("Nauja");
        rendered.FindAll($"#{CommentLimits.ElementId(old)} .tag--new").ShouldBeEmpty();
    }

    [Fact]
    public void A_server_rendered_response_leaves_a_marker_only_while_only_new_is_on()
    {
        var state = Services.GetRequiredService<MyBillState>();
        var newResponse = Guid.NewGuid();
        var oldResponse = Guid.NewGuid();
        state.SetNews(Bill, new NewTexts(new BillSeenResponse(true, LastVisit, [], [], [], [newResponse])));
        var fresh = Render<NewMark>(p => p.Add(c => c.Number, Bill).Add(c => c.Id, newResponse).Add(c => c.MarksOut, true));
        var seen = Render<NewMark>(p => p.Add(c => c.Number, Bill).Add(c => c.Id, oldResponse).Add(c => c.MarksOut, true));
        fresh.Find(".tag--new").TextContent.ShouldBe("Nauja");
        seen.FindAll(".new-out, .new-keep").ShouldBeEmpty("nothing is filtered until the person asks");

        state.ShowOnlyNew(Bill, true);

        seen.WaitForAssertion(() => seen.FindAll(".new-out").Count.ShouldBe(1));
        fresh.FindAll(".new-keep").Count.ShouldBe(1);

        state.ShowOnlyNew(Bill, false);
        seen.WaitForAssertion(() => seen.FindAll(".new-out").ShouldBeEmpty());
    }

    private static ReasonResponse Reason(Guid id) => new(id, "for", "Argumentas " + id.ToString()[^1], "Rūta Mockutė", 0, "published", DateTimeOffset.UnixEpoch);

}
