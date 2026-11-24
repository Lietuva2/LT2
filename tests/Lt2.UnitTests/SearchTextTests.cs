using Lt2.SharedKernel;
using Shouldly;
using Xunit;

namespace Lt2.UnitTests;

/// <summary>Design 8.12.5: the one folding the bill search's stored copy and the query's words go through.</summary>
public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Ąžuolaitis", "azuolaitis")]
    [InlineData("Čiurlionis", "ciurlionis")]
    [InlineData("ČIURLIONIS", "ciurlionis")]
    [InlineData("įstatymo pataisa", "istatymo pataisa")]
    [InlineData("Šeima ir ŽŪKIS", "seima ir zukis")]
    public void The_fold_ignores_case_and_Lithuanian_letters(string text, string folded) => SearchText.Fold(text).ShouldBe(folded);

    [Fact]
    public void The_fold_keeps_a_bill_number_as_it_is_in_lower_case() => SearchText.Fold("XVP-0412").ShouldBe("xvp-0412");

    [Fact]
    public void The_fold_makes_every_run_of_white_space_one_space_and_trims() =>
        SearchText.Fold("  Gyventojų \t pajamų\r\n  mokesčio  ").ShouldBe("gyventoju pajamu mokescio");

    [Fact]
    public void The_fold_of_a_blank_text_is_empty() => SearchText.Fold("  \t ").ShouldBe("");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public void A_blank_query_has_no_words(string? query) => SearchText.Words(query).ShouldBeEmpty();

    [Fact]
    public void The_words_of_a_query_are_split_on_white_space_with_no_empty_ones() =>
        SearchText.Words("  pajamų   XVP-0412\tmokesčio ").ShouldBe(["pajamų", "XVP-0412", "mokesčio"]);
}
