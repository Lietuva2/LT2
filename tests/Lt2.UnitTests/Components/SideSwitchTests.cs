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
/// The argument's side in the writing box (owner, 2026-11-13): a two-part switch "Už | Prieš" over real radio inputs;
/// the person's own side preselected (design 8.2), neither half chosen when they have none.
/// </summary>
public sealed class SideSwitchTests : BunitContext
{
    private const string Bill = "XVP-0412";

    private readonly ApiFake _api;

    public SideSwitchTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        MeFakes.Add(Services);
        CommentFakes.Add(Services);
        var api = DispatchProxy.Create<IAppealsApi, ApiFake>();
        _api = (ApiFake)(object)api;
        var record = new BillRecordResponse(
            Guid.NewGuid(), new BillRefResponse(Bill, "Gyventojų pajamų mokesčio įstatymo pataisa"), "open", "Aprašymas.", "moderator",
            new SideCountsResponse(6800, 6000, 4280, 4000), new ThresholdResponse(100, "default", null, 11080, true, false), []);
        _api.On("GetRecordAsync", ApiResult<BillRecordResponse>.Ok(record));
        _api.On("GetMyRestrictionAsync", ApiResult<MyRestrictionResponse>.Ok(new MyRestrictionResponse(false, null, null)));
        Services.AddSingleton(api);

        // A member, not a moderator: the moderators' answer is refused (design 8.12.3).
        var moderation = DispatchProxy.Create<IModerationApi, ApiFake>();
        ((ApiFake)(object)moderation).On("GetBillAsync", ApiResult<ModerationBillResponse>.Fail(new ApiProblem { Status = 403, Code = "forbidden" }));
        Services.AddSingleton(moderation);
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private IRenderedComponent<RecordReasons> OpenWritingBox(string? ownSide, int reasonMaxLength = 2000)
    {
        _api.On("GetMineAsync", ApiResult<MyBillRecordResponse>.Ok(new MyBillRecordResponse(ownSide, [], null, [], null, true)));
        _api.On("GetRulesAsync", ApiResult<AppealRulesResponse>.Ok(new AppealRulesResponse(100, 100, reasonMaxLength, 0, 0, 1500)));
        var reasons = Render<RecordReasons>(p => p.Add(c => c.Number, Bill));
        reasons.Find("#add-reason").Click();
        return reasons;
    }

    [Fact]
    public void The_side_is_a_two_part_switch_over_real_radio_inputs()
    {
        var box = OpenWritingBox("for");
        var halves = box.FindAll("#reason-side .switch__half");
        halves.Select(h => h.TextContent.Trim()).ShouldBe(["Už", "Prieš"]);
        box.FindAll("#reason-side input[type=radio][name=reason-side]").Count.ShouldBe(2);
        box.Markup.ShouldNotContain("class=\"check\"", Case.Sensitive, "not the plain radio list any more");
    }

    [Fact]
    public void The_persons_own_side_is_preselected()
    {
        var box = OpenWritingBox("against");
        box.Find("#reason-side-against").HasAttribute("checked").ShouldBeTrue();
        box.Find("#reason-side-for").HasAttribute("checked").ShouldBeFalse();
    }

    [Fact]
    public void Without_a_side_of_their_own_neither_half_is_chosen()
    {
        var box = OpenWritingBox(null);
        box.FindAll("#reason-side input:checked").ShouldBeEmpty();
        box.Find("#reason-side-for").HasAttribute("checked").ShouldBeFalse();
        box.Find("#reason-side-against").HasAttribute("checked").ShouldBeFalse();
    }

    [Fact]
    public void The_text_field_and_its_counter_follow_the_length_setting()
    {
        var box = OpenWritingBox("for", reasonMaxLength: 2000);
        box.Find("#reason-text").GetAttribute("maxlength").ShouldBe("2000");
        box.Find("#reason-hint").TextContent.ShouldContain("0 iš 2 000 ženklų");
    }

    [Fact]
    public void With_no_length_limit_there_is_no_counter_and_only_the_ceiling_bounds_the_field()
    {
        var box = OpenWritingBox("for", reasonMaxLength: 0);
        box.Find("#reason-text").GetAttribute("maxlength").ShouldBe(MemberTextLimits.Ceiling.ToString(System.Globalization.CultureInfo.InvariantCulture));
        box.Find("#reason-hint").TextContent.ShouldNotContain("ženklų");
        box.Find("#reason-hint").TextContent.ShouldContain("viešas su jūsų vardu", Case.Sensitive, "what becomes public stays inline");
    }

    [Fact]
    public void Choosing_a_half_checks_its_radio()
    {
        var box = OpenWritingBox(null);
        box.Find("#reason-side-for").Change(true);
        box.Find("#reason-side-for").HasAttribute("checked").ShouldBeTrue();
    }
}
