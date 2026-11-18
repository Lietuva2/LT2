using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Api.Contracts.Mps;
using Lt2.Api.Contracts.Organisations;
using Lt2.Web.Client;
using Lt2.Web.Client.Comments;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// "Aptarimas" (design 8.23.6) as the server renders it and as the island runs: the comments oldest first under their names,
/// links as links, who agreed, an anchor per comment; the caller's own controls only once the island runs.
/// </summary>
public sealed class CommentThreadTests : BunitContext
{
    private const string Bill = "XVP-0412";
    private static readonly Guid Target = Guid.Parse("0193a1b2-0000-7000-8000-0000000000a1");
    private static readonly Guid PersonComment = Guid.Parse("0193a1b2-0000-7000-8000-0000000000b1");
    private static readonly Guid OrganisationComment = Guid.Parse("0193a1b2-0000-7000-8000-0000000000b2");
    private static readonly Guid MpComment = Guid.Parse("0193a1b2-0000-7000-8000-0000000000b3");

    private readonly ApiFake _comments;
    private readonly ApiFake _moderation;

    public CommentThreadTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        (_comments, _moderation) = CommentFakes.Add(Services);
        var appeals = DispatchProxy.Create<IAppealsApi, ApiFake>();
        ((ApiFake)(object)appeals).On("GetRulesAsync", ApiResult<AppealRulesResponse>.Ok(new AppealRulesResponse(100, 100, 2000, 0, 0, 1500)));
        ((ApiFake)(object)appeals).On("GetMyRestrictionAsync", ApiResult<MyRestrictionResponse>.Ok(new MyRestrictionResponse(false, null, null)));
        Services.AddSingleton(appeals);
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private const string RutaProfile = "rutaprof01";

    private static CommentThreadResponse Thread() => new("reason", Target, Bill, 3, true, true,
    [
        new(PersonComment, new CommentAuthorResponse("person", "Rūta Mockutė", null, null, null, RutaProfile), "Šaltinis: https://pavyzdys.test/tyrimas ir http://kitas.pavyzdys.test", DateTimeOffset.UnixEpoch,
            [new("Tomas"), new(null), new("Ona Jonaitienė")]),
        new(OrganisationComment, new CommentAuthorResponse("organisation", "Pavyzdinė asociacija", "pavyzdine-asociacija", new OrganisationLabelResponse(null, null), null), "Asociacijos pastaba.", DateTimeOffset.UnixEpoch.AddHours(1), []),
        new(MpComment, new CommentAuthorResponse("mp", null, null, null, new MpResponderResponse("jonas-petraitis", "Jonas", "Petraitis", null)), "Seimo nario pastaba.", DateTimeOffset.UnixEpoch.AddHours(2), [new("Rūta Mockutė", RutaProfile)]),
    ], [new AuthorCardResponse(RutaProfile, "Rūta Mockutė", "Mokytoja iš Kauno.")]);

