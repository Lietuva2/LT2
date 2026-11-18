using System.Diagnostics;
using System.Text;
using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// The version histories show what changed (owner, 2026-11-18): a word-level comparison of two versions of a typed text, laid
/// out as paragraphs and lists (design 8.2, "A text's layout"). One source file, compiled into SharedKernel and into the
/// browser's assembly.
/// </summary>
public sealed class TextDiffTests
{
    /// <summary>"=" same, "+" inserted, "-" removed; items separated by " | ".</summary>
    private static string Shape(IReadOnlyList<ChangedItem> items) =>
        string.Join(" | ", items.Select(i => string.Concat(i.Pieces.Select(Mark))));

    private static string Mark(TextPiece p) => p.Change switch
    {
        TextChange.Same => p.Text,
        TextChange.Inserted => "{+" + p.Text + "}",
        _ => "{-" + p.Text + "}",
    };

    private static string Older(IEnumerable<TextPiece> pieces) => string.Concat(pieces.Where(p => p.Change != TextChange.Inserted).Select(p => p.Text));

    private static string Newer(IEnumerable<TextPiece> pieces) => string.Concat(pieces.Where(p => p.Change != TextChange.Removed).Select(p => p.Text));

    [Fact]
    public void An_unchanged_text_is_the_same_throughout()
    {
        var items = TextDiff.Of("Pirma pastraipa.\nAntra.", "Pirma pastraipa.\nAntra.");
        items.SelectMany(i => i.Pieces).ShouldAllBe(p => p.Change == TextChange.Same);
        Shape(items).ShouldBe("Pirma pastraipa. | Antra.");
    }

    [Fact]
    public void An_inserted_word_is_inserted_alone() =>
        Shape(TextDiff.Of("Projektas mažina tarifą.", "Projektas gerokai mažina tarifą.")).ShouldBe("Projektas {+gerokai }mažina tarifą.");

    [Fact]
    public void A_deleted_word_is_removed_alone() =>
        Shape(TextDiff.Of("Projektas gerokai mažina tarifą.", "Projektas mažina tarifą.")).ShouldBe("Projektas {-gerokai }mažina tarifą.");

    [Fact]
    public void A_replaced_word_is_removed_then_inserted() =>
        Shape(TextDiff.Of("Projektas mažina tarifą.", "Projektas didina tarifą.")).ShouldBe("Projektas {-mažina}{+didina} tarifą.");

    [Fact]
    public void Replaced_words_with_only_a_space_between_read_as_one_removal_and_one_insertion() =>
        Shape(TextDiff.Of("Projektas mažina tarifą.", "Projektas didina mokestį.")).ShouldBe("Projektas {-mažina tarifą}{+didina mokestį}.");

    [Fact]
    public void A_word_left_between_changes_stays_unmarked() =>
        Shape(TextDiff.Of("Tarifas mažėtų nuo 20 iki 15 procentų.", "Tarifas augtų nuo 15 iki 20 procentų."))
            .ShouldBe("Tarifas {-mažėtų}{+augtų} nuo {-20}{+15} iki {-15}{+20} procentų.");

    [Fact]
    public void A_moved_paragraph_is_removed_where_it_was_and_inserted_where_it_is() =>
        Shape(TextDiff.Of("Pirma.\nAntra pastraipa.\nTrečia pastraipa.", "Antra pastraipa.\nTrečia pastraipa.\nPirma."))
            .ShouldBe("{-Pirma.} | Antra pastraipa. | Trečia pastraipa. | {+Pirma.}");

    [Fact]
    public void A_paragraph_inserted_before_a_changed_one_does_not_take_its_changes() =>
        Shape(TextDiff.Of("Įžanga.\nProjektas mažina pajamų mokesčio tarifą.", "Įžanga.\nVisai nauja pastraipa.\nProjektas didina pajamų mokesčio tarifą."))
            .ShouldBe("Įžanga. | {+Visai nauja pastraipa.} | Projektas {-mažina}{+didina} pajamų mokesčio tarifą.");

    [Fact]
    public void Lithuanian_letters_are_part_of_their_word() =>
        Shape(TextDiff.Of("Ąžuolas žaliuoja.", "Ąžuolai žaliuoja.")).ShouldBe("{-Ąžuolas}{+Ąžuolai} žaliuoja.");

