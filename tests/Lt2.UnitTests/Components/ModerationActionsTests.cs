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
        _me.On("GetAsync", ApiResult<MeResponse>.Ok(MeFakes.Member));
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
    public void An_open_bill_offers_the_description_the_timing_actions_closing_and_keep_only_when_something_was_reported()
    {
        Offered(Render("record", "open", openItems: true)).ShouldBe(["keep", "describe", "planned", "threshold", "close", "hold", "urgent", "hide"]);
        Offered(Render("record", "open")).ShouldBe(["describe", "planned", "threshold", "close", "hold", "urgent", "hide"]);
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
            .ShouldBe(["keep", "confirm", "describe", "undescribe", "planned", "threshold", "close", "hold", "urgent", "hide"]);
        Offered(Render("record", "open", description: "Moderatoriaus tekstas.", source: "moderator"))
            .ShouldBe(["describe", "undescribe", "planned", "threshold", "close", "hold", "urgent", "hide"]);
        Offered(Render("record", "open", description: "Patvirtinta santrauka.", source: "ai-draft")).ShouldNotContain("confirm");
    }

    [Fact]
    public void Confirming_goes_by_bill_number_with_an_optional_note_and_then_is_no_longer_offered()
    {
        _api.On("ConfirmDescriptionAsync", ApiResult.Ok());
        var actions = Render("record", "open", openItems: true, description: "DI santrauka.", source: "ai");
        actions.Find($"#action-confirm-{Item}").Click();
        actions.Find($"#action-sentence-{Item}").TextContent.ShouldBe(Text("Mod.Sentence.appeal.confirm"));
        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe(Text("Mod.Statement.Public.Optional"));
        actions.Find($"#statement-hint-{Item}").TextContent.ShouldBe(Text("Mod.Statement.Public.Hint.description"));
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
        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe(Text("Mod.Statement.Public"));
        actions.Find($"#statement-hint-{Item}").TextContent.ShouldBe(Text("Mod.Statement.Public.Hint.description"));
        actions.Find($"#statement-{Item}").Input("Santrauka klaidinga.");
        actions.Find("form").Submit();

        _api.Calls.ShouldContain("RemoveDescriptionAsync");
        Offered(actions).ShouldBe(["describe", "planned", "threshold", "close", "hold", "urgent", "hide"]);
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

    private IRenderedComponent<ModerationActions> RenderWithTitle(string? plainTitle, bool draft = false)
    {
        _api.On("GetAiAsync", ApiResult<AiStatusResponse>.Ok(new AiStatusResponse(draft)));
        return Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Number, Bill).Add(c => c.Kind, "record").Add(c => c.Status, "open")
            .Add(c => c.Description, "Projektas keičia rinkliavas.").Add(c => c.DescriptionSource, "moderator").Add(c => c.CurrentPlainTitle, plainTitle));
    }

    /// <summary>Design 8.13.1: the readable title is a field above the description, starting from the record's own.</summary>
    [Fact]
    public void The_description_form_has_the_readable_title_above_the_description_starting_from_the_records()
    {
        var actions = RenderWithTitle("Keičiamos vietinės rinkliavos");
        actions.Find($"#action-describe-{Item}").Click();

        actions.Find($"label[for=describe-title-{Item}]").TextContent.ShouldBe(Text("Mod.PlainTitle"));
        actions.Find($"#describe-title-hint-{Item}").TextContent.ShouldContain(Text("Mod.PlainTitleHint"));
        var field = actions.Find($"#describe-title-{Item}");
        field.GetAttribute("value").ShouldBe("Keičiamos vietinės rinkliavos");
        field.GetAttribute("maxlength").ShouldBe("90");
        actions.Markup.IndexOf($"id=\"describe-title-{Item}\"", StringComparison.Ordinal)
            .ShouldBeLessThan(actions.Markup.IndexOf($"id=\"describe-{Item}\"", StringComparison.Ordinal), "the title comes first");
        field.ParentElement!.QuerySelector("p.num")!.TextContent.ShouldContain("90");

        var empty = RenderWithTitle(null);
        empty.Find($"#action-describe-{Item}").Click();
        empty.Find($"#describe-title-{Item}").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void The_readable_title_goes_with_the_description_and_a_blank_one_is_sent_as_none()
    {
        _api.On("WriteDescriptionAsync", ApiResult.Ok());
        var actions = RenderWithTitle("Keičiamos vietinės rinkliavos");
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-title-{Item}").Input("Didinamas neapmokestinamųjų pajamų dydis");
        actions.Find($"#statement-{Item}").Input("Pridėtas trumpas pavadinimas.");
        actions.Find("form").Submit();
        ((ModeratorDescriptionRequest)_api.Arguments[_api.Calls.LastIndexOf("WriteDescriptionAsync")][1]!).PlainTitle
            .ShouldBe("Didinamas neapmokestinamųjų pajamų dydis");

        // Saved, the next opening starts from the title just written.
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-title-{Item}").GetAttribute("value").ShouldBe("Didinamas neapmokestinamųjų pajamų dydis");
        actions.Find($"#describe-title-{Item}").Input("   ");
        actions.Find($"#statement-{Item}").Input("Pavadinimas pašalintas.");
        actions.Find("form").Submit();
        ((ModeratorDescriptionRequest)_api.Arguments[_api.Calls.LastIndexOf("WriteDescriptionAsync")][1]!).PlainTitle.ShouldBeNull();
    }

    [Fact]
    public void The_draft_fills_the_readable_title_and_a_draft_without_one_empties_it()
    {
        _api.OnEach("DraftDescriptionAsync",
            ApiResult<DescriptionDraftResponse>.Ok(new DescriptionDraftResponse("Projektas keičia rinkliavas.", "Keičiamos rinkliavos")),
            ApiResult<DescriptionDraftResponse>.Ok(new DescriptionDraftResponse("Projektas keičia tvarką.", null)));
        var actions = RenderWithTitle("Senas pavadinimas", draft: true);
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-source-{Item}").Input("Oficialus tekstas apie rinkliavas.");
        actions.Find($"#describe-draft-{Item}").Click();

        actions.Find($"#describe-title-{Item}").GetAttribute("value").ShouldBe("Keičiamos rinkliavos");
        actions.FindAll($"#describe-title-from-draft-{Item}").Count.ShouldBe(1, "the draft's label stands beside the title as beside the description");

        actions.Find($"#describe-draft-{Item}").Click();
        actions.Find($"#describe-title-{Item}").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void A_refused_readable_title_is_said_at_its_field_and_nothing_is_sent()
    {
        _api.On("WriteDescriptionAsync", ApiResult.Ok());
        var actions = RenderWithTitle(null);
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-title-{Item}").Input("Pirma eilutė\nantra eilutė");
        actions.Find($"#statement-{Item}").Input("Bandymas.");
        actions.Find("form").Submit();

        _api.Calls.ShouldNotContain("WriteDescriptionAsync");
        var message = Services.GetRequiredService<IStringLocalizer<ValidationMessages>>()["PlainTitle.Multiline"].Value;
        actions.Find($"#describe-title-{Item}").ParentElement!.ClassList.ShouldContain("field--error");
        actions.Find($"#describe-title-{Item}").ParentElement!.TextContent.ShouldContain(message);
    }

    [Fact]
    public void A_description_over_the_limit_is_said_at_its_field_and_nothing_is_sent()
    {
        // An agent sets the value past the textarea's maxlength (owner, 2026-11-25): the refusal must be visible at the field.
        _api.On("WriteDescriptionAsync", ApiResult.Ok());
        var actions = RenderWithTitle(null);
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-{Item}").Input(new string('a', AppealLimits.DescriptionMaxLength + 1));
        actions.Find($"#statement-{Item}").Input("Bandymas.");
        actions.Find("form").Submit();

        _api.Calls.ShouldNotContain("WriteDescriptionAsync");
        var message = Services.GetRequiredService<IStringLocalizer<ValidationMessages>>()["Description.TooLong"].Value;
        actions.Find($"#describe-{Item}").ParentElement!.ClassList.ShouldContain("field--error");
        actions.Find($"#describe-{Item}").ParentElement!.TextContent.ShouldContain(message);
        actions.Find($"#describe-{Item}").GetAttribute("aria-describedby")!.ShouldContain(Lt2.Web.Client.Components.FieldError.IdFor($"describe-{Item}"));
    }

    [Fact]
    public void The_servers_refusal_of_the_title_is_said_at_its_field()
    {
        _api.On("WriteDescriptionAsync", ApiResult.Fail(new ApiProblem
        {
            Status = 400, Errors = new Dictionary<string, string[]> { ["plainTitle"] = ["Per ilgas trumpas pavadinimas."] },
        }));
        var actions = RenderWithTitle(null);
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-title-{Item}").Input("Trumpas pavadinimas");
        actions.Find($"#statement-{Item}").Input("Bandymas.");
        actions.Find("form").Submit();

        actions.Find($"#describe-title-{Item}").ParentElement!.TextContent.ShouldContain("Per ilgas trumpas pavadinimas.");
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
        _me.Calls.Where(c => c != "GetAsync").ToList().ShouldBe(["ConfirmPasskeyAsync"]);
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
        _me.Calls.ShouldNotContain("ConfirmPasskeyAsync");
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

    /// <summary>
    /// Design 8.1 step 6 (owner, 2026-11-19): under "Seime" an open bill offers closing it as rejected or withdrawn; a closed one
    /// offers undoing the close only when a moderator closed it, never after the Seimas's vote.
    /// </summary>
    [Theory]
    [InlineData("rejected", true)]
    [InlineData("withdrawn", true)]
    [InlineData("vote", false)]
    [InlineData(null, false)]
    public void A_closed_bill_offers_undoing_the_close_only_when_a_moderator_closed_it(string? reason, bool offered)
    {
        var actions = Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Number, Bill).Add(c => c.Kind, "record").Add(c => c.Status, "closed")
            .Add(c => c.Description, "").Add(c => c.ClosedReason, reason));
        Offered(actions).ShouldBe(offered ? ["describe", "reopen", "hide"] : ["describe", "hide"]);
        Offered(actions).ShouldNotContain("close");
    }

    [Fact]
    public void Closing_asks_what_happened_the_day_as_a_typed_date_the_lrs_source_and_a_statement_and_sends_them()
    {
        _api.On("CloseRecordAsync", ApiResult.Ok());
        var actions = Render("record", "open");
        actions.Find($"#action-close-{Item}").TextContent.ShouldBe(Text("Mod.Action.close"));
        actions.Find($"#action-close-{Item}").Click();

        actions.Find($"#action-sentence-{Item}").TextContent.ShouldBe(Text("Mod.Sentence.appeal.close"));
        actions.FindAll($"input[name=close-reason-{Item}]").Select(r => r.GetAttribute("value")).ShouldBe(["rejected", "withdrawn"]);
        var date = actions.Find($"#close-date-{Item}");
        date.GetAttribute("type").ShouldBe("text", "the field itself is never the browser's date control; the calendar button beside it fills it");
        actions.Find($"#close-date-{Item} + [data-date-pick]").GetAttribute("data-date-pick").ShouldBe($"close-date-{Item}");
        actions.FindAll("input[type=date]").ShouldBeEmpty("the picker's date input is made by js/lt2-dates.js on a press, never rendered");
        date.GetAttribute("placeholder").ShouldBe(Display.DateExample);

        actions.Find($"#close-reason-withdrawn-{Item}").Change(true);
        date.Change("2026-11-17");
        actions.Find($"#close-url-{Item}").Input("https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/pavyzdys");
        actions.Find($"#statement-{Item}").Input("Iniciatoriai projektą atsiėmė.");
        actions.Find("form").Submit();

        var sent = (CloseRecordRequest)_api.Arguments[_api.Calls.IndexOf("CloseRecordAsync")][1]!;
        sent.ShouldBe(new CloseRecordRequest
        {
            Reason = "withdrawn",
            DecidedOn = new DateOnly(2026, 11, 17),
            SourceUrl = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/pavyzdys",
            Statement = "Iniciatoriai projektą atsiėmė.",
        });
        actions.Markup.ShouldContain(Text("Mod.Done.close"));
    }

    [Theory]
    [InlineData(null, "2026-11-17", "https://lrs.lt/a", "reason")]
    [InlineData("rejected", "17.11.2026", "https://lrs.lt/a", "decidedOn")]
    [InlineData("rejected", "2026-11-17", "", "sourceUrl")]
    [InlineData("rejected", "2026-11-17", "https://pavyzdys.test/a", "sourceUrl")]
    public void A_close_without_a_reason_with_a_wrong_day_or_without_an_lrs_source_is_not_sent(string? reason, string day, string source, string field)
    {
        var actions = Render("record", "open");
        actions.Find($"#action-close-{Item}").Click();
        if (reason is not null)
        {
            actions.Find($"#close-reason-{reason}-{Item}").Change(true);
        }

        actions.Find($"#close-date-{Item}").Change(day);
        actions.Find($"#close-url-{Item}").Input(source);
        actions.Find($"#statement-{Item}").Input("Bandymas.");
        actions.Find("form").Submit();

        _api.Calls.ShouldNotContain("CloseRecordAsync");
        actions.FindAll(".field--error").Count.ShouldBeGreaterThan(0, field);
    }

    [Fact]
    public void Undoing_a_close_asks_for_a_statement_and_sends_it()
    {
        _api.On("ReopenRecordAsync", ApiResult.Ok());
        var actions = Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Number, Bill).Add(c => c.Kind, "record").Add(c => c.Status, "closed").Add(c => c.ClosedReason, "rejected"));
        actions.Find($"#action-reopen-{Item}").Click();
        actions.Find($"#action-sentence-{Item}").TextContent.ShouldBe(Text("Mod.Sentence.appeal.reopen"));
        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe(Text("Mod.Statement.Staff"));
        actions.Find($"#statement-hint-{Item}").TextContent.ShouldBe(Text("Mod.Statement.Staff.Hint"));
        actions.Find($"#statement-{Item}").Input("Projektas neatmestas, grąžintas tobulinti.");
        actions.Find("form").Submit();

        ((StatementRequest)_api.Arguments[_api.Calls.IndexOf("ReopenRecordAsync")][1]!).Statement.ShouldBe("Projektas neatmestas, grąžintas tobulinti.");
        actions.Markup.ShouldContain(Text("Mod.Done.reopen"));
    }

    /// <summary>
    /// Owner, 2026-11-22 (design 8.3): each explanation says who reads it. Closing a bill as rejected or withdrawn has no
    /// author and is shown nowhere: only staff read it (moderation_action, audit), and the line under the label says so.
    /// </summary>
    [Fact]
    public void The_close_statement_is_labelled_as_read_by_staff_only()
    {
        var actions = Render("record", "open");
        actions.Find($"#action-close-{Item}").Click();

        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe("Paaiškinimas moderatoriams (privalomas)");
        actions.Find($"#statement-hint-{Item}").TextContent.ShouldBe("Mato tik moderatoriai ir administratoriai.");
        actions.Find($"#statement-{Item}").GetAttribute("aria-describedby").ShouldBe($"statement-hint-{Item}");
    }

    /// <summary>Owner, 2026-11-22 (design 8.3): an argument's author receives the statement of reasons by email.</summary>
    [Fact]
    public void Hiding_an_argument_labels_the_statement_as_the_authors()
    {
        var actions = Render("reason", "published");
        actions.Find($"#action-hide-{Item}").Click();

        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe("Paaiškinimas autoriui (privalomas)");
        actions.FindAll($"#statement-hint-{Item}").ShouldBeEmpty();
    }

    /// <summary>Owner, 2026-11-22 (design 8.3, 8.12.3): a hidden record's page shows the statement to everyone.</summary>
    [Fact]
    public void Hiding_a_record_labels_the_statement_as_public_on_the_bills_page()
    {
        var actions = Render("record", "open");
        actions.Find($"#action-hide-{Item}").Click();

        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe("Viešas paaiškinimas (privalomas)");
        actions.Find($"#statement-hint-{Item}").TextContent.ShouldBe("Rodomas projekto puslapyje.");
    }

    /// <summary>
    /// Design 8.13 (owner, 2026-11-25): a description's statement is public in its change history and laid out as typed, so
    /// writing, removing and confirming a description label it as public, say where it is shown and carry the layout hint.
    /// </summary>
    [Theory]
    [InlineData("describe", "Viešas paaiškinimas (privalomas)")]
    [InlineData("undescribe", "Viešas paaiškinimas (privalomas)")]
    [InlineData("confirm", "Viešas paaiškinimas (nebūtinas)")]
    public void A_descriptions_statement_is_labelled_public_in_the_history_with_the_layout_hint(string action, string label)
    {
        var actions = Render("record", "open", description: "DI santrauka.", source: "ai");
        actions.Find($"#action-{action}-{Item}").Click();

        actions.Find($"label[for=statement-{Item}]").TextContent.ShouldBe(label);
        actions.Find($"#statement-hint-{Item}").TextContent.ShouldBe("Rodomas keitimų istorijoje. Jį el. paštu gaus ir apie aprašymą pranešę nariai.");
        actions.Find($"#statement-layout-{Item}").ClassList.ShouldContain("field__hint");
        var box = actions.Find($"#statement-{Item}");
        box.GetAttribute("rows").ShouldBe("5");
        box.GetAttribute("aria-describedby").ShouldBe($"statement-hint-{Item} statement-layout-{Item}");
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("planned")]
    [InlineData("hide")]
    [InlineData("close")]
    public void Other_actions_statements_have_no_layout_hint(string action)
    {
        var actions = Render("record", "open", description: "DI santrauka.", source: "ai");
        actions.Find($"#action-{action}-{Item}").Click();

        actions.FindAll($"#statement-layout-{Item}").ShouldBeEmpty();
        var box = actions.Find($"#statement-{Item}");
        box.HasAttribute("rows").ShouldBeFalse();
        (box.GetAttribute("aria-describedby") ?? "").ShouldNotContain("statement-layout");
    }

    /// <summary>Design 8.11.4, 8.13: the draft fills the public statement, and the address of the read document it rests on.</summary>
    [Fact]
    public void The_draft_fills_the_statement_and_the_address_of_the_document_it_rests_on()
    {
        const string Address = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/pavyzdys";
        _api.On("DraftDescriptionAsync", ApiResult<DescriptionDraftResponse>.Ok(new DescriptionDraftResponse(
            "Projektas keičia rinkliavas.", "Keičiamos rinkliavos", "Perskaitytas aiškinamasis raštas. Praleista:\n- Vyriausybės argumentai", Address)));
        var actions = RenderWithTitle(null, draft: true);
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-source-{Item}").Input("Oficialus tekstas apie rinkliavas.");
        actions.Find($"#describe-draft-{Item}").Click();

        actions.Find($"#describe-{Item}").GetAttribute("value").ShouldBe("Projektas keičia rinkliavas.");
        actions.Find($"#statement-{Item}").GetAttribute("value").ShouldBe("Perskaitytas aiškinamasis raštas. Praleista:\n- Vyriausybės argumentai");
        actions.Find($"#describe-url-{Item}").GetAttribute("value").ShouldBe(Address);
        actions.Markup.ShouldContain(Text("ModOpen.DraftReady"));
    }

    [Fact]
    public void A_draft_without_an_address_keeps_the_address_the_field_holds()
    {
        const string Address = "https://e-seimas.lrs.lt/portal/legalAct/lt/TAP/pavyzdys";
        _api.On("DraftDescriptionAsync", ApiResult<DescriptionDraftResponse>.Ok(new DescriptionDraftResponse(
            "Projektas keičia rinkliavas.", "Keičiamos rinkliavos", "Perskaitytas įklijuotas tekstas.", null)));
        _api.On("GetAiAsync", ApiResult<AiStatusResponse>.Ok(new AiStatusResponse(true)));
        var actions = Render<ModerationActions>(p => p
            .Add(c => c.Id, Item).Add(c => c.Number, Bill).Add(c => c.Kind, "record").Add(c => c.Status, "open")
            .Add(c => c.Description, "Projektas keičia rinkliavas.").Add(c => c.DescriptionSource, "moderator").Add(c => c.DescriptionUrl, Address));
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-source-{Item}").Input("Oficialus tekstas apie rinkliavas.");
        actions.Find($"#describe-draft-{Item}").Click();

        actions.Find($"#statement-{Item}").GetAttribute("value").ShouldBe("Perskaitytas įklijuotas tekstas.");
        actions.Find($"#describe-url-{Item}").GetAttribute("value").ShouldBe(Address, "pasted text has no address, so the field keeps its own");
    }

    [Fact]
    public void A_draft_without_a_statement_keeps_what_the_moderator_typed()
    {
        _api.On("DraftDescriptionAsync", ApiResult<DescriptionDraftResponse>.Ok(new DescriptionDraftResponse(
            "Iš pateikto teksto neaišku, ką projektas keičia.", null, null, null)));
        var actions = RenderWithTitle(null, draft: true);
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#statement-{Item}").Input("Mano paaiškinimas.");
        actions.Find($"#describe-source-{Item}").Input("Oficialus tekstas.");
        actions.Find($"#describe-draft-{Item}").Click();

        actions.Find($"#statement-{Item}").GetAttribute("value").ShouldBe("Mano paaiškinimas.");
    }

    /// <summary>Design 8.11.4: where a reader of the bills' documents exists, the draft may be asked for with the source empty.</summary>
    [Fact]
    public void With_a_reader_of_official_texts_the_draft_is_offered_on_an_empty_source_and_the_hint_says_so()
    {
        _api.On("GetAiAsync", ApiResult<AiStatusResponse>.Ok(new AiStatusResponse(true, ReadsOfficialTexts: true)));
        var actions = Render("record", "open");
        actions.Find($"#action-describe-{Item}").Click();

        actions.Find($"#describe-draft-{Item}").HasAttribute("disabled").ShouldBeFalse("the reader reads the bill's documents");
        actions.Find($"#describe-source-hint-{Item}").TextContent.ShouldContain(Text("ModOpen.SourceHintReader"));
    }

    /// <summary>
    /// Design 8.13 (owner, 2026-11-25): the description form has a box for an AI agent filling it in the moderator's session,
    /// out of sight and out of the tab order but in the page's text; ticked, the description is sent as written by AI.
    /// </summary>
    [Fact]
    public void The_describe_form_has_a_hidden_box_for_an_AI_agent_out_of_the_tab_order()
    {
        var actions = Render("record", "open", description: "Projektas keičia rinkliavas.");
        actions.Find($"#action-describe-{Item}").Click();

        var box = actions.Find($"#describe-by-ai-{Item}");
        box.GetAttribute("type").ShouldBe("checkbox");
        box.GetAttribute("name").ShouldBe("writtenByAi");
        box.GetAttribute("tabindex").ShouldBe("-1");
        box.HasAttribute("checked").ShouldBeFalse();
        var hidden = actions.Find($"#describe-by-ai-box-{Item}");
        hidden.ClassList.ShouldContain("visually-hidden");
        hidden.Contains(box).ShouldBeTrue("the box is inside the hidden block");
        hidden.QuerySelector("label")!.TextContent.Trim().ShouldBe(Text("Mod.WrittenByAi"));
        Text("Mod.WrittenByAi").ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("undescribe")]
    [InlineData("confirm")]
    [InlineData("hold")]
    [InlineData("planned")]
    public void Only_the_describe_form_has_the_box_for_an_AI_agent(string action)
    {
        var actions = Render("record", "open", description: "DI santrauka.", source: "ai");
        actions.Find($"#action-{action}-{Item}").Click();
        actions.FindAll($"#describe-by-ai-{Item}").ShouldBeEmpty();
        actions.FindAll($"#describe-by-ai-box-{Item}").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_box_for_an_AI_agent_is_sent_with_the_description(bool ticked)
    {
        _api.On("WriteDescriptionAsync", ApiResult.Ok());
        var actions = Render("record", "open", description: "Projektas keičia rinkliavas.");
        actions.Find($"#action-describe-{Item}").Click();
        actions.Find($"#describe-{Item}").Input("Projektas keičia vietinių rinkliavų tvarką.");
        actions.Find($"#statement-{Item}").Input("Parašyta pagal aiškinamąjį raštą.");
        if (ticked)
        {
            actions.Find($"#describe-by-ai-{Item}").Change(true);
        }

        actions.Find("form").Submit();

        var sent = (ModeratorDescriptionRequest)_api.Arguments[_api.Calls.IndexOf("WriteDescriptionAsync")][1]!;
        sent.WrittenByAi.ShouldBe(ticked);
        actions.Markup.ShouldContain(Text("Mod.Done.describe"));
    }

    [Fact]
    public void Without_a_reader_the_source_hint_does_not_promise_reading()
    {
        _api.On("GetAiAsync", ApiResult<AiStatusResponse>.Ok(new AiStatusResponse(true)));
        var actions = Render("record", "open");
        actions.Find($"#action-describe-{Item}").Click();

        actions.Find($"#describe-draft-{Item}").HasAttribute("disabled").ShouldBeTrue();
        actions.Find($"#describe-source-hint-{Item}").TextContent.ShouldNotContain(Text("ModOpen.SourceHintReader"));
    }
}