    /// <summary>
    /// Owner, 2026-11-17: "Redaguoti" in "⋯" of one's own comment only while the caller's own answer says it may be edited (nobody
    /// agreed), not shown at all otherwise; it opens the form below the row with the text, and saving reads the thread again in place.
    /// </summary>
    [Fact]
    public void Redaguoti_is_in_the_more_menu_of_ones_own_editable_comment_only_and_saves_in_place()
    {
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _comments.On("GetMineAsync", ApiResult<MyCommentsResponse>.Ok(new MyCommentsResponse(
            true, false, false, [PersonComment, OrganisationComment], [PersonComment, OrganisationComment], [], [], 0, new Dictionary<Guid, int>()) { Editable = [PersonComment] }));
        var thread = RenderThread();

        var person = CommentLimits.ElementId(PersonComment);
        thread.Find($"#{person}-more-actions-menu #{person}-edit").TextContent.ShouldBe("Redaguoti");
        thread.FindAll($"#{CommentLimits.ElementId(OrganisationComment)}-edit").ShouldBeEmpty("someone agreed with it: not shown, not disabled");
        thread.FindAll($"#{CommentLimits.ElementId(MpComment)}-edit").ShouldBeEmpty("not one's own");

        thread.Find($"#{person}-edit").Click();
        var form = thread.Find($"#{person}-actions > form.act__panel");
        form.QuerySelector("h4")!.TextContent.ShouldBe("Redaguoti komentarą");
        thread.Find($"#{person}-edit-text").GetAttribute("value").ShouldStartWith("Šaltinis:");
        thread.Find($"#{person}-edit-save").TextContent.ShouldBe("Išsaugoti");

        _comments.On("EditAsync", ApiResult.Ok());
        thread.Find($"#{person}-edit-text").Input("Pataisytas komentaras.");
        thread.Find($"#{person}-edit-form").Submit();
        thread.WaitForAssertion(() => thread.Markup.ShouldContain("Komentaras išsaugotas."));
        thread.FindAll($"#{person}-edit-form").ShouldBeEmpty();
        var sent = _comments.Arguments[_comments.Calls.LastIndexOf("EditAsync")];
        ((Guid)sent[0]!, ((CommentRequest)sent[1]!).Text).ShouldBe((PersonComment, "Pataisytas komentaras."));
        _comments.Calls.Skip(_comments.Calls.LastIndexOf("EditAsync")).ShouldContain("GetThreadAsync", "the thread is read again in place");
    }

