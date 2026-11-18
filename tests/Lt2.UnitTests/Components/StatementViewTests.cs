using Bunit;
using Lt2.Api.Contracts.Organisations;
using Lt2.Web.Client;
using Lt2.Web.Client.Organisations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.16.3 (owner, 2026-11-18): a bubble's tail points at its author's name, so the line right above an organisation's
/// statement is its name; the stance stands above the name, the label ("Patvirtinta" with its hint) under the bubble.
/// </summary>
public sealed class StatementViewTests : BunitContext
{
    private static readonly StatementResponse Statement = new(
        Guid.Parse("0193a1b2-0000-7000-8000-0000000000c1"), "against", "Asociacija prašo balsuoti prieš.", "https://pavyzdys.test/pozicija",
        "pavyzdys.test", null, null, DateTimeOffset.UnixEpoch, null, "published");

    public StatementViewTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
    }

    [Fact]
    public void The_name_stands_right_above_the_bubble_with_the_stance_above_it_and_the_label_under_it()
    {
        var view = Render<StatementView>(p => p.Add(c => c.Statement, Statement).Add(c => c.Name, "Pavyzdinė asociacija")
            .Add(c => c.Slug, "pavyzdine-asociacija").Add(c => c.Label, new OrganisationLabelResponse("domain", "pavyzdys.test")));

        var parts = view.Find("article.said").Children.Take(4).ToList();
        parts[0].ClassList.ShouldContain("said__side", "the stance first, as a small line above the name");
        parts[0].TextContent.ShouldContain("prieš");
        parts[1].ClassList.ShouldContain("statement__who");
        parts[1].TextContent.Trim().ShouldBe("Pavyzdinė asociacija", "nothing but the name on the line the tail points at");
        parts[2].ClassList.ShouldContain("bubble");
        parts[3].ClassList.ShouldContain("statement__meta");
        parts[3].QuerySelector("[data-org-label=domain]").ShouldNotBeNull("the label under the bubble");
        view.Find($"#statement-{Statement.Id}-label").GetAttribute("popovertarget").ShouldBe($"statement-{Statement.Id}-label-panel", "its hint still opens");
    }

    [Fact]
    public void On_the_organisations_own_profile_its_name_stands_above_the_bubble_not_as_a_link()
    {
        var view = Render<StatementView>(p => p.Add(c => c.Statement, Statement).Add(c => c.Name, "Pavyzdinė asociacija"));

        var bubble = view.Find(".bubble");
        bubble.PreviousElementSibling!.ClassList.ShouldContain("statement__who");
        bubble.PreviousElementSibling!.QuerySelector("a").ShouldBeNull();
        bubble.PreviousElementSibling!.TextContent.Trim().ShouldBe("Pavyzdinė asociacija");
    }
}
