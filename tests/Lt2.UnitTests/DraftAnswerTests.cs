using Lt2.Appeals.Domain;

namespace Lt2.UnitTests;

/// <summary>
/// Design 8.11.4, 8.13, 8.13.1: the AI answers with the readable title, an empty line, the description, an empty line,
/// "Šaltinis: N" naming one of the documents sent and "Paaiškinimas:" with the public statement; or with the one "not
/// enough" sentence.
/// </summary>
public sealed class DraftAnswerTests
{
    private const string Title = "Didinamas neapmokestinamųjų pajamų dydis";
    private const string Description = "Projektas didintų neapmokestinamųjų pajamų dydį. Pataisa įsigaliotų nuo 2027 m. sausio 1 d.";
    private const string Statement = "Perskaitytas aiškinamasis raštas.";

    /// <summary>What follows the description in a valid answer: the source's line and the statement.</summary>
    private static string Tail(int source = 1, string statement = Statement) => $"\n\nŠaltinis: {source}\nPaaiškinimas: {statement}";

    [Fact]
    public void A_title_an_empty_line_a_description_a_source_and_a_statement_are_a_valid_answer()
    {
        var read = DraftAnswers.Read(Title + "\n\n" + Description + Tail(), documents: 1);
        read.Kind.ShouldBe(DraftAnswerKind.Valid);
        read.Title.ShouldBe(Title);
        read.Description.ShouldBe(Description);
        read.Source.ShouldBe(1);
        read.Statement.ShouldBe(Statement);
    }

    [Fact]
    public void A_statement_of_several_lines_with_a_list_is_kept_as_written_and_the_source_names_the_second_document()
    {
        var answer = Title + "\n\n" + Description + "\n\nŠaltinis: 2\nPaaiškinimas:\nPerskaityti aiškinamasis raštas ir projekto tekstas. Praleista:\n- Vyriausybės argumentai už projektą\n- Specialiųjų tyrimų tarnybos kritika";
        var read = DraftAnswers.Read(answer, documents: 2);
        read.Kind.ShouldBe(DraftAnswerKind.Valid);
        read.Source.ShouldBe(2);
        read.Description.ShouldBe(Description);
        read.Statement.ShouldBe("Perskaityti aiškinamasis raštas ir projekto tekstas. Praleista:\n- Vyriausybės argumentai už projektą\n- Specialiųjų tyrimų tarnybos kritika");
    }

