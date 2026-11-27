using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 7.3, 8.3: the moderators' page says when the hand-copied seating plan and the Seimas's sitting MPs differ, naming
/// who is missing from it and who on it no longer sits, and says nothing while they agree.
/// </summary>
public sealed class SeatingPlanNoteTests : BunitContext
{
    public SeatingPlanNoteTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var proxy = DispatchProxy.Create<IParliamentApi, ApiFake>();
        Services.AddSingleton(proxy);
        Api = (ApiFake)(object)proxy;
    }

    private ApiFake Api { get; }

    private IStringLocalizer<AppealUi> L => Services.GetRequiredService<IStringLocalizer<AppealUi>>();

    private static List<string> PlanSlugs => SeatingPlan.Rows.SelectMany(r => r.Seats).Where(s => s.Length > 0).ToList();

    private static MpListItem Mp(string slug, bool sitting = true, string first = "Vardas", string last = "Pavardė") =>
        new(slug, first, last, null, sitting, null, null);

    [Fact]
    public void Nothing_is_said_while_the_plan_seats_exactly_the_sitting_MPs()
    {
        Api.On("GetMpsAsync", ApiResult<MpListResponse>.Ok(new([.. PlanSlugs.Select(s => Mp(s))], 140, null)));

        var note = Render<SeatingPlanNote>();

        note.Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void A_sitting_MP_off_the_plan_and_an_MP_on_it_who_left_are_named_across_pages_of_the_list()
    {
        var slugs = PlanSlugs;
        var gone = slugs[0];
        var first = slugs.Skip(1).Take(100).Select(s => Mp(s)).ToList();
        var second = slugs.Skip(101).Select(s => Mp(s)).Append(Mp(gone, sitting: false, "Buvęs", "Narys")).Append(Mp("ona-jonaitiene", first: "Ona", last: "Jonaitienė")).ToList();
        Api.OnEach("GetMpsAsync", ApiResult<MpListResponse>.Ok(new(first, 141, "next")), ApiResult<MpListResponse>.Ok(new(second, 141, null)));

        var note = Render<SeatingPlanNote>();

        note.Find("#seating-plan-note").TextContent.ShouldContain(L["Seating.Stale", SeatingPlan.Date].Value);
        note.Find("#seating-plan-missing").TextContent.ShouldBe(L["Seating.NotOnPlan"].Value + " Ona Jonaitienė");
        note.Find("#seating-plan-gone").TextContent.ShouldBe(L["Seating.NoLongerSitting"].Value + " Buvęs Narys");
        note.Find("#seating-plan-note a").GetAttribute("href").ShouldBe(SeatingPlan.Source);
    }

    [Fact]
    public void Nothing_is_said_when_the_list_cannot_be_read()
    {
        Api.On("GetMpsAsync", ApiResult<MpListResponse>.Fail(new ApiProblem { Status = 500 }));

        Render<SeatingPlanNote>().Markup.Trim().ShouldBeEmpty();
    }
}
