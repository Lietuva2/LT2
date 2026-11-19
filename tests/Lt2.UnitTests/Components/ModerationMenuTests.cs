using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Comments;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Comments;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.3, "The screen" (owner, 2026-11-17, replacing one control per action): one filled button for what the item needs
/// most in its state, and beside it "Kiti veiksmai" ("Veiksmai" when nothing stands out) opening the rest in the browser's own
/// popover, a bill's under "Aprašymas", "Seime" and "Suvestinės", "Paslėpti" set apart at the end. The trigger works before the
/// island runs; the actions do not. Choosing one closes the menu and moves the focus into its form.
/// </summary>
public sealed class ModerationMenuTests : BunitContext
{
    private const string Bill = "XVP-0412";
    private static readonly Guid Item = Guid.Parse("0193a1b2-0000-7000-8000-000000000412");

    public ModerationMenuTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var moderation = DispatchProxy.Create<IModerationApi, ApiFake>();
        ((ApiFake)(object)moderation).On("GetAiAsync", ApiResult<AiStatusResponse>.Ok(new AiStatusResponse(false)));
        Services.AddSingleton(moderation);
        Services.AddSingleton(DispatchProxy.Create<ICommentModerationApi, ApiFake>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    private IRenderedComponent<ModerationActions> Record(bool openItems = false, string? source = null, string description = "Projektas keičia rinkliavas.") =>
        Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Number, Bill).Add(c => c.Kind, "record").Add(c => c.Status, "open").Add(c => c.Threshold, 100)
            .Add(c => c.HasOpenItems, openItems).Add(c => c.Description, description).Add(c => c.DescriptionSource, source));

    private IRenderedComponent<ModerationActions> Argument(string status, bool seen = true, bool reported = false, int candidates = 0) =>
        Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Kind, "reason").Add(c => c.Status, status).Add(c => c.Seen, seen).Add(c => c.Reported, reported)
            .Add(c => c.MergeCandidates, Enumerable.Range(0, candidates)
                .Select(i => new ReasonResponse(Guid.NewGuid(), "for", "Panašus argumentas " + i, "Rūta Mockutė", i, "published", DateTimeOffset.UnixEpoch)).ToList()));

    private IRenderedComponent<CommentModerationActions> Comment(string status, bool reported) =>
        Render<CommentModerationActions>(p => p.Add(c => c.Id, Item).Add(c => c.Status, status).Add(c => c.Reported, reported));

    /// <summary>The filled button's action, or null when there is none.</summary>
    private static string? Primary<T>(IRenderedComponent<T> menu) where T : IComponent =>
        menu.FindAll(".actions > button.btn:not(.menu__trigger)").SingleOrDefault()?.GetAttribute("data-action");

    private static IElement Trigger<T>(IRenderedComponent<T> menu) where T : IComponent => menu.Find(".actions > button.menu__trigger");

    /// <summary>The menu's items in order, each as "heading: action" (no heading: the action alone), a rule as "—".</summary>
    private static string[] MenuItems<T>(IRenderedComponent<T> menu) where T : IComponent =>
        [.. menu.FindAll(".menu button.menu__item, .menu hr.menu__rule").Select(e => e.TagName == "HR"
            ? "—"
            : (e.Closest(".menu__group")?.QuerySelector(".menu__heading")?.TextContent is { } heading ? heading + ": " : "") + e.GetAttribute("data-action"))];

    [Fact]
    public void A_bill_with_an_AI_summary_nobody_has_read_offers_confirming_first_and_the_rest_under_their_kinds()
    {
        var menu = Record(source: "ai");
        Primary(menu).ShouldBe("confirm");
        menu.Find($"#action-confirm-{Item}").TextContent.ShouldBe("Patvirtinti aprašymą");
        Trigger(menu).TextContent.Trim().ShouldBe("Kiti veiksmai");
        MenuItems(menu).ShouldBe([
            "Aprašymas: describe", "Aprašymas: undescribe",
            "Seime: planned", "Seime: threshold", "Seime: close",
            "Suvestinės: hold", "Suvestinės: urgent",
            "—", "hide",
        ]);
        menu.FindAll(".menu__heading").Select(h => h.TextContent).ShouldBe(["Aprašymas", "Seime", "Suvestinės"]);
        menu.Find($"#action-hide-{Item}").TextContent.ShouldBe("Paslėpti");
    }

    [Fact]
    public void A_reported_bill_offers_palikti_first_and_confirming_stays_under_the_description()
    {
        var menu = Record(openItems: true, source: "ai");
        Primary(menu).ShouldBe("keep");
        MenuItems(menu).Take(3).ShouldBe(["Aprašymas: confirm", "Aprašymas: describe", "Aprašymas: undescribe"]);
    }

    [Fact]
    public void A_bill_that_needs_nothing_has_no_filled_button_and_its_menu_is_called_veiksmai()
    {
        var menu = Record(source: "moderator");
        Primary(menu).ShouldBeNull();
        Trigger(menu).TextContent.Trim().ShouldBe(Text("Mod.Actions"));
        MenuItems(menu)[0].ShouldBe("Aprašymas: describe");
    }

    [Fact]
    public void A_waiting_argument_offers_publishing_first_then_merging_restricting_and_hiding_last_without_headings()
    {
        var menu = Argument("pending", candidates: 1);
        Primary(menu).ShouldBe("publish");
        MenuItems(menu).ShouldBe(["merge", "restrict", "—", "hide"]);
        menu.FindAll(".menu__heading").ShouldBeEmpty("an argument's actions are of one kind");
    }

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(true, true, "keep")]
    [InlineData(false, false, "keep")]
    public void A_shown_argument_offers_palikti_first_only_when_it_does_something(bool seen, bool reported, string? primary)
    {
        var menu = Argument("published", seen, reported);
        Primary(menu).ShouldBe(primary);
        MenuItems(menu).ShouldBe(["restrict", "—", "hide"]);
    }

    [Fact]
    public void A_comment_offers_palikti_when_reported_and_atkurti_when_hidden()
    {
        var reported = Comment("published", reported: true);
        Primary(reported).ShouldBe("keep");
        MenuItems(reported).ShouldBe(["restrict", "—", "hide"]);
        reported.Find($"#comment-action-hide-{Item:N}").TextContent.ShouldBe("Paslėpti");

        var shown = Comment("published", reported: false);
        Primary(shown).ShouldBeNull();
        MenuItems(shown).ShouldBe(["restrict", "—", "hide"]);

        var hidden = Comment("hidden", reported: false);
        Primary(hidden).ShouldBe("restore");
        MenuItems(hidden).ShouldBe(["restrict"]);
    }

    [Fact]
    public void The_menu_is_the_browsers_popover_and_each_item_closes_it()
    {
        var menu = Record(source: "ai");
        var trigger = Trigger(menu);
        var popover = menu.Find(".menu");
        popover.GetAttribute("popover").ShouldBe("auto");
        trigger.GetAttribute("popovertarget").ShouldBe(popover.Id);
        trigger.Id.ShouldBe($"mod-more-{Item}");
        trigger.HasAttribute("popovertargetaction").ShouldBeFalse();
        foreach (var item in menu.FindAll(".menu button.menu__item"))
        {
            item.GetAttribute("popovertarget").ShouldBe(popover.Id);
            item.GetAttribute("popovertargetaction").ShouldBe("hide");
        }
    }

    [Fact]
    public void Before_the_island_runs_the_menu_opens_but_every_action_is_disabled()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var menu = Record(openItems: true, source: "ai");
        Trigger(menu).HasAttribute("disabled").ShouldBeFalse("it only shows the popover");
        menu.FindAll("[data-action]").ShouldAllBe(b => b.HasAttribute("disabled"));
    }

    [Fact]
    public void Choosing_an_item_opens_its_form_in_place_of_the_row_and_moves_the_focus_to_it()
    {
        var menu = Record(source: "ai");
        menu.Find($"#action-planned-{Item}").Click();

        menu.FindAll(".menu").ShouldBeEmpty("the form stands in place of the row");
        var heading = menu.Find($"#action-title-{Item}");
        heading.TextContent.ShouldBe(Text("Mod.Action.planned"));
        heading.GetAttribute("tabindex").ShouldBe("-1");
        JSInterop.VerifyFocusAsyncInvoke();
    }
}
