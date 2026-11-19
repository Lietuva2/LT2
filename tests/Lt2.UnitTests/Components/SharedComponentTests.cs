using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Lt2.UnitTests.Components;

/// <summary>
/// The shared components of the UI review (2026-11-15): one copy link, one restore form, one queued argument. Each keeps
/// the ids its pages and the end-to-end tests rely on, and fails visibly instead of breaking the island.
/// </summary>
public sealed class SharedComponentTests : BunitContext
{
    private static readonly Guid Item = Guid.Parse("0193a1b2-0000-7000-8000-000000000412");

    public SharedComponentTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IAppealsApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<Lt2.Api.Contracts.Auth.IAuthApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<Lt2.Api.Contracts.Me.IMeApi, ApiFake>());
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    [Fact]
    public void Copying_a_link_says_so_in_a_status_region_that_was_there_before()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
        var link = Render<CopyLink>(p => p.Add(c => c.Link, "/prisijungti#kodas=ABCD").Add(c => c.LinkId, "share-link").Add(c => c.ButtonId, "share-copy"));

        link.Find("#share-link").TextContent.ShouldBe("http://localhost/prisijungti#kodas=ABCD");
        link.Find("[role=status]").TextContent.Trim().ShouldBeEmpty();
        link.Find("#share-copy").Click();

        link.Find("[role=status]").TextContent.Trim().ShouldBe(Text("Share.Copied"));
        JSInterop.VerifyInvoke("navigator.clipboard.writeText").Arguments.ShouldBe(["http://localhost/prisijungti#kodas=ABCD"]);
    }

    /// <summary>
    /// Design 8.15, "A hint": a real button that opens its own popover with the browser alone (so a press works with
    /// JavaScript off), tells screen readers it opens a description, and links "Plačiau" only when there is more.
    /// </summary>
    [Fact]
    public void A_hint_is_a_button_that_opens_its_own_popover_with_the_answer_and_more_only_when_given()
    {
        var hint = Render<Hint>(p => p.Add(c => c.Id, "threshold-what").Add(c => c.Text, "Kas yra riba?")
            .Add(c => c.Answer, "Riba – dalyvių skaičius. Dabar – 100.").Add(c => c.More, "/metodika#riba"));

        var trigger = hint.Find("#threshold-what");
        trigger.TagName.ShouldBe("BUTTON");
        trigger.GetAttribute("type").ShouldBe("button");
        trigger.TextContent.ShouldBe("Kas yra riba?");
        trigger.GetAttribute("popovertarget").ShouldBe("threshold-what-panel");
        hint.Find("#" + trigger.GetAttribute("aria-describedby")).TextContent.ShouldBe(Services.GetRequiredService<IStringLocalizer<UiText>>()["Hint.Opens"]);
        var panel = hint.Find("#threshold-what-panel");
        panel.GetAttribute("popover").ShouldBe("auto");
        panel.QuerySelector(".hint__answer")!.TextContent.ShouldBe("Riba – dalyvių skaičius. Dabar – 100.");
        panel.QuerySelector("a")!.GetAttribute("href").ShouldBe("/metodika#riba");
        panel.QuerySelector("a")!.TextContent.ShouldBe("Plačiau");
        hint.FindAll("div, p").ShouldBeEmpty(customMessage: "only phrasing content, so a hint can stand inside a paragraph");
        hint.FindAll(".hint__trigger + .visually-hidden + .hint__panel").ShouldHaveSingleItem("the panel follows the trigger, so its link comes next when tabbing");

        var bare = Render<Hint>(p => p.Add(c => c.Id, "labels-what").Add(c => c.Text, "Ką reiškia žymos?").Add(c => c.Answer, "Žyma sako, ar organizacija patikrinta."));
        bare.FindAll("a").ShouldBeEmpty(customMessage: "no \"Plačiau\" where there is nothing more to say");
    }

    [Fact]
    public void A_browser_that_refuses_the_clipboard_gets_a_message_not_a_broken_island()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetException(new JSException("Clipboard denied"));
        var link = Render<CopyLink>(p => p.Add(c => c.Link, "/seimas/XVP-0412").Add(c => c.ButtonId, "copy"));

        link.Find("#copy").Click();

        link.Find("[role=status]").TextContent.Trim().ShouldBe(Text("Share.CopyFailed"));
        link.Find("#copy").HasAttribute("disabled").ShouldBeFalse("the button works again");
    }

    [Fact]
    public void The_restore_form_needs_its_statement_and_keeps_the_confirmation_after_it_closes()
    {
        StatementRequest? sent = null;
        var answer = ApiResult.Fail(new ApiProblem { Status = 400, Errors = new Dictionary<string, string[]> { ["statement"] = ["Parašykite paaiškinimą."] } });
        var form = Render<RestoreForm>(p => p
            .Add(c => c.Id, Item).Add(c => c.IdPrefix, "org-restore").Add(c => c.Sentence, "Sakinys.").Add(c => c.StatementLabel, "Paaiškinimas")
            .Add(c => c.Restore, request => { sent = request; return Task.FromResult(answer); }));

        form.Find($"#org-restore-open-{Item}").Click();
        form.Find("form").Submit();
        form.Find($"#org-restore-statement-{Item}-error").TextContent.ShouldBe("Parašykite paaiškinimą.");
        form.Find($"#org-restore-statement-{Item}").GetAttribute("aria-describedby").ShouldBe($"org-restore-statement-{Item}-error");

        answer = ApiResult.Ok();
        form.Find($"#org-restore-statement-{Item}").Change("Klaida ištaisyta.");
        form.Find("form").Submit();

        sent!.Statement.ShouldBe("Klaida ištaisyta.");
        form.FindAll("form").ShouldBeEmpty();
        form.FindAll($"#org-restore-open-{Item}").ShouldBeEmpty("restored once");
        form.Find("[role=status] .note").TextContent.ShouldBe(Text("Hidden.Restored"));
    }

    [Fact]
    public void Restoring_is_offered_but_disabled_while_the_server_would_refuse()
    {
        var form = Render<RestoreForm>(p => p
            .Add(c => c.Id, Item).Add(c => c.IdPrefix, "restore").Add(c => c.Sentence, "S").Add(c => c.StatementLabel, "P").Add(c => c.Allowed, false)
            .Add(c => c.Restore, _ => Task.FromResult(ApiResult.Ok())));

        form.Find($"#restore-open-{Item}").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_queued_argument_in_the_panel_never_repeats_the_ids_of_the_public_list()
    {
        var reason = new QueueReasonResponse(Item, Guid.NewGuid(), new BillRefResponse("XVP-0412", "Pataisa"), "against", "Argumentas.", "Rūta Mockutė",
            "published", false, ["link"], [], [], DateTimeOffset.UnixEpoch);

        var queue = Render<QueueReasonItem>(p => p.Add(c => c.Reason, reason));
        queue.Find($"#queue-reason-{Item}").ShouldNotBeNull();
        queue.Find($"#actions-{Item}").ShouldNotBeNull();
        queue.Find(".tag--ink").TextContent.ShouldBe("XVP-0412");
        queue.FindAll(".tag").Select(t => t.TextContent).ShouldContain(Text("Mod.NotSeen"));

        var panel = Render<QueueReasonItem>(p => p.Add(c => c.Reason, reason).Add(c => c.Panel, true));
        panel.Find($"#panel-reason-{Item}").ShouldNotBeNull();
        panel.FindAll($"#actions-{Item}").ShouldBeEmpty("the argument in the public list beside it has those ids");
        panel.Find($"#actions-panel-{Item}").ShouldNotBeNull();
        panel.FindAll(".tag--ink").ShouldBeEmpty("the panel is on the bill's own page");
    }

    [Fact]
    public void A_queued_argument_links_to_itself_on_the_bills_page()
    {
        var reason = new QueueReasonResponse(Item, Guid.NewGuid(), new BillRefResponse("XVP-0412", "Pataisa"), "against", "Argumentas.", "Rūta Mockutė",
            "published", false, [], [], [], DateTimeOffset.UnixEpoch);

        var link = Render<QueueReasonItem>(p => p.Add(c => c.Reason, reason)).Find("a.tag--ink");

        link.GetAttribute("href").ShouldBe($"/seimas/XVP-0412?argumentas={Item:N}#reason-{Item:D}");
        link.GetAttribute("title").ShouldBe("Pataisa");
        link.TextContent.ShouldBe("XVP-0412");
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1234, "1 234")]
    [InlineData(null, "–")]
    public void Numbers_are_written_with_a_no_break_space_for_thousands(int? value, string written) =>
        Display.Number(value).ShouldBe(written);

    [Fact]
    public void Dates_are_the_vilnius_day_whatever_the_machine_zone()
    {
        var lateUtc = new DateTimeOffset(2026, 11, 14, 22, 30, 0, TimeSpan.Zero);
        Display.Day(lateUtc).ShouldBe("2026-11-15", "00:30 in Vilnius (UTC+2 in winter)");
        Display.DayTime(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero)).ShouldBe("2026-07-01 12:00", "UTC+3 in summer");
        Display.TryReadDate(" 2026-11-19 ", out var day).ShouldBeTrue();
        day.ShouldBe(new DateOnly(2026, 11, 19));
        Display.TryReadDate("19.11.2026", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Design 6.5 (owner, 2026-11-18): the positions consent is asked before a first side and before a first "Man svarbu", so
    /// its text (version <see cref="Lt2.Api.Contracts.Me.PositionConsentText.Version"/>) covers both: the side on bills and
    /// which arguments the person marks. The same form wherever it is asked; only the confirming button names the action.
    /// </summary>
    [Theory]
    [InlineData(false, "Consent.Submit.for")]
    [InlineData(true, "Consent.Submit.mark")]
    public void The_positions_consent_form_covers_sides_and_man_svarbu_marks(bool forMark, string submit)
    {
        Lt2.Api.Contracts.Me.PositionConsentText.Version.ShouldBe("2026-11-18", "the wording that names the marks");
        var form = Render<PositionConsentForm>(p => p.Add(c => c.Side, "for").Add(c => c.ForMark, forMark).Add(c => c.IdPrefix, "r-"));

        form.Find("#r-consent-title").TextContent.ShouldBe(Text("Consent.Title"));
        var text = form.Find("#r-consent p").TextContent;
        text.ShouldBe(Text("Consent.Text"));
        text.ShouldContain("ar prašote balsuoti už, ar prieš");
        text.ShouldContain("kuriuos argumentus pažymite „Man svarbu“");
        text.ShouldContain("visos jūsų pozicijos ir „Man svarbu“ žymos bus ištrintos");
        var check = form.Find("label.check span").TextContent;
        check.ShouldBe(Text("Consent.Check"));
        check.ShouldContain("mano pozicijas dėl įstatymų projektų ir „Man svarbu“ žymas");
        form.Find("#r-consent-privacy").GetAttribute("href").ShouldBe("/privatumas#pozicijos");
        form.Find("#r-consent-submit").TextContent.ShouldBe(Text(submit));
        form.Find("#r-consent-submit").HasAttribute("disabled").ShouldBeTrue("not before the box is checked");
    }

    [Fact]
    public void Today_comes_from_the_clock_in_vilnius()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 31, 21, 30, 0, TimeSpan.Zero));
        Display.Today(clock).ShouldBe(new DateOnly(2026, 4, 1), "00:30 on 1 April in Vilnius, summer time");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
