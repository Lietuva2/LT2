using Lt2.Web.Client.Components;

namespace Lt2.UnitTests;

/// <summary>Design 8.25.4 (owner, 2026-11-19): the initials badge's letters and colour, as pure rules.</summary>
public sealed class MemberBadgesTests
{
    [Theory]
    [InlineData("Rūta Mockutė", "RM")]
    [InlineData("ąžuolas", "Ą")]
    [InlineData("Ąžuolas Šimkus", "ĄŠ")]
    [InlineData("rūta mockutė", "RM")]
    [InlineData("Jonas", "J")]
    [InlineData("Jonas Antanas Petraitis", "JP")]
    [InlineData("  Ona   Jonaitienė  ", "OJ")]
    [InlineData("Ona-Marija Jonaitienė", "OJ")]
    [InlineData("„Rūta“ (Mockutė)", "RM")]
    [InlineData("Rūta 2 Mockutė", "RM")]
    public void The_initials_are_the_first_letters_of_the_first_and_of_the_last_word_in_upper_case(string name, string expected) =>
        MemberBadges.Initials(name).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("- 42 !")]
    public void A_name_with_no_letter_has_no_initials_and_so_no_badge(string? name) =>
        MemberBadges.Initials(name).ShouldBeNull();

    [Fact]
    public void A_word_without_a_letter_is_not_counted_as_the_last_word() =>
        MemberBadges.Initials("Rūta 42").ShouldBe("R");

    [Fact]
    public void The_palette_has_eight_colours() => MemberBadges.PaletteSize.ShouldBe(8);

    [Fact]
    public void The_same_public_id_always_has_the_same_colour_which_is_fixed_by_the_hash_and_not_by_the_process()
    {
        // FNV-1a values worked out by hand: a runtime's string hash would differ between runs and these would fail.
        MemberBadges.Colour("").ShouldBe(5);
        MemberBadges.Colour("a").ShouldBe(4);
        MemberBadges.Colour("7k3qm2x9ab").ShouldBe(MemberBadges.Colour("7k3qm2x9ab"));
    }

    [Fact]
    public void Every_colour_is_within_the_palette_and_ids_spread_over_all_of_it()
    {
        var alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var random = new Random(7);
        var seen = new HashSet<int>();
        for (var i = 0; i < 400; i++)
        {
            var id = new string(Enumerable.Range(0, 10).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var colour = MemberBadges.Colour(id);
            colour.ShouldBeInRange(0, MemberBadges.PaletteSize - 1);
            seen.Add(colour);
        }

        seen.Count.ShouldBe(MemberBadges.PaletteSize);
    }
}
