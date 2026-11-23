using System.Text.RegularExpressions;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>Design 8.16.5: a list's sticky row, the first child of its sheet, and a list page's sticky head.</summary>
public static partial class ListHeadSteps
{
    /// <summary>
    /// The sticky row (<c>.list-head</c>) that holds <paramref name="needle"/> (a heading's or a line's <c>id="…"</c>, or a
    /// heading's text), from its opening tag to its closing one; fails unless that row is the first child of the nearest
    /// sheet before it.
    /// </summary>
    public static string StickyRow(string html, string needle)
    {
        var at = html.IndexOf(needle, StringComparison.Ordinal);
        at.ShouldBeGreaterThan(0, $"the page has {needle}");
        var sheet = html.LastIndexOf("class=\"sheet", at, StringComparison.Ordinal);
        sheet.ShouldBeGreaterThan(0, $"{needle} is on a sheet");
        var first = FirstTag().Match(html, html.IndexOf('>', sheet) + 1);
        first.Success.ShouldBeTrue($"the first child of {needle}'s sheet is an element with a class");
        first.Groups["class"].Value.Split(' ').ShouldContain("list-head", $"the first child of {needle}'s sheet is its sticky row");
        var start = first.Groups["open"].Index;
        var close = "</" + first.Groups["tag"].Value + ">";
        var end = html.IndexOf(close, start, StringComparison.Ordinal);
        at.ShouldBeLessThan(end, $"{needle} stands in the sticky row");
        return html[start..(end + close.Length)];
    }

    /// <summary>
    /// A list page's sticky head (design 8.16.5: <c>main &gt; .page-head</c>, where the list's sheet has no heading of its
    /// own), from its opening tag to its closing one: the title and the tab row under it, never the lead or the filters.
    /// </summary>
    public static string PageHead(string html)
    {
        var id = html.IndexOf("id=\"page-head\"", StringComparison.Ordinal);
        id.ShouldBeGreaterThan(0, "the page has its sticky head");
        var start = html.LastIndexOf('<', id);
        var end = html.IndexOf("</div>", id, StringComparison.Ordinal) + "</div>".Length;
        var head = html[start..end];
        head.ShouldStartWith("<div class=\"");
        head[..head.IndexOf('>')].ShouldContain("page-head");
        head.ShouldContain("<h1");
        head.ShouldNotContain("class=\"lead\"");
        head.ShouldNotContain("<form");
        return head;
    }

    /// <summary>
    /// The head of the five pages that are the tabs of "Seimas" (design 8.21): the heading "Seimas" and the sticky
    /// <c>main &gt; .page-head</c> that holds the tab row alone, from the heading to the head's closing tag. Nothing else
    /// stands in it: not the page's own heading, the lead, the views of "Projektai" or the filters.
    /// </summary>
    public static string SeimasHead(string html)
    {
        var start = html.IndexOf("<h1 class=\"seimas-title\">", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "the page has the heading \"Seimas\"");
        var end = html.IndexOf("</div>", start, StringComparison.Ordinal) + "</div>".Length;
        var head = html[start..end];
        head.ShouldMatch("^<h1 class=\"seimas-title\">Seimas</h1>\\s*<div class=\"page-head\" id=\"page-head\"><nav class=\"row tabs project-tabs\"");
        head.ShouldEndWith("</nav></div>");
        head.ShouldNotContain("class=\"lead\"");
        head.ShouldNotContain("<form");
        return head;
    }

    /// <summary>The first tag after a sheet's opening tag, with only white space before it.</summary>
    [GeneratedRegex("\\G\\s*(?<open><(?<tag>[a-z0-9]+)[^>]*?\\bclass=\"(?<class>[^\"]*)\"[^>]*>)")]
    private static partial Regex FirstTag();
}