    [Fact]
    public void Windows_line_ends_and_outer_white_space_are_read_the_same()
    {
        var read = DraftAnswers.Read("  \r\n" + Title + "\r\n\r\n" + Description + Tail().Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n ", documents: 1);
        read.Kind.ShouldBe(DraftAnswerKind.Valid);
        read.Title.ShouldBe(Title);
        read.Description.ShouldBe(Description);
        read.Statement.ShouldBe(Statement);
    }

    [Fact]
    public void A_line_with_only_white_space_is_the_empty_line_and_a_description_of_several_paragraphs_is_kept()
    {
        var read = DraftAnswers.Read(Title + "\n  \n" + Description + "\n\nAntra pastraipa." + Tail(), documents: 1);
        read.Kind.ShouldBe(DraftAnswerKind.Valid);
        read.Description.ShouldBe(Description + "\n\nAntra pastraipa.");
    }

    [Fact]
    public void One_final_full_stop_of_the_title_is_removed()
    {
        DraftAnswers.Read(Title + ".\n\n" + Description + Tail(), 1).Title.ShouldBe(Title);
        DraftAnswers.Read(Title + "..\n\n" + Description + Tail(), 1).Title.ShouldBe(Title + ".", "only one");
    }

    [Fact]
    public void An_answer_without_an_empty_line_is_invalid()
    {
        DraftAnswers.Read(Title + "\n" + Description + Tail(), 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(Description, 1).Kind.ShouldBe(DraftAnswerKind.Invalid, "a description alone has no title");
        DraftAnswers.Read("", 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(null, 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
    }

    [Fact]
    public void A_title_of_over_ninety_characters_is_invalid()
    {
        DraftAnswers.Read(new string('x', 91) + "\n\n" + Description + Tail(), 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(new string('x', 90) + "\n\n" + Description + Tail(), 1).Kind.ShouldBe(DraftAnswerKind.Valid);
        DraftAnswers.Read(new string('x', 90) + ".\n\n" + Description + Tail(), 1).Kind.ShouldBe(DraftAnswerKind.Valid, "the full stop is removed before the length is counted");
    }

    [Fact]
    public void A_title_of_two_lines_before_the_empty_line_is_invalid()
    {
        DraftAnswers.Read("Pirma eilutė\nAntra eilutė\n\n" + Description + Tail(), 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
    }

    [Fact]
    public void An_empty_title_is_invalid()
    {
        DraftAnswers.Read(".\n\n" + Description + Tail(), 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
    }

    [Fact]
    public void The_not_enough_sentence_alone_is_not_enough_with_or_without_quotes()
    {
        DraftAnswers.Read(SummaryRules.NotEnoughAnswer, 1).Kind.ShouldBe(DraftAnswerKind.NotEnough);
        DraftAnswers.Read($"„{SummaryRules.NotEnoughAnswer}“", 2).Kind.ShouldBe(DraftAnswerKind.NotEnough);
        DraftAnswers.Read("\n" + SummaryRules.NotEnoughAnswer + "\n", 1).Kind.ShouldBe(DraftAnswerKind.NotEnough);
    }

    [Fact]
    public void A_description_that_is_too_long_or_missing_is_invalid()
    {
        DraftAnswers.Read(Title + "\n\n" + new string('x', AppealRules.DescriptionMaxLength + 1) + Tail(), 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(Title + "\n\n" + new string('x', AppealRules.DescriptionMaxLength) + Tail(), 1).Kind.ShouldBe(DraftAnswerKind.Valid);
        DraftAnswers.Read(Title + "\n\n" + Tail().TrimStart('\n'), 1).Kind.ShouldBe(DraftAnswerKind.Invalid, "nothing between the empty line and the source");
    }

    [Fact]
    public void An_answer_without_the_source_line_is_invalid()
    {
        DraftAnswers.Read(Title + "\n\n" + Description, 1).Kind.ShouldBe(DraftAnswerKind.Invalid, "the old two-part form");
        DraftAnswers.Read(Title + "\n\n" + Description + "\n\nPaaiškinimas: " + Statement, 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(Title + "\n\n" + Description + "\n\nŠaltinis: pirmas\nPaaiškinimas: " + Statement, 1).Kind.ShouldBe(DraftAnswerKind.Invalid, "not a number");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(0, 2)]
    [InlineData(3, 2)]
    public void A_source_that_names_no_document_sent_is_invalid(int source, int documents) =>
        DraftAnswers.Read(Title + "\n\n" + Description + Tail(source), documents).Kind.ShouldBe(DraftAnswerKind.Invalid);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    public void A_source_that_names_a_document_sent_is_read(int source, int documents) =>
        DraftAnswers.Read(Title + "\n\n" + Description + Tail(source), documents).Source.ShouldBe(source);

    [Fact]
    public void An_answer_without_the_statement_label_is_invalid()
    {
        DraftAnswers.Read(Title + "\n\n" + Description + "\n\nŠaltinis: 1", 1).Kind.ShouldBe(DraftAnswerKind.Invalid, "nothing after the source");
        DraftAnswers.Read(Title + "\n\n" + Description + "\n\nŠaltinis: 1\n" + Statement, 1).Kind.ShouldBe(DraftAnswerKind.Invalid, "a statement without its label");
    }

    [Fact]
    public void An_empty_statement_is_invalid()
    {
        DraftAnswers.Read(Title + "\n\n" + Description + "\n\nŠaltinis: 1\nPaaiškinimas:", 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(Title + "\n\n" + Description + "\n\nŠaltinis: 1\nPaaiškinimas:   \n  ", 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
    }

    [Fact]
    public void A_statement_of_over_a_thousand_characters_is_invalid()
    {
        DraftAnswers.Read(Title + "\n\n" + Description + Tail(statement: new string('x', StatementRules.MaxLength + 1)), 1).Kind.ShouldBe(DraftAnswerKind.Invalid);
        DraftAnswers.Read(Title + "\n\n" + Description + Tail(statement: new string('x', StatementRules.MaxLength)), 1).Kind.ShouldBe(DraftAnswerKind.Valid);
        StatementRules.MaxLength.ShouldBe(1000);
    }
}
