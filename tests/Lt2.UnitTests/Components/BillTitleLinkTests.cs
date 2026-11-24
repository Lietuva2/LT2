using Bunit;
using Lt2.Web.Client.Components;

namespace Lt2.UnitTests.Components;

/// <summary>Design 8.13.1: a bill's title in a list row, the readable title as the link with the official one in fine print under it.</summary>
public sealed class BillTitleLinkTests : BunitContext
{
    private const string Official = "Gyventojų pajamų mokesčio įstatymo 20 straipsnio pakeitimo įstatymo projektas";

    private IRenderedComponent<BillTitleLink> Render(string? plain, string? id = null, string? cssClass = null) =>
        Render<BillTitleLink>(p =>
        {
            p.Add(c => c.Href, "/seimas/XVP-0412").Add(c => c.Title, Official).Add(c => c.PlainTitle, plain).Add(c => c.Id, id);
            if (cssClass is not null)
            {
                p.Add(c => c.Class, cssClass);
            }
        });

    [Fact]
    public void Without_a_readable_title_the_official_title_is_the_link_and_nothing_stands_under_it()
    {
        var link = Render(null);
        var a = link.Find("a");
        a.TextContent.ShouldBe(Official);
        a.GetAttribute("href").ShouldBe("/seimas/XVP-0412");
        a.ClassList.ShouldContain("tl-item__title");
        link.FindAll(".tl-item__official").ShouldBeEmpty();
    }

    [Fact]
    public void With_a_readable_title_it_is_the_link_and_the_official_title_stands_under_it_in_fine_print()
    {
        var link = Render("Didinamas neapmokestinamųjų pajamų dydis");
        link.Find("a").TextContent.ShouldBe("Didinamas neapmokestinamųjų pajamų dydis");
        var official = link.Find("span.tl-item__official");
        official.TextContent.ShouldBe(Official);
        official.ClassList.ShouldContain("fine");
        link.FindAll("a").Count.ShouldBe(1, "the official title is not a second link");
        link.Markup.IndexOf("<span", StringComparison.Ordinal).ShouldBeGreaterThan(link.Markup.IndexOf("</a>", StringComparison.Ordinal));
    }

    [Fact]
    public void The_class_and_the_id_are_the_callers()
    {
        var link = Render("Trumpas pavadinimas", id: "title-XVP-0412", cssClass: "my-agenda__title");
        link.Find("a").GetAttribute("id").ShouldBe("title-XVP-0412");
        link.Find("a").ClassList.ShouldBe(["my-agenda__title"]);
        Render(null).Find("a").HasAttribute("id").ShouldBeFalse();
        Render(null, cssClass: "").Find("a").HasAttribute("class").ShouldBeFalse("an empty class leaves the link plain");
    }

    [Fact]
    public void Both_titles_are_plain_text_encoded_on_output()
    {
        var link = Render<BillTitleLink>(p => p.Add(c => c.Href, "/x").Add(c => c.Title, "<b>oficialus</b> & kita").Add(c => c.PlainTitle, "<script>alert(1)</script>"));
        link.FindAll("script").ShouldBeEmpty();
        link.FindAll("b").ShouldBeEmpty();
        link.Find("a").TextContent.ShouldBe("<script>alert(1)</script>");
        link.Find("span.tl-item__official").TextContent.ShouldBe("<b>oficialus</b> & kita");
        link.Markup.ShouldContain("&lt;script&gt;");
    }
}
