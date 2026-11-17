using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.2, "A text's layout" (owner, 2026-11-17): people type plain text; each line is a paragraph, two or more lines in a
/// row with the same kind of marker are a list, and nothing else is read from the text. One source file, compiled into
/// SharedKernel and into the browser's assembly.
/// </summary>
public sealed class TextLayoutTests
{
    private static string Shape(IReadOnlyList<TextBlock> blocks) => string.Join(" | ", blocks.Select(b => b.Kind switch
    {
        TextBlockKind.Paragraph => "p:" + b.Items[0].Text,
        TextBlockKind.Bullets => "ul:" + string.Join(";", b.Items.Select(i => i.Text)),
        _ => "ol:" + string.Join(";", b.Items.Select(i => i.Number + "=" + i.Text)),
    }));

    [Fact]
    public void Each_line_is_a_paragraph_and_empty_lines_add_nothing() =>
        Shape(TextLayout.Of("Pirma.\r\n\r\n\n  Antra.  \nTrečia.")).ShouldBe("p:Pirma. | p:Antra. | p:Trečia.");

    [Theory]
    [InlineData("- ")]
    [InlineData("– ")]
    [InlineData("• ")]
    [InlineData("* ")]
    public void Two_or_more_lines_beginning_with_a_bullet_are_a_bulleted_list(string marker) =>
        Shape(TextLayout.Of($"Siūlome:\n{marker}mažinti\n{marker}  palikti\nAčiū.")).ShouldBe("p:Siūlome: | ul:mažinti;palikti | p:Ačiū.");

    [Fact]
    public void Two_or_more_numbered_lines_are_a_numbered_list_with_the_numbers_as_typed() =>
        Shape(TextLayout.Of("1. pirma\n2) antra\n7. septinta")).ShouldBe("ol:1=pirma;2=antra;7=septinta");

    [Fact]
    public void One_marked_line_alone_stays_a_paragraph_as_typed() =>
        Shape(TextLayout.Of("- tik viena eilutė\nTęsinys.\n1. ir viena su numeriu")).ShouldBe("p:- tik viena eilutė | p:Tęsinys. | p:1. ir viena su numeriu");

    [Fact]
    public void A_bulleted_and_a_numbered_run_are_two_lists() =>
        Shape(TextLayout.Of("- a\n- b\n1. c\n2. d")).ShouldBe("ul:a;b | ol:1=c;2=d");

    [Theory]
    [InlineData("-mažinti\n-„palikti“", "ul:mažinti;„palikti“")]
    [InlineData("–mažinti\n•palikti", "ul:mažinti;palikti")]
    [InlineData("1.pirma\n2)antra", "ol:1=pirma;2=antra")]
    [InlineData("- su tarpu\n-be tarpo", "ul:su tarpu;be tarpo")]
    public void A_marker_right_before_a_word_counts_too(string text, string shape) =>
        // Owner, 2026-11-17: people type "-pirma" as often as "- pirma", and the hint shows no space.
        Shape(TextLayout.Of(text)).ShouldBe(shape);

    [Theory]
    [InlineData("-5 % mokesčio\n-3 % akcizo")]
    [InlineData("--\n--")]
    [InlineData("---\n---")]
    [InlineData("2026. metais\n2027. metais")]
    [InlineData("*žodis*\n*kitas*")]
    [InlineData("1.5 procento\n2.5 procento")]
    [InlineData("10.30 val.\n12.00 val.")]
    public void Lines_that_only_look_like_a_list_stay_paragraphs(string text) =>
        TextLayout.Of(text).ShouldAllBe(b => b.Kind == TextBlockKind.Paragraph);

    [Fact]
    public void Indentation_is_ignored_and_there_is_one_level() =>
        Shape(TextLayout.Of("- a\n    - b\n\t- c")).ShouldBe("ul:a;b;c");

    [Fact]
    public void A_short_text_is_not_cut()
    {
        var parts = TextLayout.Of("- a\n- b", TextExcerpt.ArgumentShown, TextExcerpt.ArgumentMargin);
        parts.Rest.ShouldBeNull();
        Shape(parts.Shown).ShouldBe("ul:a;b");
    }

    [Fact]
    public void A_list_cut_by_Rodyti_daugiau_stays_a_list_on_both_sides_with_its_numbers()
    {
        var items = Enumerable.Range(1, 12).Select(n => $"{n}. Punktas su keliais žodžiais, kad būtų ilgesnis tekstas numeris {n}.").ToList();
        var text = "Siūlymai:\n" + string.Join("\n", items);
        var parts = TextLayout.Of(text, TextExcerpt.ArgumentShown, TextExcerpt.ArgumentMargin);

        parts.Rest.ShouldNotBeNull();
        parts.Shown[0].Kind.ShouldBe(TextBlockKind.Paragraph);
        parts.Shown[^1].Kind.ShouldBe(TextBlockKind.Numbered);
        parts.Rest[0].Kind.ShouldBe(TextBlockKind.Numbered, "the rest goes on as the same list");
        var shown = parts.Shown.SelectMany(b => b.Items).ToList();
        var rest = parts.Rest.SelectMany(b => b.Items).ToList();
        shown.Concat(rest).Count(i => !i.Continues).ShouldBe(13, "every item once, the cut one continuing behind the control");
        rest.Where(i => !i.Continues).Select(i => i.Number).ShouldBe(Enumerable.Range(1, 12).Select(n => (int?)n).Skip(shown.Count - 1), "numbers as typed");
    }

    [Fact]
    public void Nothing_is_lost_when_a_text_is_cut()
    {
        var text = string.Join("\n", Enumerable.Range(1, 30).Select(n => $"- eilutė {n} su žodžiais"));
        var parts = TextLayout.Of(text, TextExcerpt.ArgumentShown, TextExcerpt.ArgumentMargin);
        var words = parts.Shown.Concat(parts.Rest!).SelectMany(b => b.Items).SelectMany(i => i.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        words.ShouldBe(text.Split(['\n', ' '], StringSplitOptions.RemoveEmptyEntries).Where(w => w != "-"));
    }
}
