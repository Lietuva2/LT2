using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// The arguments on a bill's page (design 8.2, 8.12.3). Every argument is keyed by its id and a mark moves nothing until the
/// page is read again, so a moderator's open action form stays with the argument it was opened on (UI review, finding 1).
/// </summary>
public sealed class RecordReasonsTests : BunitContext
{
    private const string Bill = "XVP-0412";
    private static readonly Guid First = Guid.Parse("0193a1b2-0000-7000-8000-00000000000a");
    private static readonly Guid Second = Guid.Parse("0193a1b2-0000-7000-8000-00000000000b");

    private readonly ApiFake _appeals;

    private readonly ApiFake _me;

    private readonly BunitAuthorizationContext _auth;

    public RecordReasonsTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        _me = MeFakes.Add(Services);
        CommentFakes.Add(Services);
        var appeals = DispatchProxy.Create<IAppealsApi, ApiFake>();
        _appeals = (ApiFake)(object)appeals;
        var moderation = DispatchProxy.Create<IModerationApi, ApiFake>();
        Services.AddSingleton(appeals);
        Services.AddSingleton(moderation);
        Services.AddSingleton(DispatchProxy.Create<Lt2.Api.Contracts.Me.IProfilesApi, ApiFake>());
        _auth = AddAuthorization();
        _auth.SetAuthorized("moderatorius@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        _appeals.On("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(BillRecord(Reason(First, 3), Reason(Second, 2))));
        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse("for", [], null, [], null, true)));
        _appeals.On("GetMyRestrictionAsync", ApiResult<MyRestrictionResponse>.Ok(new MyRestrictionResponse(false, null, null)));
        ((ApiFake)(object)moderation).On("GetBillAsync", ApiResult<ModerationBillResponse>.Ok(new ModerationBillResponse(
            Guid.NewGuid(), Bill, "open", "", null, 100, "default", false, null, false, [Queued(First), Queued(Second)], null, [])));
    }

    /// <summary>
    /// Owner, 2026-11-17: "the view is scrolled away after clicking 'Man svarbu'". The answer comes back ordered by the marks,
    /// but the arguments stay where they stood until the page is read again, the new number in place and the focus back on the
    /// pressed button; an open moderation form stays with its own argument.
    /// </summary>
    [Fact]
    public void A_mark_keeps_the_arguments_where_they_stand_and_the_focus_on_its_button_and_an_open_form_with_its_argument()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill).Add(c => c.Moderator, true));
        reasons.Find($"#moderate-{First}").Click();
        reasons.Find($"#action-hide-{First}").Click();
        reasons.Find($"#statement-{First}").Input("Įžeidimas.");

        // The second argument gets a mark; the server's answer now lists it first.
        _appeals.On("EndorseAsync", ApiResult.Ok());
        _appeals.On("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(BillRecord(Reason(Second, 4), Reason(First, 3))));
        reasons.Find($"#mark-{Second}").Click();

        reasons.WaitForAssertion(() => reasons.Find($"#mark-{Second}").TextContent.ShouldContain("4"));
        reasons.FindAll("#reasons-for .said").Select(s => s.Id).ShouldBe([$"reason-{First}", $"reason-{Second}"], "nothing moves under the person's hand");
        reasons.Find($"#reason-{First} #statement-{First}").GetAttribute("value").ShouldBe("Įžeidimas.");
        reasons.FindAll($"#reason-{Second} form").ShouldBeEmpty("the hide form was opened on the first argument");
        JSInterop.Invocations.ShouldContain(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal), "the pressed button gets the focus back");
    }

    /// <summary>
    /// Design 8.5.4 (owner, 2026-11-17): the MP's own account takes no part. Once the island runs it gets no "Man svarbu" (the
    /// number stays, as text) and no "Rašyti argumentą"; before that the HTML is the same as for any member.
    /// </summary>
    [Fact]
    public void The_mps_own_account_gets_neither_man_svarbu_nor_writing_an_argument_once_the_island_runs()
    {
        _me.On("GetAsync", ApiResult<Lt2.Api.Contracts.Me.MeResponse>.Ok(MeFakes.MpAccount));
        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse(null, [], null, [], null, false)));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        var mark = reasons.Find($"#mark-{First}");
        mark.TagName.ShouldBe("SPAN");
        mark.TextContent.ShouldContain("3");
        reasons.FindAll("button.mark").ShouldBeEmpty();
        reasons.FindAll("#add-reason").ShouldBeEmpty();
        reasons.FindAll("#reasons-note").ShouldBeEmpty();
        reasons.FindAll($"#report-{First}").ShouldNotBeEmpty("reporting stays");
        reasons.FindAll("#endorse-what").ShouldBeEmpty("no \"Man svarbu\", so nothing to explain");
    }

    /// <summary>
    /// Owner, 2026-11-17: one hint beside the arguments says when "Man svarbu" can be marked (a side first, one's own side, not
    /// one's own argument), with "Plačiau" to the methodology's part on arguments; the same served markup for everyone.
    /// </summary>
    [Fact]
    public void One_hint_beside_the_arguments_says_when_man_svarbu_can_be_marked_the_same_for_everyone_served()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var served = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        served.FindAll(".hint").Count.ShouldBe(1, "one hint beside the list, never one per argument");
        served.Find("#endorse-what").TextContent.ShouldBe("Kas yra „Man svarbu“?");
        var answer = served.Find("#endorse-what-panel .hint__answer").TextContent;
        answer.ShouldContain("nurodžius, ko prašote");
        answer.ShouldContain("tik savo pusės argumentus, ne savo parašytus");
        served.Find("#endorse-what-panel .hint__more").GetAttribute("href").ShouldBe("/metodika#argumentai");
        served.Find("#h-why + #reasons-endorse-what").ShouldNotBeNull("beside the heading");

        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        Render<RecordReasons>(p => p.Add(c => c.Number, Bill)).FindAll("#endorse-what").Count.ShouldBe(1, "a member keeps it once the island runs");
    }

    [Fact]
    public void An_office_member_takes_part_as_any_member()
    {
        _me.On("GetAsync", ApiResult<Lt2.Api.Contracts.Me.MeResponse>.Ok(MeFakes.Member with { Mp = MeFakes.MpAccount.Mp! with { Part = "office" } }));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        reasons.FindAll("#add-reason").ShouldNotBeEmpty("an office member writes in their own name");
        reasons.Find($"#mark-{First}").TagName.ShouldBe("BUTTON");
    }

    [Fact]
    public void Before_the_island_runs_the_mps_own_account_gets_the_same_html_as_any_member()
    {
        _me.On("GetAsync", ApiResult<Lt2.Api.Contracts.Me.MeResponse>.Ok(MeFakes.MpAccount));
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        reasons.Find($"#mark-{First}").TagName.ShouldBe("BUTTON");
        _me.Calls.ShouldBeEmpty("the account's own answer is asked only in the browser");
    }

    [Fact]
    public void A_reread_record_keeps_the_shown_order_and_puts_a_new_argument_last()
    {
        var third = Guid.NewGuid();
        var shown = BillRecord(Reason(First, 3), Reason(Second, 2));
        var fresh = BillRecord(Reason(third, 9), Reason(Second, 5), Reason(First, 3));
        RecordReasons.StableOrder(fresh, shown)!.Reasons.Select(r => r.Id).ShouldBe([First, Second, third]);
        RecordReasons.StableOrder(fresh, null).ShouldBe(fresh);
    }

    /// <summary>
    /// Design 8.16.1 (the designer's proposal of 2026-11-17): under every argument one action row, "Man svarbu · n",
    /// "Aptarimas" and "⋯"; "⋯" is the browser's popover with the rarer actions (the author's "Atsiimti" only on their own),
    /// each closing the menu, and a chosen one opens below the row; the items are disabled until the island runs.
    /// </summary>
    [Fact]
    public void Every_argument_has_one_action_row_and_the_rarer_actions_behind_its_more_menu()
    {
        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse("for", [], null, [Reason(Second, 2)], null, true)));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        foreach (var id in new[] { First, Second })
        {
            var row = reasons.Find($"#said-actions-{id}");
            row.Children.Select(e => e.ClassName?.Split(' ')[0]).Where(c => c is "mark" or "discuss" or "act__item").ShouldBe(["mark", "discuss", "act__item"], "Man svarbu, Aptarimas, then ⋯");
            var more = reasons.Find($"#more-reason-{id}");
            more.GetAttribute("popovertarget").ShouldBe($"more-reason-{id}-menu");
            more.GetAttribute("aria-label").ShouldBe("Daugiau veiksmų");
            reasons.Find($"#more-reason-{id}-menu").GetAttribute("popover").ShouldBe("auto");
            reasons.Find($"#report-{id}").GetAttribute("popovertargetaction").ShouldBe("hide");
        }

        reasons.FindAll($"#withdraw-{First}").ShouldBeEmpty("not the author");
        reasons.Find($"#more-reason-{Second}-menu #withdraw-{Second}").TextContent.ShouldBe("Atsiimti");
        reasons.FindAll(".act__panel").ShouldBeEmpty();

        reasons.Find($"#report-{First}").Click();
        reasons.Find($"#said-actions-{First} > .act__panel form").ShouldNotBeNull("the report form opens below the row");
    }

    [Fact]
    public void Before_the_island_runs_the_more_menu_opens_but_its_items_are_disabled()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        reasons.Find($"#more-reason-{First}").HasAttribute("disabled").ShouldBeFalse("the popover opens without script");
        reasons.Find($"#report-{First}").HasAttribute("disabled").ShouldBeTrue();
    }

    /// <summary>Owner, 2026-11-17: an argument's author cannot mark it "Man svarbu"; the island says why, from the caller's own answer.</summary>
    [Fact]
    public void The_authors_own_argument_offers_no_man_svarbu()
    {
        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse("for", [], null, [Reason(Second, 2)], null, true)));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        var own = reasons.Find($"#mark-{Second}");
        own.HasAttribute("disabled").ShouldBeTrue();
        own.GetAttribute("title").ShouldBe("Savo argumento „Man svarbu“ žymėti negalima.");
        reasons.Find($"#mark-{First}").HasAttribute("disabled").ShouldBeFalse("someone else's argument of one's own side");
    }

    /// <summary>
    /// Owner, 2026-11-17: the author's name stands above the argument's text, as above a statement, a response and a comment;
    /// a visitor's "Man svarbu" is a sign-in link back to the bill with the same look, not a grey label that looks like a button.
    /// </summary>
    [Fact]
    public void The_name_comes_first_and_a_visitors_man_svarbu_leads_to_sign_in_and_back_to_the_bill()
    {
        _auth.SetNotAuthorized();
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        var said = reasons.Find($"#reason-{First}");
        said.Children.Select(e => e.ClassName?.Split(' ')[0]).Take(2).ShouldBe(["said__by", "bubble"], "the name, then the text");
        var mark = reasons.Find($"#mark-{First}");
        mark.TagName.ShouldBe("A");
        mark.ClassList.ShouldContain("mark");
        mark.GetAttribute("href").ShouldBe("/prisijungti#tikslas=" + Uri.EscapeDataString("/seimas/" + Bill));
        mark.TextContent.Trim().ShouldBe("Man svarbu · 3");
    }

    [Fact]
    public void The_actions_beside_an_argument_and_its_moderation_controls_have_different_ids()
    {
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill).Add(c => c.Moderator, true));
        reasons.Find($"#moderate-{First}").Click();

        reasons.FindAll($"[id='actions-{First}']").Count.ShouldBe(1, "only the moderation controls use actions-{id}");
        reasons.Find($"#said-actions-{First}").ClassList.ShouldContain("act");
        reasons.Find($"#moderate-{First}").ClassList.ShouldContain("menu__item");
        reasons.Find($"#moderation-{First}").ClassList.ShouldContain("act__panel");
    }

    private static ReasonResponse Reason(Guid id, int marks) => new(id, "for", "Argumentas " + id.ToString()[^1], "Rūta Mockutė", marks, "published", DateTimeOffset.UnixEpoch);

    private static QueueReasonResponse Queued(Guid id) =>
        new(id, Guid.NewGuid(), new BillRefResponse(Bill, "Pataisa"), "for", "Argumentas", "Rūta Mockutė", "published", true, [], [], [], DateTimeOffset.UnixEpoch);

    private static BillRecordResponse BillRecord(params ReasonResponse[] reasons) => new(
        Guid.NewGuid(), new BillRefResponse(Bill, "Gyventojų pajamų mokesčio įstatymo pataisa"), "open", "", null, null,
        new SideCountsResponse(1, 1, 0, 0), new ThresholdResponse(100, "default", null, 1, false, false), reasons);
}
