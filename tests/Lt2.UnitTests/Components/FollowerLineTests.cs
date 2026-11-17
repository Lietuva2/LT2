using System.Reflection;
using Bunit;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Follows;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client;
using Lt2.Web.Client.Appeals;
using Microsoft.AspNetCore.Components;
using Lt2.Web.Client.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>Design 8.11.2: "Sekėjai: n" on a thing's own page; nothing with none; the hint beside it.</summary>
public sealed class FollowerLineTests : BunitContext
{
    public FollowerLineTests() => Services.AddLocalization();

    [Fact]
    public void Nothing_is_shown_with_no_followers() => Render<FollowerLine>(p => p.Add(c => c.Count, 0)).Markup.Trim().ShouldBeEmpty();

    [Fact]
    public void The_number_is_exact_from_one_with_the_hint_to_the_methodology()
    {
        var line = Render<FollowerLine>(p => p.Add(c => c.Count, 1));
        line.Find("#followers-count").TextContent.ShouldBe("Sekėjai: 1");
        line.Find("#followers-hint").TextContent.ShouldBe("Kas yra sekėjai?");
        line.Find("#followers-hint-panel a").GetAttribute("href").ShouldBe("/metodika#sekimas");
    }

    /// <summary>The browser look of 2026-11-16: "Sekti" moves the number at once, without a reload.</summary>
    [Fact]
    public void The_number_beside_the_follow_button_moves_with_the_persons_own_follow()
    {
        Services.AddLt2ClientComponents();
        var follows = DispatchProxy.Create<IFollowsApi, ApiFake>();
        ((ApiFake)(object)follows).On("GetMineAsync", ApiResult<MyFollowsResponse>.Ok(new MyFollowsResponse([], [], [])));
        ((ApiFake)(object)follows).On("FollowBillAsync", ApiResult.Ok());
        ((ApiFake)(object)follows).On("UnfollowBillAsync", ApiResult.Ok());
        Services.AddSingleton(follows);
        Services.AddSingleton(DispatchProxy.Create<IProfilesApi, ApiFake>());
        AddBillState();
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var button = Render<FollowButton>(p => p.Add(c => c.Kind, "bill").Add(c => c.Key, "XVP-0412").Add(c => c.Followers, 2));
        button.Find("#followers-count").TextContent.ShouldBe("Sekėjai: 2");
        button.Find("#follow-toggle-bill").Click();
        button.WaitForAssertion(() => button.Find("#followers-count").TextContent.ShouldBe("Sekėjai: 3"));
        button.Find("#follow-toggle-bill").Click();
        button.WaitForAssertion(() => button.Find("#followers-count").TextContent.ShouldBe("Sekėjai: 2"));
    }

    /// <summary>
    /// Owner, 2026-11-17: one compact line: "Sekti", "Sekėjai: n" and one hint, "Kas yra sekimas?", which says what the emails
    /// are about for the kind, that nobody sees who follows and what the number is; the sentence no longer stands beside the
    /// button, and the number has no second hint of its own there.
    /// </summary>
    [Theory]
    [InlineData("bill", "Gausite laišką, kai projektas bus įtrauktas į darbotvarkę ir kai Seimas dėl jo balsuos.")]
    [InlineData("mp", "Laiškuose apie jūsų sekamus projektus matysite, kaip balsavo šis Seimo narys.")]
    [InlineData("organisation", "Gausite laišką, kai organizacija paskelbs poziciją dėl projekto.")]
    [InlineData("member", "Gausite laišką, kai narys paskelbs argumentą ar komentarą.")]
    public void One_compact_line_with_the_follow_hint_for_each_kind(string kind, string what)
    {
        Services.AddLt2ClientComponents();
        var follows = DispatchProxy.Create<IFollowsApi, ApiFake>();
        ((ApiFake)(object)follows).On("GetMineAsync", ApiResult<MyFollowsResponse>.Ok(new MyFollowsResponse([], [], [])));
        Services.AddSingleton(follows);
        var profiles = DispatchProxy.Create<IProfilesApi, ApiFake>();
        ((ApiFake)(object)profiles).On("GetMineAsync", ApiResult<MyProfileResponse>.Fail(new ApiProblem { Status = 404, Code = "profile.not-found" }));
        Services.AddSingleton(profiles);
        AddBillState();
        AddAuthorization().SetAuthorized("ruta.mockute@pavyzdys.test");
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var line = Render<FollowButton>(p => p.Add(c => c.Kind, kind).Add(c => c.Key, "raktas").Add(c => c.Followers, 4));
        var row = line.Find($"#follow-{kind}");
        row.Children.Select(e => e.Id is { Length: > 0 } id ? id : e.ClassName).Take(3).ShouldBe([$"follow-toggle-{kind}", "followers", "small"]);
        line.Find($"#follow-what-{kind}").TextContent.ShouldBe("Kas yra sekimas?");
        line.Find($"#follow-what-{kind}-panel .hint__answer").TextContent.ShouldBe(
            what + " Kas ką seka, niekas kitas nemato. „Sekėjai“ – tiek Lietuva 2.0 narių paprašė šių laiškų; tai ne palaikymas ir ne reprezentatyvi apklausa.");
        line.Find($"#follow-what-{kind}-panel a").GetAttribute("href").ShouldBe("/metodika#sekimas");
        line.FindAll("#followers-hint").ShouldBeEmpty("one hint on the line");
    }

    /// <summary>The bill page's shared state (a first side follows the bill, design 8.11.2), which the button listens to.</summary>
    private void AddBillState()
    {
        Services.AddSingleton(DispatchProxy.Create<IAppealsApi, ApiFake>());
        Services.AddSingleton(DispatchProxy.Create<IModerationApi, ApiFake>());
    }
}