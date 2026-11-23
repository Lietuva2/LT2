using Bunit;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// The one "show more" (owner, 2026-11-19): "Rodoma N iš M" and a link to the grown page, followed by enhanced navigation on a
/// server-rendered page and before an island runs; once an island runs and fetches the page itself, the click is its own.
/// </summary>
public sealed class ShowMoreTests : BunitContext
{
    public ShowMoreTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
    }

    [Fact]
    public void Server_rendered_it_is_the_status_and_a_plain_link_to_the_grown_page()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var more = Render<ShowMore>(p => p.Add(c => c.Id, "votes-more").Add(c => c.Shown, 30).Add(c => c.Total, 1234)
            .Add(c => c.Href, "/seimas/balsavimai?rodyti=60#vote-31").Add(c => c.OnMore, () => { }));

        more.Find("#votes-more-line").ClassList.ShouldContain("show-more");
        more.Find("#votes-more-shown").TextContent.ShouldBe("Rodoma 30 iš 1 234");
        var link = more.Find("a#votes-more");
        // Owner, 2026-11-19: a plain text link, not a button's look (it clashed with the buttons under a list).
        (link.TextContent, link.GetAttribute("href"), link.ClassName).ShouldBe(("Rodyti daugiau", "/seimas/balsavimai?rodyti=60#vote-31", "show-more__link"));
        link.HasAttribute("data-enhance-nav").ShouldBeFalse("enhanced navigation fetches it");
    }

    [Fact]
    public void In_a_running_island_the_click_fetches_in_place_and_without_an_address_there_is_no_link()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        var pressed = 0;
        var more = Render<ShowMore>(p => p.Add(c => c.Id, "comments-more").Add(c => c.Shown, 10).Add(c => c.Total, 12)
            .Add(c => c.Href, "/seimas/XVP-0412?komentarai=x-20#k").Add(c => c.Text, "Rodyti daugiau komentarų").Add(c => c.OnMore, () => pressed++));
        var link = more.Find("a#comments-more");
        link.GetAttribute("data-enhance-nav").ShouldBe("false");
        link.ClassName.ShouldBe("show-more__link", "the same plain link once the island runs");
        link.Click();
        pressed.ShouldBe(1);

        Render<ShowMore>(p => p.Add(c => c.Id, "all").Add(c => c.Shown, 12).Add(c => c.Total, 12)).FindAll("a").ShouldBeEmpty();
    }
}
