using Bunit;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.21, 8.26: "Seimas" is one menu item with four tabs, "Projektai", "Balsavimai", "Suvestinės" and "Seimo nariai" ("Statistika" is
/// a menu item of its own); the item is current on the tab pages and everything under them, and the tab row under the
/// heading "Seimas" marks the page it stands on and reaches the other three.
/// </summary>
public sealed class MenuTests : BunitContext
{
    public MenuTests()
    {
        Services.AddLocalization();
    }

    [Theory]
    [InlineData(SeimasTab.Projects, "tab-projektai")]
    [InlineData(SeimasTab.Votes, "tab-seimas-balsavimai")]
    [InlineData(SeimasTab.Digests, "tab-suvestines")]
    [InlineData(SeimasTab.Mps, "tab-mps")]
    public void The_head_shows_the_heading_and_the_tab_row_marking_the_page_it_stands_on(SeimasTab current, string currentId)
    {
        var head = Render<SeimasHead>(p => p.Add(c => c.Current, current));

        head.Find("h1.seimas-title").TextContent.ShouldBe("Seimas");
        var links = head.FindAll("#project-tabs a");
        links.Select(a => a.GetAttribute("href")).ShouldBe(["/darbotvarke", "/seimas/balsavimai", "/suvestines", "/seimo-nariai"]);
        links.Select(a => a.TextContent).ShouldBe(["Projektai", "Balsavimai", "Suvestinės", "Seimo nariai"]);
        links.Count(a => a.GetAttribute("aria-current") == "page").ShouldBe(1);
        head.FindAll("#project-tabs a[aria-current]").ShouldHaveSingleItem().Id.ShouldBe(currentId);
        head.Find($"#{currentId}").GetAttribute("aria-current").ShouldBe("page");
        head.Find("#project-tabs").GetAttribute("aria-label").ShouldBe("Seimas");
        head.Find("#page-head").ClassList.ShouldContain("page-head");
        head.Find("#page-head").Children.ShouldHaveSingleItem().Id.ShouldBe("project-tabs");
    }

    [Theory]
    [InlineData("/darbotvarke", SeimasTab.Projects)]
    [InlineData("projektai?rikiuoti=dalyviai", SeimasTab.Projects)]
    [InlineData("/seimas/balsavimai?kas=visi#balsavimai", SeimasTab.Votes)]
    [InlineData("/seimas/balsavimai/", SeimasTab.Votes)]
    [InlineData("/suvestines/", SeimasTab.Digests)]
    [InlineData("/seimo-nariai?q=jonas", SeimasTab.Mps)]
    public void Each_tab_covers_its_pages(string path, SeimasTab tab) =>
        MenuSections.TabOf(path).ShouldBe(tab);

    [Theory]
    [InlineData("/seimas/XVP-0412")]
    [InlineData("/seimas/balsavimai/123")]
    [InlineData("/suvestines/2026-11-16")]
    [InlineData("/p/jonas-petraitis")]
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
    [InlineData("/seimo-nariai")]
    [InlineData("/p/jonas-petraitis")]
    [InlineData("/p/jonas-petraitis/balsavimai")]
    [InlineData("/frakcijos/reformu-partija")]
    [InlineData("/komitetai/biudzeto")]
    public void Seimas_is_current_on_the_tab_pages_and_everything_under_them(string path) =>
        MenuSections.InSeimas(path).ShouldBeTrue();

    [Theory]
    [InlineData("/")]
    [InlineData("/metodika")]
    [InlineData("/organizacijos")]
    [InlineData("/paskyra")]
    [InlineData("/prisijungti")]
    [InlineData("/suvestines/nustatymai/abcdefghijklmnopqrstuvwxyz")]
    [InlineData("/suvestines/kopijos/abcdefghijklmnopqrstuvwxyz")]
    [InlineData("/seimasx")]
    [InlineData("/statistika")]
    public void Seimas_is_not_current_elsewhere(string path) =>
        MenuSections.InSeimas(path).ShouldBeFalse();

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
        texts["Menu.Projects.Line"].Value.ShouldBe("Keturi puslapiai apie Seimą:");
        MenuSections.Items.Select(i => i.Slug).ShouldBeUnique();
        MenuSections.Items.Select(i => i.Path).ShouldBeUnique();
    }

    /// <summary>Design 8.21, 8.28.2: where each item stands, in the order every place shows them.</summary>
    [Fact]
    public void The_one_list_holds_the_menu_and_the_footer_in_their_order()
    {
        var texts = Services.GetRequiredService<IStringLocalizer<UiText>>();
        string[] Names(IEnumerable<MenuItem> items) => [.. items.Select(i => texts[i.NameKey].Value)];

        Names(MenuSections.Tabs).ShouldBe(["Projektai", "Balsavimai", "Suvestinės", "Seimo nariai"]);
        MenuSections.Tabs.Select(t => t.Tab).ShouldBe([SeimasTab.Projects, SeimasTab.Votes, SeimasTab.Digests, SeimasTab.Mps]);
        Names(MenuSections.In(MenuPlace.Menu)).ShouldBe(["Statistika"]);
        Names(MenuSections.In(MenuPlace.Account)).ShouldBe(["Paskyra"]);
        Names(MenuSections.Browse).ShouldBe(["Projektai", "Balsavimai", "Suvestinės", "Seimo nariai", "Statistika"]);
        Names(MenuSections.In(MenuPlace.FooterAbout)).ShouldBe(["Metodika", "Moderavimo taisyklės", "Naudojimo taisyklės", "Privatumas"]);
        Names(MenuSections.In(MenuPlace.FooterTakePart)).ShouldBe(["Organizacijos", "Atsiliepimai"]);
    }

    [Theory]
    [InlineData("/statistika", "statistics", "page")]
    [InlineData("/statistika/?metai=2026", "statistics", "page")]
    [InlineData("/projektai", "statistics", null)]
    [InlineData("/seimo-nariai", "statistics", null)]
    public void An_item_of_the_menus_line_is_marked_on_its_page(string path, string slug, string? mark) =>
        MenuSections.CurrentMark(MenuSections.Items.Single(i => i.Slug == slug), path).ShouldBe(mark);

    [Fact]
    public void The_first_tab_lands_on_the_agenda() =>
        MenuSections.SeimasHome.ShouldBe("/darbotvarke");

    [Theory]
    [InlineData("/statistika", true)]
    [InlineData("/statistika/?metai=2026", true)]
    [InlineData("/seimas/analitika", false)]
    [InlineData("/darbotvarke", false)]
    public void Statistika_is_current_on_the_statistics_page(string path, bool current) =>
        MenuSections.InStatistics(path).ShouldBe(current);
}
