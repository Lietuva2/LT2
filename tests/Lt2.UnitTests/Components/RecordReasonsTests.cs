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

        Arguments(Reason(First, 3), Reason(Second, 2));
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
        Arguments(Reason(Second, 4), Reason(First, 3));
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
        answer.ShouldContain("bet kurios pusės, tik ne savo parašytus");
        answer.ShouldContain("Skaičiuojamos tik žymos prie tos pusės argumentų, kurios prašote patys");
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
        }

        reasons.Find($"#report-{First}").GetAttribute("popovertargetaction").ShouldBe("hide");
        reasons.FindAll($"#report-{Second}").ShouldBeEmpty("owner, 2026-11-17: no \"Pranešti\" on one's own argument");
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

    /// <summary>
    /// Owner, 2026-11-17: a moderator in a staff-passkey session gets only the moderators' actions in "⋯", no "Pranešti"; before
    /// the island runs the served markup is the same as any member's.
    /// </summary>
    [Fact]
    public void A_moderator_in_a_staff_session_sees_only_the_moderators_actions_in_the_more_menu()
    {
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill).Add(c => c.Moderator, true));
        reasons.FindAll($"#moderate-{First}").ShouldNotBeEmpty();
        reasons.FindAll("[id^='report-']").ShouldBeEmpty();

        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        Render<RecordReasons>(p => p.Add(c => c.Number, Bill).Add(c => c.Moderator, true)).FindAll($"#report-{First}").ShouldNotBeEmpty("the served HTML is a member's");
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

    /// <summary>
    /// Owner, 2026-11-17: "when writing an argument with the argument list closed, it doesn't show up after posting". A published
    /// argument has no "Man svarbu" yet and the answer lists it last, beyond the two shown; it stands first in its side's column on
    /// top of the two already shown, marked and given the focus (which brings it into view), the list stays collapsed and its
    /// count says every argument.
    /// </summary>
    [Fact]
    public void A_published_argument_shows_first_in_its_side_and_the_list_stays_collapsed()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var third = Guid.Parse("0193a1b2-0000-7000-8000-00000000000c");
        var written = Guid.Parse("0193a1b2-0000-7000-8000-00000000000d");
        Arguments(Reason(First, 3), Reason(Second, 2), Reason(third, 1));
        var reasons = WriteAnArgument(new ReasonCreatedResponse(written, "published"),
            Reason(First, 3), Reason(Second, 2), Reason(third, 1), Reason(written, 0));

        reasons.WaitForAssertion(() => reasons.FindAll("#reasons-for .said").Select(s => s.Id)
            .ShouldBe([$"reason-{written}", $"reason-{First}", $"reason-{Second}"], "the new one first, the two shown stay"));
        reasons.Markup.ShouldContain("Argumentas paskelbtas.");
        var shown = reasons.Find($"#reason-{written}");
        shown.ClassList.ShouldContain("said--linked");
        shown.GetAttribute("tabindex").ShouldBe("-1");
        reasons.FindAll($"#reason-{third}").ShouldBeEmpty("the list stays collapsed");
        reasons.Find("#reasons-more-for-shown").TextContent.Trim().ShouldBe("Rodoma 3 iš 4", "owner, 2026-11-19: the side's own count");
        JSInterop.Invocations.ShouldContain(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal), "brought into view");
    }

    /// <summary>A waiting argument (pre-screened, design 8.3) is not public: the person is told so and nothing of it is shown.</summary>
    [Fact]
    public void A_waiting_argument_is_not_shown()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var waiting = Guid.Parse("0193a1b2-0000-7000-8000-00000000000e");
        var reasons = WriteAnArgument(new ReasonCreatedResponse(waiting, "pending"), Reason(First, 3), Reason(Second, 2));

        reasons.WaitForAssertion(() => reasons.Markup.ShouldContain("Argumentą pirma peržiūrės moderatorius."));
        reasons.FindAll($"#reason-{waiting}").ShouldBeEmpty();
        reasons.FindAll(".said--linked").ShouldBeEmpty();
        reasons.FindAll("#reasons-for .said").Select(s => s.Id).ShouldBe([$"reason-{First}", $"reason-{Second}"]);
    }

    /// <summary>
    /// Owner, 2026-11-17: "Redaguoti" in "⋯" of one's own argument only while the caller's own answer says it may be edited (nobody
    /// reacted), not shown at all otherwise; it opens the form below the row with the side and the text, and saving reads the
    /// arguments again in place.
    /// </summary>
    [Fact]
    public void Redaguoti_is_in_the_more_menu_of_ones_own_editable_argument_only_and_saves_in_place()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var third = Guid.Parse("0193a1b2-0000-7000-8000-00000000000f");
        Arguments(Reason(First, 3), Reason(Second, 0), Reason(third, 1));
        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(
            new MyBillRecordResponse("for", [], null, [Reason(Second, 0), Reason(third, 1)], null, true) { Editable = [Second] }));
        _appeals.On("GetRulesAsync", ApiResult<AppealRulesResponse>.Ok(new AppealRulesResponse(100, 100, 2000, 0, 0, 1500)));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        reasons.Find($"#more-reason-{Second}-menu #edit-{Second}").TextContent.ShouldBe("Redaguoti");
        reasons.FindAll($"#edit-{First}").ShouldBeEmpty("not one's own");
        reasons.Find("#reasons-more-for").Click();
        reasons.WaitForAssertion(() => reasons.FindAll($"#reason-{third}").ShouldNotBeEmpty("the next page, in place"));
        reasons.FindAll($"#edit-{third}").ShouldBeEmpty("someone reacted: not shown, not disabled");
        reasons.FindAll($"#withdraw-{third}").ShouldNotBeEmpty("withdrawing stays");

        reasons.Find($"#edit-{Second}").Click();
        var form = reasons.Find($"#said-actions-{Second} > form.act__panel");
        form.QuerySelector("h3")!.TextContent.ShouldBe("Redaguoti argumentą");
        reasons.Find($"#edit-side-for-{Second}").HasAttribute("checked").ShouldBeTrue();
        reasons.Find($"#edit-text-{Second}").GetAttribute("value").ShouldBe("Argumentas b");

        _appeals.On("EditReasonAsync", ApiResult<ReasonCreatedResponse>.Ok(new ReasonCreatedResponse(Second, "published")));
        reasons.Find($"#edit-side-against-{Second}").Change(true);
        reasons.Find($"#edit-text-{Second}").Input("Pataisytas argumentas.");
        reasons.Find($"#edit-reason-{Second}").Submit();
        reasons.WaitForAssertion(() => reasons.Markup.ShouldContain("Argumentas išsaugotas."));
        reasons.FindAll($"#edit-reason-{Second}").ShouldBeEmpty();
        var sent = _appeals.Arguments[_appeals.Calls.LastIndexOf("EditReasonAsync")];
        var request = (ReasonRequest)sent[1]!;
        ((Guid)sent[0]!, request.Side, request.Text).ShouldBe((Second, "against", "Pataisytas argumentas."));
        _appeals.Calls.Skip(_appeals.Calls.LastIndexOf("EditReasonAsync")).ShouldContain("GetRecordAsync", "read again in place");
    }

    /// <summary>
    /// Owner, 2026-11-17: a too short argument is asked about in place before it is published: "Papildyti" goes back to the text,
    /// "Vis tiek skelbti" publishes it (the server flags it and it waits for a moderator).
    /// </summary>
    [Fact]
    public void A_too_short_argument_is_asked_about_first_and_published_only_when_the_person_says_so()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _appeals.On("GetRulesAsync", ApiResult<AppealRulesResponse>.Ok(new AppealRulesResponse(100, 100, 2000, 0, 0, 1500) { MinReasonWords = 3 }));
        _appeals.On("FindSimilarAsync", ApiResult<IReadOnlyList<ReasonResponse>>.Ok([]));
        _appeals.On("WriteReasonAsync", ApiResult<ReasonCreatedResponse>.Ok(new ReasonCreatedResponse(Guid.NewGuid(), "pending")));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));
        reasons.Find("#add-reason").Click();
        reasons.Find("#reason-text").Input("Taip.");
        reasons.Find("#write-reason").Submit();

        reasons.Find("#too-short-text").TextContent.ShouldStartWith("Argumentas labai trumpas.");
        _appeals.Calls.ShouldNotContain("WriteReasonAsync");
        _appeals.Calls.ShouldNotContain("FindSimilarAsync");
        reasons.Find("#too-short-more").Click();
        reasons.FindAll("#too-short").ShouldBeEmpty("back to the text");

        reasons.Find("#write-reason").Submit();
        reasons.Find("#too-short-anyway").Click();
        reasons.WaitForAssertion(() => reasons.Markup.ShouldContain("Argumentą pirma peržiūrės moderatorius."));
        _appeals.Calls.ShouldContain("WriteReasonAsync");
    }

    /// <summary>
    /// Owner, 2026-11-17: the author sees their own hidden argument in its side's column, quiet, with the moderator's explanation
    /// and no actions, from their own answer once the island runs; the served HTML holds nothing of it.
    /// </summary>
    [Fact]
    public void The_author_sees_their_hidden_argument_greyed_with_the_explanation_only_once_the_island_runs()
    {
        var hidden = Guid.Parse("0193a1b2-0000-7000-8000-0000000000aa");
        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse(
            "for", [], null, [new ReasonResponse(hidden, "against", "Paslėptas tekstas.", "Rūta Mockutė", 0, "hidden", DateTimeOffset.UnixEpoch)], null, true)
        {
            HiddenStatements = new Dictionary<Guid, string> { [hidden] = "Įžeidžiantis tonas." },
        }));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        var item = reasons.Find($"#reasons-against #hidden-reason-{hidden}");
        item.ClassList.ShouldContain("said--hidden");
        item.TextContent.ShouldContain("Paslėptas tekstas.");
        reasons.Find($"#hidden-reason-why-{hidden}").TextContent.ShouldContain("Paslėpė moderatorius: Įžeidžiantis tonas.");
        item.QuerySelectorAll("button, .act").ShouldBeEmpty("no actions on a hidden argument");

        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        Render<RecordReasons>(p => p.Add(c => c.Number, Bill)).Markup.ShouldNotContain("Paslėptas tekstas.");
    }

    /// <summary>
    /// Owner, 2026-11-18: "Man svarbu" on either side, with or without a side of one's own; the person's own mark that does not
    /// count now stays pressed with a short note, which the button is described by: without a side it says when it will count,
    /// with the other side that it does not count.
    /// </summary>
    [Fact]
    public void Man_svarbu_works_on_either_side_and_an_uncounted_own_mark_carries_a_short_note()
    {
        var against = Reason(Second, 0) with { Side = "against" };
        Arguments(Reason(First, 3), against);
        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse(null, [First], null, [], null, true) { Uncounted = [First] }));
        var noSide = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        noSide.Find($"#mark-{First}").GetAttribute("aria-pressed").ShouldBe("true");
        noSide.Find($"#mark-{First}").HasAttribute("disabled").ShouldBeFalse();
        noSide.Find($"#mark-{Second}").HasAttribute("disabled").ShouldBeFalse("either side, without a side of one's own");
        noSide.Find($"#mark-note-{First}").TextContent.ShouldBe("Bus skaičiuojama, kai prašysite balsuoti už.");
        noSide.Find($"#mark-{First}").GetAttribute("aria-describedby").ShouldBe($"mark-note-{First}");
        noSide.FindAll($"#mark-note-{Second}").ShouldBeEmpty("not marked");
        noSide.FindAll("#reasons-note").ShouldBeEmpty("no side note: marking needs no side");

        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse("against", [First, Second], null, [], null, true) { Uncounted = [First] }));
        Services.GetRequiredService<MyBillState>().Forget(Bill);
        var against2 = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));
        against2.Find($"#mark-note-{First}").TextContent.ShouldBe("Neskaičiuojama: jūs prašote balsuoti prieš.");
        against2.FindAll($"#mark-note-{Second}").ShouldBeEmpty("a counted mark has no note");
        against2.Find($"#mark-{Second}").HasAttribute("aria-describedby").ShouldBeFalse();
    }

    /// <summary>
    /// Design 6.5, owner 2026-11-18: a first "Man svarbu" without the positions consent asks for it in place, under the
    /// argument's row, with the side buttons' form; nothing is saved before "Sutinku ir pažymiu „Man svarbu“".
    /// </summary>
    [Fact]
    public void A_first_mark_without_the_consent_asks_for_it_in_place_and_marks_after_it()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _appeals.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse(null, [], null, [], null, false)));
        _appeals.On("EndorseAsync", ApiResult.Ok());
        _me.On("GivePositionConsentAsync", ApiResult.Ok());
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        reasons.Find($"#mark-{First}").Click();
        _appeals.Calls.ShouldNotContain("EndorseAsync");
        reasons.Find($"#mark-consent-{First} #mark-{First}-consent-submit").TextContent.ShouldBe("Sutinku ir pažymiu „Man svarbu“");
        reasons.Find($"#mark-{First}-consent-check").Change(true);
        reasons.Find($"#mark-{First}-consent-submit").Click();

        reasons.WaitForAssertion(() => _appeals.Calls.ShouldContain("EndorseAsync"));
        _me.Calls.ShouldContain("GivePositionConsentAsync");
        _appeals.Arguments[_appeals.Calls.IndexOf("EndorseAsync")][0].ShouldBe(First);
        reasons.FindAll($"#mark-consent-{First}").ShouldBeEmpty("closed once given");
    }

    /// <summary>
    /// Owner, 2026-11-18: the line directly above every bubble is its author's name, in the writing box's similar arguments too;
    /// "Man svarbu" stays under the bubble, in the action row.
    /// </summary>
    [Fact]
    public void A_similar_arguments_name_stands_above_its_bubble_and_man_svarbu_below()
    {
        _appeals.On("GetRulesAsync", ApiResult<AppealRulesResponse>.Ok(new AppealRulesResponse(100, 100, 2000, 0, 0, 1500)));
        _appeals.On("FindSimilarAsync", ApiResult<IReadOnlyList<ReasonResponse>>.Ok([Reason(Second, 2)]));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));
        reasons.Find("#add-reason").Click();
        reasons.Find("#reason-text").Input("Mokesčiai turi būti paprastesni.");
        reasons.Find("#write-reason").Submit();

        var parts = reasons.Find($"#similar-{Second}").Children.Select(c => c.ClassName ?? "").ToList();
        parts[0].ShouldContain("said__by");
        reasons.Find($"#similar-{Second} > .said__by").TextContent.ShouldBe("Rūta Mockutė");
        parts[1].ShouldNotContain("said__by", Case.Sensitive, "the bubble right under the name");
        parts[2].ShouldBe("act");
        reasons.Find($"#similar-{Second} > .act #similar-mark-{Second}").TextContent.ShouldBe("Man svarbu · 2");
    }

    /// <summary>
    /// Owner, 2026-11-19: each side is read a page at a time. Under each column "Rodoma 2 iš 15" and "Rodyti daugiau argumentų", a
    /// link to the page with that side grown by ten (every other parameter kept), landing on the first one added; once the island
    /// runs it reads the next ten from the API in place, gives the first one added the focus, and the link then points further.
    /// </summary>
    [Fact]
    public void Rodyti_daugiau_argumentu_reads_the_sides_next_page_in_place_and_points_to_the_grown_address()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/seimas/{Bill}?atsakymai=20");
        var many = Enumerable.Range(1, 15).Select(i => Reason(Guid.Parse($"0193a1b2-0000-7000-8000-0000000003{i:D2}"), 20 - i)).ToArray();
        var against = Reason(Guid.Parse("0193a1b2-0000-7000-8000-000000000401"), 1) with { Side = "against" };
        Arguments([.. many, against]);
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));

        reasons.FindAll("#reasons-for .said").Count.ShouldBe(2);
        reasons.Find("#reasons-more-for-shown").TextContent.ShouldBe("Rodoma 2 iš 15");
        var more = reasons.Find("#reasons #reasons-for #reasons-more-for");
        more.TextContent.ShouldBe("Rodyti daugiau argumentų");
        more.GetAttribute("href").ShouldBe($"/seimas/{Bill}?atsakymai=20&argumentai-uz=12#argumentai-uz-3");
        reasons.FindAll("#reasons-more-against").ShouldBeEmpty("one argument against: nothing more to show");

        more.Click();

        reasons.WaitForAssertion(() => reasons.FindAll("#reasons-for .said").Count.ShouldBe(12));
        var asked = _appeals.Arguments[_appeals.Calls.LastIndexOf("GetReasonsAsync")];
        asked[1].ShouldBe("for");
        asked[2].ShouldBe("2", "from where the first page ends");
        asked[3].ShouldBe(ReasonLimits.Step);
        reasons.Find($"#reason-{many[2].Id}").GetAttribute("tabindex").ShouldBe("-1", "the first one added");
        reasons.Find($"#reason-{many[2].Id} > .said__by").Id.ShouldBe("argumentai-uz-3");
        JSInterop.Invocations.ShouldContain(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal));
        reasons.Find("#reasons-more-for-shown").TextContent.ShouldBe("Rodoma 12 iš 15");
        reasons.Find("#reasons-more-for").GetAttribute("href").ShouldBe($"/seimas/{Bill}?atsakymai=20&argumentai-uz=22#argumentai-uz-13");

        reasons.Find("#reasons-more-for").Click();
        reasons.WaitForAssertion(() => reasons.FindAll("#reasons-for .said").Count.ShouldBe(15));
        reasons.FindAll("#reasons-more-for").ShouldBeEmpty("every argument of the side is shown");
        reasons.Find("#reasons-more-for-shown").TextContent.ShouldBe("Rodoma 15 iš 15");
    }

    /// <summary>
    /// Owner, 2026-11-19: without script the address grows the side (<c>?argumentai-uz=12</c>), and the server renders as many,
    /// with their discussions; a linked argument its pages would put further on stands first in its side, marked.
    /// </summary>
    [Fact]
    public void The_address_grows_a_side_and_a_linked_argument_beyond_its_pages_stands_first_without_script()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var many = Enumerable.Range(1, 15).Select(i => Reason(Guid.Parse($"0193a1b2-0000-7000-8000-0000000003{i:D2}"), 20 - i)).ToArray();
        Arguments(many);
        var linked = many[14].Id;
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/seimas/{Bill}?argumentai-uz=12");
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill).Add(c => c.LinkedReason, linked));

        var shown = reasons.FindAll("#reasons-for .said").Select(s => s.Id).ToList();
        shown.Count.ShouldBe(13, "twelve as the address asks, and the linked one");
        shown[0].ShouldBe($"reason-{linked}");
        reasons.Find($"#reason-{linked}").ClassList.ShouldContain("said--linked");
        reasons.Find("#reasons-more-for-shown").TextContent.ShouldBe("Rodoma 13 iš 15");
        var more = reasons.Find("#reasons-more-for");
        more.HasAttribute("data-enhance-nav").ShouldBeFalse("a plain link without script");
        more.GetAttribute("href")!.ShouldContain("argumentai-uz=22");
        _appeals.Calls.ShouldContain("GetReasonAsync");
    }

    /// <summary>Opens the writing box, writes an argument for "už" and publishes it; the record is then read as <paramref name="after"/>.</summary>
    private IRenderedComponent<RecordReasons> WriteAnArgument(ReasonCreatedResponse created, params ReasonResponse[] after)
    {
        _appeals.On("GetRulesAsync", ApiResult<AppealRulesResponse>.Ok(new AppealRulesResponse(100, 100, 2000, 0, 0, 1500)));
        _appeals.On("FindSimilarAsync", ApiResult<IReadOnlyList<ReasonResponse>>.Ok([]));
        _appeals.On("WriteReasonAsync", ApiResult<ReasonCreatedResponse>.Ok(created));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));
        reasons.Find("#add-reason").Click();
        reasons.Find("#reason-text").Input("Mokesčiai turi būti paprastesni.");
        Arguments(after);
        reasons.Find("#write-reason").Submit();
        return reasons;
    }

    private static ReasonResponse Reason(Guid id, int marks) => new(id, "for", "Argumentas " + id.ToString()[^1], "Rūta Mockutė", marks, "published", DateTimeOffset.UnixEpoch);

    private static QueueReasonResponse Queued(Guid id) =>
        new(id, Guid.NewGuid(), new BillRefResponse(Bill, "Pataisa"), "for", "Argumentas", "Rūta Mockutė", "published", true, [], [], [], DateTimeOffset.UnixEpoch);

    private void Arguments(params ReasonResponse[] all) => ReasonFakes.Serve(_appeals, Bill, all);

}
