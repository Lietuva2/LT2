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

    private IRenderedComponent<CommentThread> RenderThread() =>
        Render<CommentThread>(p => p.Add(c => c.Kind, "reason").Add(c => c.TargetId, Target).Add(c => c.Number, Bill).Add(c => c.Thread, Thread())
            .Add(c => c.Count, 3).Add(c => c.PagePath, "/seimas/" + Bill));

    [Fact]
    public void The_server_renders_the_comments_oldest_first_under_their_names_with_anchors_links_and_who_agreed_and_no_controls_for_a_visitor()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var thread = RenderThread();

        thread.Find("details.more.comments summary").TextContent.ShouldBe("Aptarimas (3)");
        thread.FindAll("li.comment").Select(li => li.Id).ShouldBe([CommentLimits.ElementId(PersonComment), CommentLimits.ElementId(OrganisationComment), CommentLimits.ElementId(MpComment)]);
        var markup = thread.Markup;
        markup.ShouldContain("<a href=\"https://pavyzdys.test/tyrimas\" rel=\"nofollow ugc noopener noreferrer\" target=\"_blank\">https://pavyzdys.test/tyrimas</a>");
        markup.ShouldNotContain("href=\"http://kitas.pavyzdys.test\"");
        thread.Find($"#{CommentLimits.ElementId(PersonComment)}-agreed").TextContent.ShouldBe("Sutinka: Tomas, Lietuva 2.0 naudotojas ir dar 1");
        thread.Find($"#{CommentLimits.ElementId(OrganisationComment)} .tag").TextContent.ShouldBe("Organizacija");
        thread.Find($"#{CommentLimits.ElementId(MpComment)} a.comment__name").GetAttribute("href").ShouldBe("/p/jonas-petraitis");
        thread.Find($"#{CommentLimits.ElementId(MpComment)} .tag").TextContent.ShouldBe("Seimo narys arba jo biuras");
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
        name.GetAttribute("href").ShouldBe("/nariai/" + RutaProfile);
        var card = name.GetAttribute("data-card")!;
        thread.FindAll($"template#{card}").ShouldHaveSingleItem("one card per person in the thread");
        thread.Markup.ShouldContain("Mokytoja iš Kauno.", Case.Sensitive, "the card's text is in the page");
        thread.Find($"#{CommentLimits.ElementId(MpComment)}-agreed a").GetAttribute("data-card").ShouldBe(card, "the same card beside a \"Sutinku\"");
        thread.Find($"#{CommentLimits.ElementId(PersonComment)}-agreed").QuerySelectorAll("a").ShouldBeEmpty("Tomas and an unnamed member have no profile");
        thread.FindAll($"#{CommentLimits.ElementId(PersonComment)}-all-agreed a").ShouldBeEmpty();
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
}