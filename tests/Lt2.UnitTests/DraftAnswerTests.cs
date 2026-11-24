using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>Design 8.11.4, 8.13.1: the AI answers with the readable title, an empty line and the description, or with the one "not enough" sentence.</summary>
public sealed class DraftAnswerTests
{
    private const string Title = "Didinamas neapmokestinamųjų pajamų dydis";
    private const string Description = "Projektas didintų neapmokestinamųjų pajamų dydį. Pataisa įsigaliotų nuo 2027 m. sausio 1 d.";

    [Fact]
    public void A_title_an_empty_line_and_a_description_are_a_valid_answer()
    {
        var read = DraftAnswers.Read(Title + "\n\n" + Description);
        read.Kind.ShouldBe(DraftAnswerKind.Valid);
        read.Title.ShouldBe(Title);
        read.Description.ShouldBe(Description);
    }

    [Fact]
    public void Windows_line_ends_and_outer_white_space_are_read_the_same()
    {
        var read = DraftAnswers.Read("  \r\n" + Title + "\r\n\r\n" + Description + "\r\n ");
        read.Kind.ShouldBe(DraftAnswerKind.Valid);
        read.Title.ShouldBe(Title);
        read.Description.ShouldBe(Description);
    }

    [Fact]
    public void A_line_with_only_white_space_is_the_empty_line_and_a_description_of_several_paragraphs_is_kept()
    {
        var read = DraftAnswers.Read(Title + "\n  \n" + Description + "\n\nAntra pastraipa.");
        read.Kind.ShouldBe(DraftAnswerKind.Valid);
        read.Description.ShouldBe(Description + "\n\nAntra pastraipa.");
    }

    [Fact]
    public void One_final_full_stop_of_the_title_is_removed()
    {
        DraftAnswers.Read(Title + ".\n\n" + Description).Title.ShouldBe(Title);
        DraftAnswers.Read(Title + "..\n\n" + Description).Title.ShouldBe(Title + ".", "only one");
    }

    [Fact]
    public void An_answer_without_an_empty_line_is_invalid()
    {
        DraftAnswers.Read(Title + "\n" + Description).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(Description).Kind.ShouldBe(DraftAnswerKind.Invalid, "a description alone has no title");
        DraftAnswers.Read("").Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(null).Kind.ShouldBe(DraftAnswerKind.Invalid);
    }

    [Fact]
    public void A_title_of_over_ninety_characters_is_invalid()
    {
        DraftAnswers.Read(new string('x', 91) + "\n\n" + Description).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(new string('x', 90) + "\n\n" + Description).Kind.ShouldBe(DraftAnswerKind.Valid);
        DraftAnswers.Read(new string('x', 90) + ".\n\n" + Description).Kind.ShouldBe(DraftAnswerKind.Valid, "the full stop is removed before the length is counted");
    }

    [Fact]
    public void A_title_of_two_lines_before_the_empty_line_is_invalid()
    {
        DraftAnswers.Read("Pirma eilutė\nAntra eilutė\n\n" + Description).Kind.ShouldBe(DraftAnswerKind.Invalid);
    }

    [Fact]
    public void An_empty_title_is_invalid()
    {
        DraftAnswers.Read(".\n\n" + Description).Kind.ShouldBe(DraftAnswerKind.Invalid);
    }

    [Fact]
    public void The_not_enough_sentence_alone_is_not_enough_with_or_without_quotes()
    {
        DraftAnswers.Read(SummaryRules.NotEnoughAnswer).Kind.ShouldBe(DraftAnswerKind.NotEnough);
        DraftAnswers.Read($"„{SummaryRules.NotEnoughAnswer}“").Kind.ShouldBe(DraftAnswerKind.NotEnough);
        DraftAnswers.Read("\n" + SummaryRules.NotEnoughAnswer + "\n").Kind.ShouldBe(DraftAnswerKind.NotEnough);
    }

    [Fact]
    public void A_description_that_is_too_long_or_missing_is_invalid()
    {
        DraftAnswers.Read(Title + "\n\n" + new string('x', AppealRules.DescriptionMaxLength + 1)).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(Title + "\n\n" + new string('x', AppealRules.DescriptionMaxLength)).Kind.ShouldBe(DraftAnswerKind.Valid);
        DraftAnswers.Read(Title + "\n\n").Kind.ShouldBe(DraftAnswerKind.Invalid, "nothing after the empty line");
    }
}
