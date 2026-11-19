using Bunit;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.21, 8.26: "Projektai" is one menu item with four tabs (owner, 2026-11-17: "Statistika" is a menu item of its own;
/// 2026-11-19: "Balsavimai" is a tab again); the item is current on the four pages and everything under them, and the tab row
/// under the heading marks the page it stands on and reaches the other three.
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
    [InlineData(ProjectTab.Votes, "tab-seimas-balsavimai")]
    [InlineData(ProjectTab.Digests, "tab-suvestines")]
    public void The_tab_row_marks_the_page_it_stands_on_and_links_the_other_three(ProjectTab current, string currentId)
    {
        var tabs = Render<ProjectTabs>(p => p.Add(c => c.Current, current));

        var links = tabs.FindAll("#project-tabs a");
        links.Select(a => a.GetAttribute("href")).ShouldBe(["/darbotvarke", "/projektai", "/seimas/balsavimai", "/suvestines"]);
        links.Select(a => a.TextContent).ShouldBe(["Darbotvarkė", "Projektai", "Balsavimai", "Suvestinės"]);
        tabs.FindAll("#project-tabs a[aria-current]").ShouldHaveSingleItem().Id.ShouldBe(currentId);
        tabs.Find($"#{currentId}").GetAttribute("aria-current").ShouldBe("page");
        tabs.Find("#project-tabs").GetAttribute("aria-label").ShouldBe("Projektai");
    }

    [Theory]
    [InlineData("/darbotvarke", ProjectTab.Agenda)]
    [InlineData("projektai?rikiuoti=dalyviai", ProjectTab.Bills)]
    [InlineData("/seimas/balsavimai?kas=visi#balsavimai", ProjectTab.Votes)]
    [InlineData("/suvestines/", ProjectTab.Digests)]
    public void Each_tab_is_its_own_page(string path, ProjectTab tab) =>
        MenuSections.TabOf(path).ShouldBe(tab);

    [Theory]
    [InlineData("/seimas/XVP-0412")]
    [InlineData("/seimas/balsavimai/123")]
    [InlineData("/suvestines/2026-11-16")]
    [InlineData("/statistika")]
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
    [InlineData("/statistika")]
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

    /// <summary>Design 8.28.2: an item cannot be added or renamed without the line that says what its page is.</summary>
    [Fact]
    public void Every_item_of_the_menu_and_the_footer_has_a_name_and_a_line()
    {
        var texts = Services.GetRequiredService<IStringLocalizer<UiText>>();

        MenuSections.Items.ShouldNotBeEmpty();
        foreach (var item in MenuSections.Items)
        {
            texts[item.NameKey].ResourceNotFound.ShouldBeFalse(item.NameKey);
            texts[item.LineKey].ResourceNotFound.ShouldBeFalse(item.LineKey);
            texts[item.NameKey].Value.ShouldNotBeNullOrWhiteSpace(item.NameKey);
            texts[item.LineKey].Value.ShouldNotBeNullOrWhiteSpace(item.LineKey);
        }

        texts["Menu.Projects.Line"].ResourceNotFound.ShouldBeFalse("the dropdown's own line in Kur ką rasti");
        MenuSections.Items.Select(i => i.Slug).ShouldBeUnique();
        MenuSections.Items.Select(i => i.Path).ShouldBeUnique();
    }

    /// <summary>Design 8.21, 8.28.2: where each item stands, in the order every place shows them.</summary>
    [Fact]
    public void The_one_list_holds_the_menu_and_the_footer_in_their_order()
    {
        var texts = Services.GetRequiredService<IStringLocalizer<UiText>>();
        string[] Names(IEnumerable<MenuItem> items) => [.. items.Select(i => texts[i.NameKey].Value)];

        Names(MenuSections.Tabs).ShouldBe(["Darbotvarkė", "Projektai", "Balsavimai", "Suvestinės"]);
        MenuSections.Tabs.Select(t => t.Tab).ShouldBe([ProjectTab.Agenda, ProjectTab.Bills, ProjectTab.Votes, ProjectTab.Digests]);
        Names(MenuSections.In(MenuPlace.Menu)).ShouldBe(["Seimo nariai", "Statistika"]);
        Names(MenuSections.In(MenuPlace.Account)).ShouldBe(["Paskyra"]);
        Names(MenuSections.Browse).ShouldBe(["Darbotvarkė", "Projektai", "Balsavimai", "Suvestinės", "Seimo nariai", "Statistika"]);
        Names(MenuSections.In(MenuPlace.FooterAbout)).ShouldBe(["Metodika", "Moderavimo taisyklės", "Naudojimo taisyklės", "Privatumas"]);
        Names(MenuSections.In(MenuPlace.FooterTakePart)).ShouldBe(["Organizacijos", "Atsiliepimai"]);
    }

    [Theory]
    [InlineData("/seimo-nariai", "mps", "page")]
    [InlineData("/p/jonas-petraitis", "mps", "true")]
    [InlineData("/statistika", "statistics", "page")]
    [InlineData("/statistika", "mps", null)]
    [InlineData("/projektai", "statistics", null)]
    public void An_item_of_the_menus_line_is_marked_on_its_page_and_under_it(string path, string slug, string? mark) =>
        MenuSections.CurrentMark(MenuSections.Items.Single(i => i.Slug == slug), path).ShouldBe(mark);

    [Fact]
    public void The_first_tab_is_the_agenda() =>
        MenuSections.ProjectsHome.ShouldBe("/darbotvarke");

    [Theory]
    [InlineData("/statistika", true)]
    [InlineData("/statistika/?metai=2026", true)]
    [InlineData("/seimas/analitika", false)]
    [InlineData("/darbotvarke", false)]
    public void Statistika_is_current_on_the_statistics_page(string path, bool current) =>
        MenuSections.InStatistics(path).ShouldBe(current);
}
