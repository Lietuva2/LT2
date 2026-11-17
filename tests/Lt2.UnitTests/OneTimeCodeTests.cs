using Lt2.SharedKernel;

namespace Lt2.UnitTests;

/// <summary>
/// Design 6.8 (owner, 2026-11-17): the typeable one-time code of an office invitation, an MP's take-over and an organisation
/// representative's invitation: twelve characters of an unambiguous alphabet in three groups, at least 50 bits, typed in any
/// case with or without spaces and dashes, stored only as a hash.
/// </summary>
public sealed class OneTimeCodeTests
{
    [Fact]
    public void A_code_is_twelve_unambiguous_characters_in_three_groups_of_four()
    {
        var code = OneTimeCode.New();
        code.ShouldMatch("^[23456789ABCDEFGHJKMNPQRSTVWXYZ]{4}-[23456789ABCDEFGHJKMNPQRSTVWXYZ]{4}-[23456789ABCDEFGHJKMNPQRSTVWXYZ]{4}$");
        OneTimeCode.Alphabet.ShouldNotContain('0');
        OneTimeCode.Alphabet.ShouldNotContain('1');
        foreach (var letter in "ILOU")
        {
            OneTimeCode.Alphabet.ShouldNotContain(letter);
        }
    }

    [Fact]
    public void A_code_holds_more_than_fifty_bits() =>
        (OneTimeCode.Length * Math.Log2(OneTimeCode.Alphabet.Length)).ShouldBeGreaterThan(50);

    [Fact]
    public void Codes_are_random() =>
        Enumerable.Range(0, 1000).Select(_ => OneTimeCode.New()).Distinct().Count().ShouldBe(1000);

    [Theory]
    [InlineData("ABCD-EFGH-JKMN")]
    [InlineData("abcd-efgh-jkmn")]
    [InlineData("ABCDEFGHJKMN")]
    [InlineData(" abcd efgh jkmn ")]
    [InlineData("ABCD–EFGH–JKMN")]
    public void A_code_is_typed_in_any_case_with_or_without_spaces_and_dashes(string typed)
    {
        OneTimeCode.Normalize(typed).ShouldBe("ABCD-EFGH-JKMN");
        OneTimeCode.Hash(typed).ShouldBe(OneTimeCode.Hash("ABCD-EFGH-JKMN"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ABCD-EFGH-JKM")]
    [InlineData("ABCD-EFGH-JKMNP")]
    [InlineData("ABCD-EFGH-JKM0")]
    [InlineData("ABCD-EFGH-JKMO")]
    [InlineData("ABCD-EFGH-JKMI")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Anything_else_is_no_code(string? typed) =>
        OneTimeCode.Normalize(typed).ShouldBeNull();

    [Fact]
    public void Only_a_hash_is_stored_and_it_differs_per_code()
    {
        var code = OneTimeCode.New();
        OneTimeCode.Hash(code).Length.ShouldBe(32);
        OneTimeCode.Hash(code).ShouldNotBe(OneTimeCode.Hash(OneTimeCode.New()));
    }
}
