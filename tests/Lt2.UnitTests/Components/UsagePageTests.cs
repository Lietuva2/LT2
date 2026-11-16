using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Lt2.Web.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.27: "Lankomumas" shows who is on the site now, 30 days, 12 months and the years, and the most viewed pages and
/// referring sites of a range the administrator picks; "Puslapiai" are people's pages, robots' apart; a refusal says so.
/// </summary>
public sealed class UsagePageTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    public UsagePageTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        var api = DispatchProxy.Create<IUsageApi, ApiFake>();
        Usage = (ApiFake)(object)api;
        Services.AddSingleton(api);
        AddAuthorization().SetAuthorized("administratorius@pavyzdys.test");
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    private ApiFake Usage { get; }

    private IStringLocalizer<AppealUi> L => Services.GetRequiredService<IStringLocalizer<AppealUi>>();

    private static UsageResponse Report(int range, params UsageItemResponse[] pages) => new(
        3,
        1,
        Enumerable.Range(0, 30).Select(i => new UsagePeriodResponse(Today.AddDays(i - 29), i == 29 ? 42 : 0, i == 29 ? 5 : 0, i == 29 ? 100 : 0, i == 29 ? 20 : 0, i == 29 ? 7 : 0)).ToList(),
        Enumerable.Range(0, 12).Select(i => new UsagePeriodResponse(new DateOnly(2026, 11, 1).AddMonths(i - 11), i == 11 ? 300 : 0, i == 11 ? 40 : 0, 0, 0, 0)).ToList(),
        [new UsagePeriodResponse(new DateOnly(2026, 1, 1), 1_200, 90, 5_000, 800, 300)],
        range,
        pages,
        [new UsageItemResponse("naujienos.test", 4)]);

    [Fact]
    public void The_screen_shows_now_today_the_month_the_year_and_the_pages_with_people_and_robots_apart()
    {
        Usage.On("GetAsync", ApiResult<UsageResponse>.Ok(Report(7, new UsageItemResponse("/seimas/XVP-0412", 12))));
        var page = Render<UsageAdmin>();

        page.Find("#usage-online").TextContent.ShouldBe(L.Format("Usage.OnlineCounts", "3", "1"));
        var today = page.Find("#usage-days tbody tr").TextContent;
        today.ShouldContain("2026-11-16");
        today.ShouldContain("42");
        today.ShouldContain("120", customMessage: "views are visitors' and members' together");
        page.Find("#usage-months tbody tr").TextContent.ShouldContain("2026-11");
        page.Find("#usage-years tbody tr").TextContent.ShouldContain("2026");
        page.Find("#usage-pages").TextContent.ShouldContain("/seimas/XVP-0412");
        page.Find("#usage-referrers").TextContent.ShouldContain("naujienos.test");
        page.Find("#usage-range-7").GetAttribute("aria-pressed").ShouldBe("true");
        page.FindAll("svg.daybars").Count.ShouldBe(2, "visitors and members");
        page.Find("#usage-visitors-most").TextContent.ShouldBe(L.Format("Usage.ChartMost", "42", "2026-11-16"), "the chart's scale");
        page.Find("#usage-referrers thead").TextContent.ShouldContain(L["Usage.Referred"].Value, customMessage: "referring sites count visitors, not views");
    }

    [Fact]
    public void Another_range_reads_the_pages_of_those_days()
    {
        Usage.OnEach("GetAsync", ApiResult<UsageResponse>.Ok(Report(7)), ApiResult<UsageResponse>.Ok(Report(30, new UsageItemResponse("/darbotvarke", 9))));
        var page = Render<UsageAdmin>();
        page.Find("#usage-pages-none").TextContent.ShouldBe(L["Usage.NoPages"].Value);

        page.Find("#usage-range-30").Click();

        page.WaitForAssertion(() => page.Find("#usage-pages").TextContent.ShouldContain("/darbotvarke"));
        Usage.Arguments[^1][0].ShouldBe(30);
        page.Find("#usage-range-30").GetAttribute("aria-pressed").ShouldBe("true");
    }

    [Fact]
    public void Anyone_but_an_administrator_in_a_staff_session_is_told_so_and_sees_no_numbers()
    {
        Usage.On("GetAsync", ApiResult<UsageResponse>.Fail(new ApiProblem { Status = 403, Code = "forbidden" }));
        var page = Render<UsageAdmin>();

        page.Find("#usage-refused").TextContent.ShouldBe(L["Usage.Refused"].Value);
        page.FindAll("#usage-online").ShouldBeEmpty();
    }
}
