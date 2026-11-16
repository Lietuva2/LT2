using Bunit;
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
}
