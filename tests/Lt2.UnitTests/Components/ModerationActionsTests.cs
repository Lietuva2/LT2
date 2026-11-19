using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Digests;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.3 (the main action and "Kiti veiksmai" since the owner's 2026-11-17, ModerationMenuTests), 8.12.3: choosing one opens only its own
/// fields with one sentence; only the actions that apply to the item's state are offered; one is open at a time; the
/// confirmation stays after the action closes. A bill's description is written by bill number on any active bill, and an
/// action on a bill without a record creates the record first.
/// </summary>
public sealed class ModerationActionsTests : BunitContext
{
    private const string Bill = "XVP-0533";
    private static readonly Guid Item = Guid.Parse("0193a1b2-0000-7000-8000-000000000412");

    private readonly ApiFake _api;
    private readonly ApiFake _me;

    public ModerationActionsTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        var api = DispatchProxy.Create<IModerationApi, ApiFake>();
        _api = (ApiFake)(object)api;
        _api.On("GetAiAsync", ApiResult<AiStatusResponse>.Ok(new AiStatusResponse(false)));
        Services.AddSingleton(api);

        // Design 6.2: the staff passkey confirmed again before an urgent summary, when the server asks for it.
        var auth = DispatchProxy.Create<IAuthApi, ApiFake>();
        ((ApiFake)(object)auth).On("GetPasskeyRequestOptionsAsync", ApiResult<string>.Ok("{\"challenge\":\"AAAA\"}"));
        Services.AddSingleton(auth);
        var me = DispatchProxy.Create<IMeApi, ApiFake>();
        _me = (ApiFake)(object)me;
        _me.On("ConfirmPasskeyAsync", ApiResult.Ok());
        Services.AddSingleton(me);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private string Text(string key) => Services.GetRequiredService<IStringLocalizer<AppealUi>>()[key];

    private IRenderedComponent<ModerationActions> Render(
        string kind, string status, bool held = false, bool openItems = false, int candidates = 0, string? description = "", bool noRecord = false, string? source = null,
        string recordStatus = "open") =>
        Render<ModerationActions>(p => p
            .Add(c => c.DescriptionSource, source)
            .Add(c => c.RecordStatus, recordStatus)
            .Add(c => c.Id, noRecord ? null : Item)
            .Add(c => c.Number, kind == "record" ? Bill : null)
            .Add(c => c.Kind, kind)
            .Add(c => c.Status, status)
            .Add(c => c.Held, held)
            .Add(c => c.Threshold, 100)
            .Add(c => c.HasOpenItems, openItems)
            .Add(c => c.Description, description)
            .Add(c => c.MergeCandidates, Enumerable.Range(0, candidates)
                .Select(i => new ReasonResponse(Guid.NewGuid(), "for", "Panašus argumentas " + i, "Rūta Mockutė", i, "published", DateTimeOffset.UnixEpoch))
                .ToList()));

    private static string Key(IRenderedComponent<ModerationActions> actions) => actions.Instance.Id?.ToString() ?? Bill;

    private static string[] Offered(IRenderedComponent<ModerationActions> actions) =>
        actions.FindAll("[data-action]").Select(b => b.GetAttribute("data-action")!).ToArray();

    /// <summary>
    /// Owner, 2026-11-17: "Palikti" on a published argument only when it does something: an open report, or not yet seen (it
    /// then marks it seen); never on a seen one without a report, on an open or a closed record.
    /// </summary>
    [Theory]
    [InlineData(false, false, "open", true)]
    [InlineData(true, true, "open", true)]
    [InlineData(true, false, "open", false)]
    [InlineData(true, false, "closed", false)]
    [InlineData(true, true, "closed", true)]
    public void Palikti_on_a_published_argument_only_when_it_is_reported_or_not_yet_seen(bool seen, bool reported, string recordStatus, bool offered)
    {
        var actions = Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Kind, "reason").Add(c => c.Status, "published").Add(c => c.RecordStatus, recordStatus)
            .Add(c => c.Seen, seen).Add(c => c.Reported, reported));
        Offered(actions).Contains("keep").ShouldBe(offered);
        Offered(actions).ShouldContain("hide");
    }

    [Fact]
    public void An_open_bill_offers_the_description_the_timing_actions_and_keep_only_when_something_was_reported()
    {
        Offered(Render("record", "open", openItems: true)).ShouldBe(["keep", "describe", "planned", "threshold", "hold", "urgent", "hide"]);
        Offered(Render("record", "open")).ShouldBe(["describe", "planned", "threshold", "hold", "urgent", "hide"]);
    }

    [Fact]
    public void Nothing_offers_publishing_returning_or_restricting_a_bill()
    {
        var offered = Offered(Render("record", "open", openItems: true));
        offered.ShouldNotContain("publish");
        offered.ShouldNotContain("return");
        offered.ShouldNotContain("restrict", "a bill's record has no author to restrict (design 8.12.4)");
    }

    [Fact]
    public void The_description_action_says_write_on_a_bill_without_one_and_correct_on_one_that_has_it()
    {
        Render("record", "open", description: "").Find($"#action-describe-{Item}").TextContent.ShouldBe(Text("Mod.Action.write"));
        Render("record", "open", description: "Projektas keičia rinkliavas.").Find($"#action-describe-{Item}").TextContent.ShouldBe(Text("Mod.Action.describe"));
    }

    [Fact]
    public void Writing_the_description_goes_by_bill_number_starts_from_the_shown_text_and_the_item_stays_after_it()
    {
        _api.On("WriteDescriptionAsync", ApiResult.Ok());
        var actions = Render("record", "open", description: "Projektas keičia rinkliavas.");
        actions.Find($"#action-describe-{Item}").Click();

        actions.Find($"#action-sentence-{Item}").TextContent.ShouldBe(Text("Mod.Sentence.appeal.describe"));
        actions.Find($"#describe-{Item}").GetAttribute("value").ShouldBe("Projektas keičia rinkliavas.");
        actions.Find($"#describe-{Item}").Input("Projektas keičia vietinių rinkliavų tvarką.");
        actions.Find($"#statement-{Item}").Input("Aprašymas buvo šališkas.");
        actions.Find("form").Submit();

        actions.Markup.ShouldContain(Text("Mod.Done.describe"));
        _api.Calls.ShouldContain("WriteDescriptionAsync");
        Offered(actions).ShouldContain("describe", "the bill stays open; nothing is finished");
    }

    [Fact]
    public void The_official_texts_address_starts_from_the_records_and_goes_with_the_description()
    {
        _api.On("WriteDescriptionAsync", ApiResult.Ok());
        const string Address = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas";
        var actions = Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Number, Bill).Add(c => c.Kind, "record").Add(c => c.Status, "open")
            .Add(c => c.Description, "DI santrauka.").Add(c => c.DescriptionSource, "ai").Add(c => c.DescriptionUrl, Address));
        actions.Find($"#action-describe-{Item}").Click();

        actions.Find($"label[for=describe-url-{Item}]").TextContent.ShouldBe(Text("Mod.DescriptionUrl"));
        actions.Find($"#describe-url-{Item}").GetAttribute("value").ShouldBe(Address, "filled in advance, so leaving it alone keeps it");
        actions.Find($"#describe-{Item}").Input("Pataisyta DI santrauka.");
        actions.Find($"#statement-{Item}").Input("Pataisyta pagal aiškinamąjį raštą.");
        actions.Find("form").Submit();

        var sent = (ModeratorDescriptionRequest)_api.Arguments[_api.Calls.IndexOf("WriteDescriptionAsync")][1]!;
        sent.DescriptionUrl.ShouldBe(Address);

        // Opened again, the field starts from what was saved; emptied, it clears the address.
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-url-{Item}").GetAttribute("value").ShouldBe(Address);
        actions.Find($"#describe-url-{Item}").Input("  ");
        actions.Find($"#describe-{Item}").Input("Dar kartą pataisyta santrauka.");
        actions.Find($"#statement-{Item}").Input("Adresas netikslus.");
        actions.Find("form").Submit();
        ((ModeratorDescriptionRequest)_api.Arguments[_api.Calls.LastIndexOf("WriteDescriptionAsync")][1]!).DescriptionUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("http://e-seimas.lrs.lt/portal/legalAct/lt/TAP/bandymas")]
    [InlineData("https://evil-lrs.lt/")]
    [InlineData("https://lrs.lt@evil.example/")]
    public void A_refused_address_is_said_at_the_field_and_nothing_is_sent(string address)
    {
        _api.On("WriteDescriptionAsync", ApiResult.Ok());
        var actions = Render("record", "open", description: "Projektas keičia rinkliavas.");
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-url-{Item}").Input(address);
        actions.Find($"#statement-{Item}").Input("Pridėtas adresas.");
        actions.Find("form").Submit();

        _api.Calls.ShouldNotContain("WriteDescriptionAsync");
        var message = Services.GetRequiredService<IStringLocalizer<ValidationMessages>>()["DescriptionUrl.Invalid"].Value;
        actions.Find($"#describe-url-{Item}").ParentElement!.ClassList.ShouldContain("field--error");
        actions.Find($"#describe-url-{Item}").ParentElement!.TextContent.ShouldContain(message);
    }

    [Fact]
    public void An_AI_summary_nobody_has_read_can_be_confirmed_corrected_or_removed_and_any_description_removed()
    {
        Offered(Render("record", "open", openItems: true, description: "DI santrauka.", source: "ai"))
            .ShouldBe(["keep", "confirm", "describe", "undescribe", "planned", "threshold", "hold", "urgent", "hide"]);
        Offered(Render("record", "open", description: "Moderatoriaus tekstas.", source: "moderator"))
            .ShouldBe(["describe", "undescribe", "planned", "threshold", "hold", "urgent", "hide"]);
        Offered(Render("record", "open", description: "Patvirtinta santrauka.", source: "ai-draft")).ShouldNotContain("confirm");
    }

    [Fact]
    public void Confirming_goes_by_bill_number_with_an_optional_note_and_then_is_no_longer_offered()
    {
        _api.On("ConfirmDescriptionAsync", ApiResult.Ok());
        var actions = Render("record", "open", openItems: true, description: "DI santrauka.", source: "ai");
        actions.Find($"#action-confirm-{Item}").Click();
        actions.Find($"#action-sentence-{Item}").TextContent.ShouldBe(Text("Mod.Sentence.appeal.confirm"));
        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe(Text("Mod.Note.Record"));
        actions.Find("form").Submit();

        actions.Markup.ShouldContain(Text("Mod.Done.confirm"));
        _api.Calls.ShouldContain("ConfirmDescriptionAsync");
        Offered(actions).ShouldNotContain("confirm");
        Offered(actions).ShouldContain("undescribe");
    }

    [Fact]
    public void Removing_asks_for_a_statement_and_then_the_bill_has_no_description_to_remove()
    {
        _api.On("RemoveDescriptionAsync", ApiResult.Ok());
        var actions = Render("record", "open", description: "DI santrauka.", source: "ai");
        actions.Find($"#action-undescribe-{Item}").Click();
        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe(Text("Mod.Statement.Required"));
        actions.Find($"#statement-{Item}").Input("Santrauka klaidinga.");
        actions.Find("form").Submit();

        _api.Calls.ShouldContain("RemoveDescriptionAsync");
        Offered(actions).ShouldBe(["describe", "planned", "threshold", "hold", "urgent", "hide"]);
        actions.Find($"#action-describe-{Item}").TextContent.ShouldBe(Text("Mod.Action.write"));
    }

    [Fact]
    public void Without_an_AI_provider_the_description_form_mentions_no_draft()
    {
        var actions = Render("record", "open");
        actions.Find($"#action-describe-{Item}").Click();
        actions.FindAll($"#describe-source-{Item}").ShouldBeEmpty();
        actions.FindAll($"#describe-draft-{Item}").ShouldBeEmpty();
        actions.Markup.ShouldNotContain(Text("ModOpen.Draft"));
    }

    [Fact]
    public void With_an_AI_provider_the_description_form_offers_the_draft_from_pasted_official_text()
    {
        _api.On("GetAiAsync", ApiResult<AiStatusResponse>.Ok(new AiStatusResponse(true)));
        var actions = Render("record", "open");
        actions.Find($"#action-describe-{Item}").Click();
        actions.FindAll($"#describe-source-{Item}").Count.ShouldBe(1);
        actions.Find($"#describe-draft-{Item}").HasAttribute("disabled").ShouldBeTrue("no draft without a source text");
    }

    [Fact]
    public void An_action_on_a_bill_without_a_record_creates_the_record_first()
    {
        var created = Guid.Parse("0193a1b2-0000-7000-8000-000000000533");
        _api.On("EnsureRecordAsync", ApiResult<BillRecordIdResponse>.Ok(new BillRecordIdResponse(created)));
        _api.On("HoldAsync", ApiResult.Ok());
        var actions = Render("record", "open", noRecord: true);
        actions.Find($"#action-hold-{Bill}").Click();
        actions.Find($"#statement-{Bill}").Input("Šuolis, tikriname.");
        actions.Find("form").Submit();

        _api.Calls.Where(c => c is not "GetAiAsync").ShouldBe(["EnsureRecordAsync", "HoldAsync"]);
        actions.Markup.ShouldContain(Text("Mod.Done.hold"));
    }

    [Fact]
    public void A_bill_on_hold_offers_release_and_no_urgent_summary()
    {
        var offered = Offered(Render("record", "open", held: true));
        offered.ShouldContain("release");
        offered.ShouldNotContain("hold");
        offered.ShouldNotContain("urgent");
        offered.ShouldNotContain("keep");
    }

    /// <summary>Design 8.23.7: the merge's confirmation says how many comments go with the merged argument.</summary>
    [Fact]
    public void The_merge_confirmation_says_how_many_comments_go_with_the_argument()
    {
        var actions = Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Kind, "reason").Add(c => c.Status, "published").Add(c => c.Comments, 3)
            .Add(c => c.MergeCandidates, [new ReasonResponse(Guid.NewGuid(), "for", "Panašus argumentas", "Rūta Mockutė", 1, "published", DateTimeOffset.UnixEpoch)]));
        actions.Find($"#action-merge-{Item}").Click();
        actions.Find($"#action-sentence-{Item}").TextContent.ShouldContain("Kartu bus ištrinti komentarai po juo: 3.");

        var none = Render("reason", "published", candidates: 1);
        none.Find($"#action-merge-{Item}").Click();
        none.Find($"#action-sentence-{Item}").TextContent.ShouldBe(Text("Mod.Sentence.reason.merge"), "nothing about comments when there are none");
    }

    [Fact]
    public void A_reason_offers_merge_only_when_there_is_something_to_merge_into()
    {
        Offered(Render("reason", "pending")).ShouldBe(["publish", "restrict", "hide"]);
        Offered(Render("reason", "published", candidates: 2)).ShouldBe(["keep", "merge", "restrict", "hide"]);
    }

    /// <summary>Design 6.9 (owner, 2026-11-15): after the final vote only the actions the rules leave are offered.</summary>
    [Fact]
    public void A_closed_bill_offers_palikti_the_description_and_hiding_only()
    {
        Offered(Render("record", "closed", openItems: true, description: "Aprašymas.")).ShouldBe(["keep", "describe", "undescribe", "hide"]);
        Offered(Render("record", "closed", description: "")).ShouldBe(["describe", "hide"]);
        Offered(Render("reason", "published", candidates: 2, recordStatus: "closed")).ShouldBe(["keep", "hide"]);
        Offered(Render("reason", "pending", candidates: 2, recordStatus: "closed")).ShouldBe(["hide"]);
    }

    [Fact]
    public void Choosing_an_action_opens_only_its_own_fields_with_its_sentence()
    {
        var actions = Render("record", "open");
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
        var actions = Render("record", "open");
        actions.Find($"#action-planned-{Item}").Click();
        var date = actions.Find($"#planned-{Item}");
        date.GetAttribute("type").ShouldBe("text");
        date.GetAttribute("inputmode").ShouldBe("numeric");
        date.GetAttribute("placeholder").ShouldBe(Display.DateExample);

        date.Change("19.11.2026");
        actions.Find($"#statement-{Item}").Input("Darbotvarkė pasikeitė.");
        actions.Find("form").Submit();

        actions.Markup.ShouldContain("2026-11-19", Case.Sensitive, "the error shows the expected form");
        actions.FindAll($"#planned-{Item}").Count.ShouldBe(1, "the form stays open; nothing was sent (any call would fail the fake)");
    }

    [Fact]
    public void The_confirmation_stays_after_the_action_closes_and_a_finished_item_offers_nothing_more()
    {
        _api.On("HideReasonAsync", ApiResult.Ok());
        var actions = Render("reason", "pending");
        actions.Find($"#action-hide-{Item}").Click();
        actions.Find($"#statement-{Item}").Input("Įžeidimas.");
        actions.Find("form").Submit();

        actions.FindAll("form").ShouldBeEmpty();
        actions.Markup.ShouldContain(Text("Mod.Done.hide"));
        actions.FindAll("[role=group] button").ShouldBeEmpty("a hidden item is finished");
    }

    private static ApiResult ConfirmationRequired() =>
        ApiResult.Fail(new ApiProblem { Status = 403, Code = "passkey.confirmation-required", Detail = "Patvirtinkite veiksmą prieigos raktu." });

    /// <summary>
    /// Design 6.2: approving an urgent summary needs the staff passkey confirmed in the last ten minutes. When the server asks
    /// for it, the same press opens the browser's passkey prompt, confirms it and sends the summary again with the statement
    /// the moderator typed.
    /// </summary>
    [Fact]
    public void An_urgent_summary_refused_for_a_stale_confirmation_asks_the_passkey_and_is_sent_again_with_the_same_statement()
    {
        _api.OnEach("UrgentAsync", ConfirmationRequired(), ApiResult.Ok());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"staff-key\"}");
        var actions = Render("record", "open");
        actions.Find($"#action-urgent-{Item}").Click();
        actions.Find($"#statement-{Item}").Input("Balsavimas perkeltas į rytojų.");
        actions.Find("form").Submit();

        actions.WaitForAssertion(() => _api.Calls.Count(c => c == "UrgentAsync").ShouldBe(2));
        _me.Calls.ShouldBe(["ConfirmPasskeyAsync"]);
        _api.Arguments.Where((_, i) => _api.Calls[i] == "UrgentAsync").Select(a => ((UrgentDigestRequest)a[1]!).Statement)
            .ShouldBe(["Balsavimas perkeltas į rytojų.", "Balsavimas perkeltas į rytojų."]);
        actions.WaitForAssertion(() => actions.Markup.ShouldContain(Text("Mod.Done.urgent")));
    }

    /// <summary>Design 6.2: without the passkey nothing is sent again; the form stays open with the statement, and says why.</summary>
    [Fact]
    public void Cancelling_the_passkey_prompt_sends_nothing_more_and_keeps_the_statement()
    {
        _api.On("UrgentAsync", ConfirmationRequired());
        var module = JSInterop.SetupModule("./js/passkeys.js");
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetException(new Microsoft.JSInterop.JSException("NotAllowedError"));
        var actions = Render("record", "open");
        actions.Find($"#action-urgent-{Item}").Click();
        actions.Find($"#statement-{Item}").Input("Balsavimas perkeltas į rytojų.");
        actions.Find("form").Submit();

        actions.WaitForAssertion(() => actions.Markup.ShouldContain(Text("Mod.Passkey.Cancelled")));
        _api.Calls.Count(c => c == "UrgentAsync").ShouldBe(1, "no second press without the confirmation");
        _me.Calls.ShouldBeEmpty();
        actions.Find($"#statement-{Item}").GetAttribute("value").ShouldBe("Balsavimas perkeltas į rytojų.");
        actions.Markup.ShouldNotContain(Text("Mod.Done.urgent"));
    }

    [Fact]
    public void Holding_keeps_the_item_and_offers_release_next()
    {
        _api.On("HoldAsync", ApiResult.Ok());
        var actions = Render("record", "open");
        actions.Find($"#action-hold-{Item}").Click();
        actions.Find($"#statement-{Item}").Input("Šuolis, tikriname.");
        actions.Find("form").Submit();

        actions.Markup.ShouldContain(Text("Mod.Done.hold"));
        var offered = Offered(actions);
        offered.ShouldContain("release");
        offered.ShouldNotContain("urgent");
    }
}