    [Fact]
    public void Words_spaces_and_each_punctuation_mark_are_tokens_so_nothing_is_lost()
    {
        TextDiff.Tokens("Tarifas – 15 %, ne „20“.").ShouldBe(["Tarifas", " ", "–", " ", "15", " ", "%", ",", " ", "ne", " ", "„", "20", "“", "."]);
        string.Concat(TextDiff.Tokens("Ąžuolas,  šaka\t1.5")).ShouldBe("Ąžuolas,  šaka\t1.5");
    }

    [Fact]
    public void Changed_punctuation_is_marked_alone() =>
        Shape(TextDiff.Of("Taip, ne.", "Taip; ne!")).ShouldBe("Taip{-,}{+;} ne{-.}{+!}");

    [Fact]
    public void An_empty_older_text_is_inserted_whole() =>
        Shape(TextDiff.Of(null, "Pirma.\n- a\n- b")).ShouldBe("{+Pirma.} | {+a} | {+b}");

    [Fact]
    public void An_empty_newer_text_is_removed_whole() =>
        Shape(TextDiff.Of("Pirma.\nAntra.", "")).ShouldBe("{-Pirma.} | {-Antra.}");

    [Fact]
    public void A_changed_list_item_keeps_the_lists_kind_and_number()
    {
        var items = TextDiff.Of("Siūlome:\n1. mažinti tarifą\n2. palikti lengvatas", "Siūlome:\n1. mažinti tarifą\n2. panaikinti lengvatas\n3. skelbti ataskaitas");
        items.Select(i => (i.Kind, i.Number)).ShouldBe([
            (TextBlockKind.Paragraph, (int?)null), (TextBlockKind.Numbered, 1), (TextBlockKind.Numbered, 2), (TextBlockKind.Numbered, 3)]);
        Shape(items).ShouldBe("Siūlome: | mažinti tarifą | {-palikti}{+panaikinti} lengvatas | {+skelbti ataskaitas}");
    }

    [Fact]
    public void A_removed_list_item_keeps_its_place_and_its_old_number()
    {
        var items = TextDiff.Of("- a\n- b\n- c", "- a\n- c");
        Shape(items).ShouldBe("a | {-b} | c");
        items.ShouldAllBe(i => i.Kind == TextBlockKind.Bullets);
    }

    [Fact]
    public void Same_and_removed_pieces_give_the_older_line_and_same_and_inserted_the_newer()
    {
        var random = new Random(20261118);
        string[] words = ["Seimas", "projektas", "mokestis", "ąžuolas", "–", ",", ".", "20", "%", "žmonės", "  "];
        for (var round = 0; round < 200; round++)
        {
            var older = string.Join(' ', Enumerable.Range(0, random.Next(1, 30)).Select(_ => words[random.Next(words.Length)])).Trim();
            var newer = string.Join(' ', Enumerable.Range(0, random.Next(1, 30)).Select(_ => words[random.Next(words.Length)])).Trim();
            var pieces = TextDiff.Words(older, newer);
            Older(pieces).ShouldBe(older);
            Newer(pieces).ShouldBe(newer);
        }
    }

    [Fact]
    public void A_long_text_is_compared_within_a_reasonable_time()
    {
        // About 10 000 characters a version (the introduction letter's limit), in paragraphs, a word changed in each.
        var older = new StringBuilder();
        var newer = new StringBuilder();
        for (var p = 0; p < 40; p++)
        {
            var line = string.Join(' ', Enumerable.Range(0, 35).Select(w => $"žodis{p}x{w}"));
            older.Append(line).Append('\n');
            newer.Append(line.Replace($"žodis{p}x17", "pakeista", StringComparison.Ordinal)).Append('\n');
        }

        var clock = Stopwatch.StartNew();
        var items = TextDiff.Of(older.ToString(), newer.ToString());
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        items.Count.ShouldBe(40);
        items.ShouldAllBe(i => i.Pieces.Count(p => p.Change == TextChange.Removed) == 1 && i.Pieces.Count(p => p.Change == TextChange.Inserted) == 1);
    }

    [Fact]
    public void Beyond_the_cap_the_changed_part_is_replaced_whole_and_quickly()
    {
        // One paragraph of about 30 000 tokens a side, changed at both ends: too much to compare word by word.
        var middle = string.Join(' ', Enumerable.Range(0, 15_000).Select(w => w % 2 == 0 ? "a" : "b"));
        var older = "Pradžia " + middle + " pabaiga";
        var newer = "Kita " + middle + " galas";
        var clock = Stopwatch.StartNew();
        var pieces = TextDiff.Words(older, newer);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        pieces.Select(p => p.Change).ShouldBe([TextChange.Removed, TextChange.Inserted]);
        (Older(pieces), Newer(pieces)).ShouldBe((older, newer));
    }
}
