using Bunit;
using Lt2.Web.Client;
using Lt2.Web.Client.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// Design 8.2, 8.15 (owner, 2026-11-16): a long member's text keeps its rest behind "Rodyti daugiau", the browser's own
/// <c>details.more</c>, closed and never opened from code; a short one is its bubble as before. An argument shows about
/// 300 characters, a statement or a response about 1 500, line by line.
/// </summary>
public sealed class LongTextTests : BunitContext
{
    private static readonly string Long = string.Concat(Enumerable.Repeat("Ilgas argumentas su daug žodžių. ", 40)).Trim();

    public LongTextTests()
    {
        Services.AddLocalization();
        Services.AddLt2ClientComponents();
    }

    [Fact]
    public void A_short_argument_is_shown_whole_without_a_control()
    {
        var text = Render<LongText>(p => p.Add(c => c.Text, "Trumpas argumentas.").Add(c => c.BubbleClass, "bubble bubble--people voice"));
        text.Find("p.bubble.bubble--people").TextContent.ShouldBe("Trumpas argumentas.");
        text.FindAll("details").ShouldBeEmpty();
    }

    [Fact]
    public void A_long_argument_shows_its_beginning_and_the_rest_behind_Rodyti_daugiau()
    {
        var text = Render<LongText>(p => p.Add(c => c.Text, Long).Add(c => c.BubbleClass, "bubble bubble--people voice").Add(c => c.Id, "reason-more-1"));
        var details = text.Find("div.bubble details.more.stack.stack--tight#reason-more-1");
        details.HasAttribute("open").ShouldBeFalse("an island never opens it from code");
        var shown = text.Find("div.bubble > p").TextContent.TrimEnd('…');
        shown.Length.ShouldBeLessThanOrEqualTo(TextExcerpt.ArgumentShown);
        (shown + " " + details.QuerySelector("p")!.TextContent).ShouldBe(Long, "nothing is lost");
    }

    [Fact]
    public void Opened_the_control_stays_in_the_page_and_reads_Rodyti_mažiau()
    {
        var text = Render<LongText>(p => p.Add(c => c.Text, Long).Add(c => c.BubbleClass, "bubble bubble--people voice"));

        // One summary, not hidden from assistive technology, holding both texts from the resources; the stylesheet shows
        // the one in force by the disclosure's [open] state (display: none hides the other from screen readers too).
        var summary = text.Find("details.long-text__more > summary.linkish.small");
        summary.HasAttribute("hidden").ShouldBeFalse();
        summary.GetAttribute("aria-hidden").ShouldBeNull();
        summary.GetAttribute("tabindex").ShouldBeNull("the browser's own summary stays focusable");
        summary.QuerySelector("span.long-text__closed")!.TextContent.ShouldBe("Rodyti daugiau");
        summary.QuerySelector("span.long-text__opened")!.TextContent.ShouldBe("Rodyti mažiau");
        summary.TextContent.ShouldBe("Rodyti daugiauRodyti mažiau", "no other text inside the summary");
        text.Find(".long-text__cut").GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Fact]
    public void A_statement_or_a_response_up_to_1500_characters_and_its_margin_is_shown_whole_line_by_line()
    {
        var statement = string.Join('\n', Enumerable.Repeat(string.Concat(Enumerable.Repeat("Pozicijos sakinys. ", 20)).Trim(), 5)); // ~1 900
        var text = Render<LongText>(p => p.Add(c => c.Text, statement).Add(c => c.BubbleClass, "bubble bubble--org voice").Add(c => c.Paragraphs, true)
            .Add(c => c.Shown, TextExcerpt.StatementShown).Add(c => c.Margin, TextExcerpt.StatementMargin));
        text.FindAll("details").ShouldBeEmpty("not cut at 300 like an argument");
        text.FindAll("div.bubble--org > p").Count.ShouldBe(5);
    }

    [Fact]
    public void A_statement_longer_than_that_shows_about_1500_characters_and_the_rest_line_by_line()
    {
        var statement = string.Join('\n', Enumerable.Repeat(string.Concat(Enumerable.Repeat("Pozicijos sakinys. ", 20)).Trim(), 10)); // ~3 800
        var text = Render<LongText>(p => p.Add(c => c.Text, statement).Add(c => c.BubbleClass, "bubble bubble--org voice").Add(c => c.Paragraphs, true)
            .Add(c => c.Shown, TextExcerpt.StatementShown).Add(c => c.Margin, TextExcerpt.StatementMargin).Add(c => c.Id, "statement-more-1"));
        var shown = string.Join('\n', text.FindAll("div.bubble--org > p").Select(p => p.TextContent.TrimEnd('…')));
        shown.Length.ShouldBeLessThanOrEqualTo(TextExcerpt.StatementShown);
        shown.Length.ShouldBeGreaterThan(TextExcerpt.StatementShown - 40);
        text.FindAll("#statement-more-1 > p").Count.ShouldBeGreaterThan(1, "the rest keeps its lines");
    }
}
