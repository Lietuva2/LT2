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
    private readonly ApiFake _me;

    private static readonly DateOnly Today = new(2026, 11, 16);

    public UsagePageTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        _me = MeFakes.Add(Services);
        Services.AddSingleton(DispatchProxy.Create<Lt2.Api.Contracts.Auth.IAuthApi, ApiFake>());
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
        page.FindAll("figure.daybars").Count.ShouldBe(2, "visitors and members");

        // Owner, 2026-11-17: one focusable column a day, its day and number as a label shown on hover and focus and as the
        // bar's title; the last day named under the axis, then every seventh before it.
        var chart = page.FindAll("figure.daybars")[0];
        var days = chart.QuerySelectorAll("li.daybars__day");
        days.Length.ShouldBe(30);
        days.ShouldAllBe(d => d.GetAttribute("tabindex") == "0");
        days[^1].GetAttribute("aria-label").ShouldBe("2026-11-16: 42");
        days[^1].QuerySelector(".daybars__value")!.TextContent.ShouldBe("2026-11-16: 42");
        days[^1].QuerySelector("title")!.TextContent.ShouldBe("2026-11-16: 42");
        days[^1].QuerySelector("rect")!.GetAttribute("height").ShouldBe("100");
        days[0].QuerySelector("rect")!.GetAttribute("height").ShouldBe("0");
        chart.QuerySelectorAll(".daybars__label").Select(l => l.TextContent).ShouldBe(["10-19", "10-26", "11-02", "11-09", "11-16"]);
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
        page.FindAll("#passkey-step-confirm").ShouldBeEmpty("an account without a staff role is offered no staff passkey");
    }

    /// <summary>Owner, 2026-11-17: a staff account signed in by a code confirms its staff passkey in place, under the refusal.</summary>
    [Fact]
    public void A_staff_account_in_a_code_session_is_offered_its_staff_passkey_under_the_refusal()
    {
        _me.On("GetAsync", ApiResult<Lt2.Api.Contracts.Me.MeResponse>.Ok(MeFakes.Member with { Roles = ["member", "administrator"], StaffAccount = true }));
        Usage.On("GetAsync", ApiResult<UsageResponse>.Fail(new ApiProblem { Status = 403, Code = "forbidden" }));
        var page = Render<UsageAdmin>();

        page.Find("#usage-refused").TextContent.ShouldBe(L["Usage.Refused"].Value);
        page.WaitForAssertion(() => page.Find("#passkey-step-confirm").HasAttribute("disabled").ShouldBeFalse());
        page.FindAll("#passkey-step-text").ShouldBeEmpty("for staff the page's refusal says why");
    }
}
