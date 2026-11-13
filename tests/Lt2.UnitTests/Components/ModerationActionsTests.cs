using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.3 (decided 2026-11-13): each moderation action is its own control; choosing one opens only its own fields
/// with one sentence; only the actions that apply to the item's state are offered; one is open at a time; the
/// confirmation stays after the action closes.
/// </summary>
public sealed class ModerationActionsTests : BunitContext
{
    private static readonly Guid Item = Guid.Parse("0193a1b2-0000-7000-8000-000000000412");

    private readonly OpenAppealFormTests.AppealsApiFake _api;

    public ModerationActionsTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var api = DispatchProxy.Create<IModerationApi, OpenAppealFormTests.AppealsApiFake>();
        _api = (OpenAppealFormTests.AppealsApiFake)(object)api;
        Services.AddSingleton(api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    private IRenderedComponent<ModerationActions> Render(string kind, string status, bool held = false, bool openItems = false, int candidates = 0, string? description = null) =>
        Render<ModerationActions>(p => p
            .Add(c => c.Id, Item)
            .Add(c => c.Kind, kind)
            .Add(c => c.Status, status)
            .Add(c => c.Held, held)
            .Add(c => c.Threshold, 100)
            .Add(c => c.HasOpenItems, openItems)
            .Add(c => c.Description, description)
            .Add(c => c.MergeCandidates, Enumerable.Range(0, candidates)
                .Select(i => new ReasonResponse(Guid.NewGuid(), "for", "Panašus argumentas " + i, "Rūta Mockutė", i, "published", DateTimeOffset.UnixEpoch))
                .ToList()));

    private static string[] Offered(IRenderedComponent<ModerationActions> actions) =>
        actions.FindAll("[role=group] button").Select(b => b.Id!.Replace("action-", "", StringComparison.Ordinal).Replace("-" + Item, "", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void A_waiting_appeal_offers_publish_return_hide_threshold_and_restrict() =>
        Offered(Render("appeal", "pending")).ShouldBe(["publish", "return", "hide", "threshold", "restrict"]);

    [Fact]
    public void An_open_appeal_offers_the_timing_actions_and_keep_only_when_something_was_reported() =>
        Offered(Render("appeal", "open", openItems: true)).ShouldBe(["keep", "hide", "threshold", "planned", "hold", "urgent", "restrict"]);

    [Fact]
    public void An_open_appeal_offers_correcting_its_description_when_the_text_is_given_and_never_a_waiting_one()
    {
        Offered(Render("appeal", "open", description: "Projektas keičia rinkliavas.")).ShouldBe(["describe", "hide", "threshold", "planned", "hold", "urgent", "restrict"]);
        Offered(Render("appeal", "open", openItems: true, description: "Projektas keičia rinkliavas.")).Take(2).ShouldBe(["keep", "describe"]);
        Offered(Render("appeal", "pending", description: "Projektas keičia rinkliavas.")).ShouldNotContain("describe", "a proposal is returned to its author instead");
    }

    [Fact]
    public void Correcting_the_description_starts_from_the_shown_text_and_the_item_stays_after_it()
    {
        _api.On("CorrectDescriptionAsync", ApiResult.Ok());
        var actions = Render("appeal", "open", description: "Projektas keičia rinkliavas.");
        actions.Find($"#action-describe-{Item}").Click();

        actions.Find($"#action-sentence-{Item}").TextContent.ShouldBe(Text("Mod.Sentence.appeal.describe"));
        actions.Find($"#describe-{Item}").GetAttribute("value").ShouldBe("Projektas keičia rinkliavas.");
        actions.Find($"#describe-{Item}").Input("Projektas keičia vietinių rinkliavų tvarką.");
        actions.Find($"#statement-{Item}").Input("Aprašymas buvo šališkas.");
        actions.Find("form").Submit();

        actions.Markup.ShouldContain(Text("Mod.Done.describe"));
        Offered(actions).ShouldContain("describe", "the appeal stays open; nothing is finished");
    }

    [Fact]
    public void An_appeal_on_hold_offers_release_and_no_urgent_summary()
    {
        var offered = Offered(Render("appeal", "open", held: true));
        offered.ShouldContain("release");
        offered.ShouldNotContain("hold");
        offered.ShouldNotContain("urgent");
        offered.ShouldNotContain("keep");
    }

    [Fact]
    public void A_reason_offers_merge_only_when_there_is_something_to_merge_into()
    {
        Offered(Render("reason", "pending")).ShouldBe(["publish", "hide", "restrict"]);
        Offered(Render("reason", "published", candidates: 2)).ShouldBe(["keep", "hide", "merge", "restrict"]);
    }

    [Fact]
    public void Choosing_an_action_opens_only_its_own_fields_with_its_sentence()
    {
        var actions = Render("appeal", "open");
        actions.Find($"#action-planned-{Item}").Click();

        actions.FindAll("[role=group]").ShouldBeEmpty("one action is open at a time");
        actions.Find($"#action-sentence-{Item}").TextContent.ShouldBe(Text("Mod.Sentence.appeal.planned"));
        actions.FindAll($"#planned-{Item}").Count.ShouldBe(1);
        actions.FindAll($"#stage-{Item}").Count.ShouldBe(1, "the stage is asked only here");
        actions.FindAll($"#threshold-{Item}").ShouldBeEmpty();
        actions.FindAll($"input[type=radio]").ShouldBeEmpty();

        actions.FindAll("button").Single(b => b.TextContent == Text("Cancel")).Click();
        actions.FindAll($"#stage-{Item}").ShouldBeEmpty("closed again");
    }

    [Fact]
    public void The_planned_date_is_a_text_field_in_the_form_yyyy_MM_dd_and_a_wrong_one_is_not_sent()
    {
        var actions = Render("appeal", "open");
        actions.Find($"#action-planned-{Item}").Click();
        var date = actions.Find($"#planned-{Item}");
        date.GetAttribute("type").ShouldBe("text");
        date.GetAttribute("inputmode").ShouldBe("numeric");
        date.GetAttribute("placeholder").ShouldBe(Dates.Example);

        date.Change("19.11.2026");
        actions.Find($"#statement-{Item}").Input("Darbotvarkė pasikeitė.");
        actions.Find("form").Submit();

        actions.Markup.ShouldContain("2026-11-19", Case.Sensitive, "the error shows the expected form");
        actions.FindAll($"#planned-{Item}").Count.ShouldBe(1, "the form stays open; nothing was sent (any call would fail the fake)");
    }

    [Fact]
    public void The_confirmation_stays_after_the_action_closes_and_a_finished_item_offers_nothing_more()
    {
        _api.On("HideAppealAsync", ApiResult.Ok());
        var actions = Render("appeal", "pending");
        actions.Find($"#action-hide-{Item}").Click();
        actions.Find($"#statement-{Item}").Input("Netinkamas aprašymas.");
        actions.Find("form").Submit();

        actions.FindAll("form").ShouldBeEmpty();
        actions.Markup.ShouldContain(Text("Mod.Done.hide"));
        actions.FindAll("[role=group] button").ShouldBeEmpty("a hidden item is finished");
    }

    [Fact]
    public void Holding_keeps_the_item_and_offers_release_next()
    {
        _api.On("HoldAsync", ApiResult.Ok());
        var actions = Render("appeal", "open");
        actions.Find($"#action-hold-{Item}").Click();
        actions.Find($"#statement-{Item}").Input("Šuolis, tikriname.");
        actions.Find("form").Submit();

        actions.Markup.ShouldContain(Text("Mod.Done.hold"));
        var offered = Offered(actions);
        offered.ShouldContain("release");
        offered.ShouldNotContain("urgent");
    }
}
