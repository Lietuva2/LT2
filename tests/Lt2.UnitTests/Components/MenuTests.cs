using Bunit;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.21, 8.26: "Projektai" is one menu item with four tabs; the item is current on the four pages and everything under them,
/// and the tab row under the heading marks the page it stands on and reaches the other three.
/// </summary>
public sealed class MenuTests : BunitContext
{
    public MenuTests()
    {
        Services.AddLocalization();
    }

    [Theory]
    [InlineData(ProjectTab.Agenda, "tab-darbotvarke")]
    [InlineData(ProjectTab.Bills, "tab-projektai")]
    [InlineData(ProjectTab.Digests, "tab-suvestines")]
    [InlineData(ProjectTab.Analytics, "tab-seimas-analitika")]
    public void The_tab_row_marks_the_page_it_stands_on_and_links_the_other_three(ProjectTab current, string currentId)
    {
        var tabs = Render<ProjectTabs>(p => p.Add(c => c.Current, current));

        var links = tabs.FindAll("#project-tabs a");
        links.Select(a => a.GetAttribute("href")).ShouldBe(["/darbotvarke", "/projektai", "/suvestines", "/seimas/analitika"]);
        links.Select(a => a.TextContent).ShouldBe(["Darbotvarkė", "Projektai", "Suvestinės", "Analitika"]);
        tabs.FindAll("#project-tabs a[aria-current]").ShouldHaveSingleItem().Id.ShouldBe(currentId);
        tabs.Find($"#{currentId}").GetAttribute("aria-current").ShouldBe("page");
        tabs.Find("#project-tabs").GetAttribute("aria-label").ShouldBe("Projektai");
    }

    [Theory]
    [InlineData("/darbotvarke", ProjectTab.Agenda)]
    [InlineData("projektai?rikiuoti=dalyviai", ProjectTab.Bills)]
    [InlineData("/suvestines/", ProjectTab.Digests)]
    [InlineData("/seimas/analitika?metai=2026", ProjectTab.Analytics)]
    public void Each_tab_is_its_own_page(string path, ProjectTab tab) =>
        MenuSections.TabOf(path).ShouldBe(tab);

    [Theory]
    [InlineData("/seimas/XVP-0412")]
    [InlineData("/seimas/balsavimai")]
    [InlineData("/seimas/balsavimai/123")]
    [InlineData("/suvestines/2026-11-16")]
    [InlineData("/")]
    public void A_page_under_a_tab_is_no_tab_itself(string path) =>
        MenuSections.TabOf(path).ShouldBeNull();

    [Theory]
    [InlineData("/darbotvarke")]
    [InlineData("/projektai")]
    [InlineData("/projektai?skirtukas=uzdaryti")]
    [InlineData("/seimas/balsavimai")]
    [InlineData("/seimas/balsavimai/9001")]
    [InlineData("/seimas/XVP-0412")]
    [InlineData("/seimas/XVP-0412/istorija")]
    [InlineData("/suvestines")]
    [InlineData("/suvestines/2026-11-16")]
    [InlineData("/suvestines/pristatymas")]
    public void Projektai_is_current_on_the_four_pages_and_everything_under_them(string path)
    {
        MenuSections.InProjects(path).ShouldBeTrue();
        MenuSections.InMps(path).ShouldBeFalse();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/metodika")]
    [InlineData("/organizacijos")]
    [InlineData("/paskyra")]
    [InlineData("/suvestines/nustatymai/abcdefghijklmnopqrstuvwxyz")]
    [InlineData("/suvestines/kopijos/abcdefghijklmnopqrstuvwxyz")]
    [InlineData("/seimasx")]
    public void Projektai_is_not_current_elsewhere(string path) =>
        MenuSections.InProjects(path).ShouldBeFalse();

    [Theory]
    [InlineData("/seimo-nariai", true)]
    [InlineData("/p/jonas-petraitis", true)]
    [InlineData("/p/jonas-petraitis/balsavimai", true)]
    [InlineData("/frakcijos/reformu-partija", true)]
    [InlineData("/komitetai/biudzeto", true)]
    [InlineData("/projektai", false)]
    [InlineData("/prisijungti", false)]
    public void Seimo_nariai_is_current_on_the_list_and_on_profiles(string path, bool current) =>
        MenuSections.InMps(path).ShouldBe(current);

    [Fact]
    public void The_menu_item_opens_the_agenda() =>
        MenuSections.ProjectsHome.ShouldBe("/darbotvarke");
}