    /// <summary>
    /// Owner, 2026-11-17: no "Pranešti" on one's own comment (in any voice, from the caller's own answer), and a moderator in a
    /// staff-passkey session gets only the moderators' actions.
    /// </summary>
    [Fact]
    public void Pranesti_is_not_on_ones_own_comment_nor_for_a_moderator_in_a_staff_session()
    {
        AddAuthorization().SetAuthorized("atstovas@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _comments.On("GetMineAsync", ApiResult<MyCommentsResponse>.Ok(new MyCommentsResponse(
            true, false, false, [], [], [], [], 0, new Dictionary<Guid, int>()) { Own = [OrganisationComment] }));
        var thread = RenderThread();
        thread.FindAll($"#{CommentLimits.ElementId(OrganisationComment)}-report").ShouldBeEmpty("the organisation's own comment");
        thread.FindAll($"#{CommentLimits.ElementId(PersonComment)}-report").ShouldNotBeEmpty();

        _moderation.On("GetOnBillAsync", ApiResult<IReadOnlyList<QueueCommentResponse>>.Ok([]));
        var moderated = Render<CommentThread>(p => p.Add(c => c.Kind, "reason").Add(c => c.TargetId, Target).Add(c => c.Number, Bill).Add(c => c.Thread, Thread())
            .Add(c => c.Count, 3).Add(c => c.PagePath, "/seimas/" + Bill).Add(c => c.Moderator, true));
        moderated.FindAll("[id$='-report']").ShouldBeEmpty();
    }

    /// <summary>Owner, 2026-11-17: the author sees their own hidden comment under its text, quiet, with the moderator's explanation.</summary>
    [Fact]
    public void The_author_sees_their_hidden_comment_greyed_with_the_explanation()
    {
        var hidden = Guid.Parse("0193a1b2-0000-7000-8000-0000000000bb");
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _comments.On("GetMineAsync", ApiResult<MyCommentsResponse>.Ok(new MyCommentsResponse(
            true, false, false, [], [], [], [], 0, new Dictionary<Guid, int>())
        {
            Hidden = [new MyHiddenCommentResponse(hidden, "reason", Target, "Paslėptas komentaras.", DateTimeOffset.UnixEpoch, "Asmens duomenys.")],
        }));
        var thread = RenderThread();

        var item = thread.Find($"#comments-hidden-{Target:N} > li");
        item.ClassList.ShouldContain("comment--hidden");
        item.TextContent.ShouldContain("Paslėptas komentaras.");
        item.TextContent.ShouldContain("Paslėpė moderatorius: Asmens duomenys.");
        item.QuerySelectorAll("button").ShouldBeEmpty();
        item.Children[0].ClassList.ShouldContain("bubble--nameless", "no name above one's own hidden comment, so no tail (design 8.16.3)");
    }

    private IRenderedComponent<CommentThread> RenderThread() =>
        Render<CommentThread>(p => p.Add(c => c.Kind, "reason").Add(c => c.TargetId, Target).Add(c => c.Number, Bill).Add(c => c.Thread, Thread())
            .Add(c => c.Count, 3).Add(c => c.PagePath, "/seimas/" + Bill));

    [Fact]
    public void The_server_renders_the_comments_oldest_first_under_their_names_with_anchors_links_and_who_agreed_and_no_controls_for_a_visitor()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var thread = RenderThread();

        // Design 8.16.1: the <details> holds only its summary, which stands in the action row; the discussion is the panel after it.
        thread.Find("details.discuss > summary").TextContent.ShouldBe("Aptarimas (3)");
        thread.Find("details.discuss").ChildElementCount.ShouldBe(1);
        thread.Find("details.discuss + .discuss__panel").Id.ShouldBe($"aptarimas-panel-{Target:N}");
        thread.FindAll("li.comment .act .act__more[popovertarget]").Count.ShouldBe(3, "every comment's own row ends in \"⋯\"");
        thread.FindAll("li.comment").Select(li => li.Id).ShouldBe([CommentLimits.ElementId(PersonComment), CommentLimits.ElementId(OrganisationComment), CommentLimits.ElementId(MpComment)]);
        var markup = thread.Markup;
        markup.ShouldContain("<a href=\"https://pavyzdys.test/tyrimas\" rel=\"nofollow ugc noopener noreferrer\" target=\"_blank\">https://pavyzdys.test/tyrimas</a>");
        markup.ShouldNotContain("href=\"http://kitas.pavyzdys.test\"");
        // Owner, 2026-11-17: "Sutinka: A, B ir dar 1", where "ir dar 1" opens the full list (no "Visi sutinkantys" of its own).
        var agreed = thread.Find($"#{CommentLimits.ElementId(PersonComment)}-agreed");
        agreed.Children[0].TextContent.ShouldBe("Sutinka: Tomas, Lietuva 2.0 naudotojas");
        thread.Find($"#{CommentLimits.ElementId(PersonComment)}-all-agreed > summary").TextContent.ShouldBe("ir dar 1");
        thread.Find($"#{CommentLimits.ElementId(PersonComment)}-all-agreed").ChildElementCount.ShouldBe(1, "the <details> holds only its summary");
        thread.Find($"#{CommentLimits.ElementId(PersonComment)}-all-agreed + .agreed__all").TextContent.Split(", ").Length.ShouldBe(3, "the full list, all three");
        markup.ShouldNotContain("Visi sutinkantys");
        thread.Find($"#{CommentLimits.ElementId(OrganisationComment)} .tag").TextContent.ShouldBe("Organizacija");
        thread.Find($"#{CommentLimits.ElementId(OrganisationComment)} .comment__label").TextContent.ShouldBe("Nepatvirtinta", "the organisation's label with its mark (design 8.23.2)");
        thread.Find($"#{CommentLimits.ElementId(MpComment)} a.comment__name").GetAttribute("href").ShouldBe("/p/jonas-petraitis");
        thread.Find($"#{CommentLimits.ElementId(MpComment)} .tag").TextContent.ShouldBe("Seimo narys arba jo biuras");
        // Design 8.16.3 (owner, 2026-11-18): the name line right above the bubble, where its tail points; the voice's mark and the
        // time under the bubble.
        foreach (var li in thread.FindAll("li.comment"))
        {
            var parts = li.Children.Take(3).ToList();
            parts[0].ClassList.ShouldContain("comment__who");
            parts[0].QuerySelectorAll(".tag, .comment__label, .num").ShouldBeEmpty("only the name (and \"Nauja\") on the line the tail points at");
            parts[1].ClassList.ShouldContain("bubble");
            parts[2].ClassList.ShouldContain("comment__meta");
        }
        thread.Find($"#{CommentLimits.ElementId(MpComment)} .comment__meta .tag").TextContent.ShouldBe("Seimo narys arba jo biuras");
        thread.FindAll("button[id$='-agree'], button[id$='-delete']").ShouldBeEmpty("no member's controls for a visitor");
        thread.Find($"#comments-sign-in-{Target:N} a").GetAttribute("href").ShouldStartWith("/prisijungti#tikslas=");
        _comments.Calls.ShouldBeEmpty("nothing of the caller's is read while the server renders");
    }

    /// <summary>
    /// Design 8.23.2, 8.23.10 with 8.25.4: a person with a public profile is a link to it with its card, beside their comment and
    /// among those who agreed; anyone else a plain name; an organisation's or an MP's comment never leads to a person.
    /// </summary>
    [Fact]
    public void A_name_with_a_public_profile_beside_a_comment_and_in_sutinka_links_to_the_profile_with_its_card_and_others_stay_plain()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var thread = RenderThread();

        var name = thread.Find($"#{CommentLimits.ElementId(PersonComment)} a.comment__name");
        name.GetAttribute("href").ShouldBe("/nariai/ruta-mockute-" + RutaProfile);
        var card = name.GetAttribute("data-card")!;
        thread.FindAll($"template#{card}").ShouldHaveSingleItem("one card per person in the thread");
        thread.Markup.ShouldContain("Mokytoja iš Kauno.", Case.Sensitive, "the card's text is in the page");
        thread.Find($"#{CommentLimits.ElementId(MpComment)}-agreed a").GetAttribute("data-card").ShouldBe(card, "the same card beside a \"Sutinku\"");
        thread.Find($"#{CommentLimits.ElementId(PersonComment)}-agreed").QuerySelectorAll("a").ShouldBeEmpty("Tomas and an unnamed member have no profile");
        thread.FindAll($"#{CommentLimits.ElementId(PersonComment)}-all-agreed-list a").ShouldBeEmpty();
        thread.Find($"#{CommentLimits.ElementId(OrganisationComment)} .comment__name").GetAttribute("href").ShouldBe("/o/pavyzdine-asociacija");
        thread.FindAll("a[href^=\"/nariai/\"]").Count.ShouldBe(2, "the person's own comment and their \"Sutinku\"");
    }

    [Fact]
    public void The_first_sutinku_asks_that_the_name_will_be_visible_and_ones_own_comment_has_none()
    {
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _comments.On("GetMineAsync", ApiResult<MyCommentsResponse>.Ok(new(false, false, false, [PersonComment], [PersonComment], [], [], 0, new Dictionary<Guid, int>())));
        var thread = RenderThread();

        thread.FindAll($"#{CommentLimits.ElementId(PersonComment)}-agree").ShouldBeEmpty("not on one's own comment");
        thread.Find($"#{CommentLimits.ElementId(PersonComment)}-delete").TextContent.ShouldBe("Ištrinti");
        thread.Find($"#{CommentLimits.ElementId(OrganisationComment)}-agree").Click();
        thread.Find($"#{CommentLimits.ElementId(OrganisationComment)}-confirm").TextContent.ShouldContain("Jūsų vardas bus matomas prie šio komentaro.");
        _comments.Calls.ShouldNotContain("AgreeAsync", "nothing is sent before the confirmation");
    }

    /// <summary>
    /// Owner, 2026-11-17: "Sutinku" is a toggle like "Man svarbu": the same words in both states, its own icon (filled by the
    /// stylesheet while pressed), the state in aria-pressed; no "Atsiimti „Sutinku“".
    /// </summary>
    [Fact]
    public void Sutinku_keeps_its_words_when_agreed_and_says_the_state_by_aria_pressed()
    {
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _comments.On("GetMineAsync", ApiResult<MyCommentsResponse>.Ok(new(true, false, false, [], [], [OrganisationComment], [], 0, new Dictionary<Guid, int>())));
        var thread = RenderThread();

        var agreed = thread.Find($"#{CommentLimits.ElementId(OrganisationComment)}-agree");
        var other = thread.Find($"#{CommentLimits.ElementId(MpComment)}-agree");
        (agreed.TextContent.Trim(), agreed.GetAttribute("aria-pressed")).ShouldBe(("Sutinku", "true"));
        (other.TextContent.Trim(), other.GetAttribute("aria-pressed")).ShouldBe(("Sutinku", "false"));
        agreed.QuerySelector("svg.icon .icon__fill").ShouldNotBeNull();
        thread.Markup.ShouldNotContain("Atsiimti „Sutinku“");
    }

    [Fact]
    public void The_writing_box_says_in_whose_name_the_comment_will_be_public()
    {
        AddAuthorization().SetAuthorized("atstovas@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _comments.On("GetMineAsync", ApiResult<MyCommentsResponse>.Ok(new(true, false, false, [], [], [], [new CommentTargetVoiceResponse("reason", Target, "organisation", null)], 0, new Dictionary<Guid, int>())));
        var thread = RenderThread();

        thread.Find($"#comments-write-{Target:N}").Click();
        thread.Find($"#comment-voice-{Target:N}").TextContent.ShouldContain("organizacijos vardu – ne jūsų");
        thread.Find($"#comment-text-{Target:N}").GetAttribute("maxlength").ShouldBe("2000");
    }

    [Fact]
    public void The_moderators_list_shows_a_reported_comment_with_its_target_and_reports_and_never_the_reporter()
    {
        AddAuthorization().SetAuthorized("moderatorius@pavyzdys.test");
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var item = new QueueCommentResponse(
            PersonComment, new BillRefResponse(Bill, "Pataisa"), new CommentAuthorResponse("person", "Rūta Mockutė", null, null, null), "Komentaras.", DateTimeOffset.UnixEpoch, "published",
            [new QueueReportResponse(Guid.NewGuid(), "insult", "Įžeidžia.", DateTimeOffset.UnixEpoch)],
            new CommentContextResponse("reason", Target, "Argumentas.", "Tomas", null, null, true), null, null, null);
        var hidden = item with { Id = OrganisationComment, Status = "hidden", Reports = [], HiddenAt = DateTimeOffset.UnixEpoch, HiddenBy = "Moderatorius M.", HideStatement = "Įžeidžia." };
        _moderation.On("GetAsync", ApiResult<CommentModerationResponse>.Ok(new CommentModerationResponse([item], [hidden])));

        var list = Render<CommentModerationView>();
        list.Find("#queue-comments").TextContent.ShouldContain("Komentaras.");
        list.Find("#queue-comments").TextContent.ShouldContain("Įžeidimas");
        var hiddenList = Render<CommentModerationView>(p => p.Add(c => c.Hidden, true));
        hiddenList.Find("#hidden-comments").TextContent.ShouldContain("Moderatorius M.");
    }

    /// <summary>
    /// Design 8.23.14: "Rodyti daugiau komentarų" lands on the first new comment, which the page gives the position anchor, and
    /// keeps the page's other parameters.
    /// </summary>
    [Fact]
    public void Rodyti_daugiau_komentaru_lands_on_the_first_new_comment_by_its_position()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var items = Enumerable.Range(1, 10).Select(i => new CommentResponse(
            Guid.Parse($"0193a1b2-0000-7000-8000-0000000001{i:D2}"), new CommentAuthorResponse("person", "Narys " + i, null, null, null), "Komentaras " + i, DateTimeOffset.UnixEpoch.AddMinutes(i), [])).ToList();
        var thread = Render<CommentThread>(p => p.Add(c => c.Kind, "reason").Add(c => c.TargetId, Target).Add(c => c.Number, Bill)
            .Add(c => c.Thread, new CommentThreadResponse("reason", Target, Bill, 12, true, true, items)).Add(c => c.Count, 12)
            .Add(c => c.PagePath, "/seimas/" + Bill + "?atsakymai=20"));
        thread.Find($"#{CommentPaging.PositionAnchor(Target, 10)}").ShouldNotBeNull("each comment carries its position");
        thread.Find($"#comments-more-{Target:N}").GetAttribute("href")
            .ShouldBe($"/seimas/{Bill}?atsakymai=20&komentarai={Target:N}-12#{CommentPaging.PositionAnchor(Target, 11)}");
        thread.Find($"#comments-shown-{Target:N}").TextContent.ShouldBe("Rodoma 10 iš 12");
        thread.Find($"#comments-more-{Target:N}").HasAttribute("data-enhance-nav").ShouldBeFalse("before the island runs, the plain link");
    }

    /// <summary>
    /// Owner, 2026-11-19 ("doesn't work on phone nor desktop"): once the island runs, "Rodyti daugiau komentarų" reads the next ten
    /// from the API, from where the shown ones end, adds them in place and gives the first one added the focus; the link is taken
    /// from enhanced navigation (which fetched the page while the island kept its own list) and points to the grown address.
    /// </summary>
    [Fact]
    public void Once_the_island_runs_rodyti_daugiau_komentaru_reads_the_next_page_in_place_and_focuses_the_first_added()
    {
        AddAuthorization().SetNotAuthorized();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        CommentResponse Comment(int i) => new(
            Guid.Parse($"0193a1b2-0000-7000-8000-0000000001{i:D2}"), new CommentAuthorResponse("person", "Narys " + i, null, null, null), "Komentaras " + i, DateTimeOffset.UnixEpoch.AddMinutes(i), []);
        var first = Enumerable.Range(1, 10).Select(Comment).ToList();
        var thread = Render<CommentThread>(p => p.Add(c => c.Kind, "reason").Add(c => c.TargetId, Target).Add(c => c.Number, Bill)
            .Add(c => c.Thread, new CommentThreadResponse("reason", Target, Bill, 23, true, true, first, null, "10")).Add(c => c.Count, 23)
            .Add(c => c.PagePath, "/seimas/" + Bill));
        thread.Find($"#comments-more-{Target:N}").GetAttribute("data-enhance-nav").ShouldBe("false");

        _comments.On("GetThreadAsync", ApiResult<CommentThreadResponse>.Ok(
            new CommentThreadResponse("reason", Target, Bill, 23, true, true, Enumerable.Range(10, 11).Select(Comment).ToList(), null, "20")));
        thread.Find($"#comments-more-{Target:N}").Click();

        thread.WaitForAssertion(() => thread.FindAll($"#comments-{Target:N} > li").Count.ShouldBe(20, "the tenth came again and is not added twice"));
        var asked = _comments.Arguments[_comments.Calls.LastIndexOf("GetThreadAsync")];
        asked[1].ShouldBe(Target);
        asked[2].ShouldBe(CommentLimits.Step);
        asked[3].ShouldBe("10", "from where the shown ones end");
        thread.Find($"#{CommentLimits.ElementId(Comment(11).Id)}").GetAttribute("tabindex").ShouldBe("-1");
        JSInterop.Invocations.ShouldContain(i => i.Identifier.EndsWith("focus", StringComparison.Ordinal), "the first one added takes the focus");
        thread.Find($"#comments-shown-{Target:N}").TextContent.ShouldBe("Rodoma 20 iš 23");
        thread.Find($"#comments-more-{Target:N}").GetAttribute("href").ShouldBe($"/seimas/{Bill}?komentarai={Target:N}-23#{CommentPaging.PositionAnchor(Target, 21)}");
    }

    /// <summary>Design 8.23.7: under a withdrawn response the discussion is read-only: no "Sutinku", no deleting, for anyone.</summary>
    [Fact]
    public void A_read_only_discussion_offers_no_sutinku_and_no_deleting_even_to_the_author()
    {
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        _comments.On("GetMineAsync", ApiResult<MyCommentsResponse>.Ok(new(true, false, false, [PersonComment], [PersonComment], [], [], 0, new Dictionary<Guid, int>())));
        var readOnly = Thread() with { Open = false, BillOpen = false };
        var thread = Render<CommentThread>(p => p.Add(c => c.Kind, "response").Add(c => c.TargetId, Target).Add(c => c.Number, Bill).Add(c => c.Thread, readOnly)
            .Add(c => c.Count, 3).Add(c => c.PagePath, "/seimas/" + Bill));
        thread.FindAll("li.comment").Count.ShouldBe(3, "the discussion reads");
        thread.FindAll("button[id$='-agree'], button[id$='-delete']").ShouldBeEmpty();
    }
}