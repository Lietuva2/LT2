using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.2, 8.5.6, 8.14.3, 8.19.3 (owner, 2026-11-16): a long member's text shows its beginning and the rest behind
/// "Rodyti daugiau" (or, in the MPs' email, a link): about 300 characters of an argument, 1 500 of a statement or a
/// response. One source file, compiled into SharedKernel and into the browser's assembly.
/// </summary>
public sealed class TextExcerptTests
{
    private static string Words(int length)
    {
        var text = string.Concat(Enumerable.Repeat("žodis ", (length / 6) + 1));
        return text[..(length - 1)].TrimEnd() + "x"; // at most length characters
    }

    [Fact]
    public void A_text_up_to_the_shown_part_plus_the_margin_is_shown_whole()
    {
        var text = Words(TextExcerpt.ArgumentShown + TextExcerpt.ArgumentMargin);
        TextExcerpt.Of(text).ShouldBe(new TextExcerpt(text, null));
        TextExcerpt.Of("Trumpas argumentas.").Rest.ShouldBeNull();
    }

    [Fact]
    public void A_longer_text_shows_at_most_the_shown_part_and_keeps_the_rest()
    {
        var excerpt = TextExcerpt.Of(Words(1200));
        excerpt.Rest.ShouldNotBeNull();
        excerpt.Shown.Length.ShouldBeLessThanOrEqualTo(TextExcerpt.ArgumentShown);
        excerpt.Shown.Length.ShouldBeGreaterThan(TextExcerpt.ArgumentShown - 10, "cut near the shown length, not far before it");
    }

    [Fact]
    public void The_cut_falls_on_a_word_boundary()
    {
        var excerpt = TextExcerpt.Of(Words(1200));
        excerpt.Shown.ShouldEndWith("žodis");
        excerpt.Rest!.ShouldStartWith("žodis");
    }

    [Fact]
    public void Nothing_is_lost_the_shown_part_and_the_rest_make_the_text()
    {
        var text = Words(5000);
        var excerpt = TextExcerpt.Of(text);
        (excerpt.Shown + " " + excerpt.Rest).ShouldBe(text);
    }

    [Fact]
    public void A_single_word_longer_than_the_shown_part_is_cut_where_the_shown_part_ends()
    {
        var text = new string('a', 1000);
        var excerpt = TextExcerpt.Of(text);
        excerpt.Shown.Length.ShouldBe(TextExcerpt.ArgumentShown);
        (excerpt.Shown + excerpt.Rest).ShouldBe(text);
    }

    [Fact]
    public void A_statement_or_a_response_within_the_default_length_setting_is_never_cut()
    {
        var text = Words(SettingKeys.TextsMaxLength.Default);
        TextExcerpt.Of(text, TextExcerpt.StatementShown, TextExcerpt.StatementMargin).Rest.ShouldBeNull();
        var longer = TextExcerpt.Of(Words(5000), TextExcerpt.StatementShown, TextExcerpt.StatementMargin);
        longer.Shown.Length.ShouldBeLessThanOrEqualTo(TextExcerpt.StatementShown);
        longer.Shown.Length.ShouldBeGreaterThan(TextExcerpt.StatementShown - 10);
    }

    [Fact]
    public void The_browser_and_the_email_cut_a_text_the_same_way()
    {
        foreach (var text in new[] { Words(350), Words(401), Words(1200), new string('a', 900), "Trumpas." })
        {
            var server = TextExcerpt.Of(text);
            var browser = Lt2.Web.Client.TextExcerpt.Of(text);
            (browser.Shown, browser.Rest).ShouldBe((server.Shown, server.Rest));
        }
    }
}
