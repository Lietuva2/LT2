using System.Reflection;
using Bunit;
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

    public RecordReasonsTests()
    {
        Services.AddLocalization();
        Services.AddLt2Validators();
        Services.AddLt2ClientComponents();
        CommentFakes.Add(Services);
        var appeals = DispatchProxy.Create<IAppealsApi, ApiFake>();
        _appeals = (ApiFake)(object)appeals;
        var moderation = DispatchProxy.Create<IModerationApi, ApiFake>();
        Services.AddSingleton(appeals);
        Services.AddSingleton(moderation);
        AddAuthorization().SetAuthorized("moderatorius@pavyzdys.test");
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

    [Fact]
    public void A_reread_record_keeps_the_shown_order_and_puts_a_new_argument_last()
    {
        var third = Guid.NewGuid();
        var shown = BillRecord(Reason(First, 3), Reason(Second, 2));
        var fresh = BillRecord(Reason(third, 9), Reason(Second, 5), Reason(First, 3));
        RecordReasons.StableOrder(fresh, shown)!.Reasons.Select(r => r.Id).ShouldBe([First, Second, third]);
        RecordReasons.StableOrder(fresh, null).ShouldBe(fresh);
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

    [Fact]
    public void The_actions_beside_an_argument_and_its_moderation_controls_have_different_ids()
    {
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill).Add(c => c.Moderator, true));

        reasons.FindAll($"[id='actions-{First}']").Count.ShouldBe(1, "only the moderation controls use actions-{id}");
        reasons.Find($"#said-actions-{First}").ClassList.ShouldContain("said__actions");
        reasons.Find($"#moderate-{First}").ClassList.ShouldContain("more");
    }

    private static ReasonResponse Reason(Guid id, int marks) => new(id, "for", "Argumentas " + id.ToString()[^1], "Rūta Mockutė", marks, "published", DateTimeOffset.UnixEpoch);

    private static QueueReasonResponse Queued(Guid id) =>
        new(id, Guid.NewGuid(), new BillRefResponse(Bill, "Pataisa"), "for", "Argumentas", "Rūta Mockutė", "published", true, [], [], [], DateTimeOffset.UnixEpoch);

    private static BillRecordResponse BillRecord(params ReasonResponse[] reasons) => new(
        Guid.NewGuid(), new BillRefResponse(Bill, "Gyventojų pajamų mokesčio įstatymo pataisa"), "open", "", null, null,
        new SideCountsResponse(1, 1, 0, 0), new ThresholdResponse(100, "default", null, 1, false, false), reasons);
}
