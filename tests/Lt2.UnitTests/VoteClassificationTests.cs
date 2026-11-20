using Lt2.Api.Contracts.Parliament;
using Lt2.Parliament.Domain;
using Lt2.Parliament.Import.Parsing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lt2.UnitTests;

/// <summary>
/// Design 7.2 (owner, 2026-11-20): a moderator corrects a vote's classification. The correction wins over the rules on every
/// read and re-parse (<see cref="VoteClassification.Effective"/>), "corrected" always differs from the rules, and the closing
/// vote of a bill's record stays the adoption-stage result, since a record closed by the vote is never reopened.
/// </summary>
public sealed class VoteClassificationTests
{
    private static readonly Classification Adoption = new(VoteKind.StageResult, BillStage.Adoption);
    private static readonly Classification Consideration = new(VoteKind.StageResult, BillStage.Consideration);
    private static readonly Classification Amendment = new(VoteKind.Amendment, BillStage.Consideration);
    private static readonly Classification Other = new(VoteKind.Other, null);

    private static readonly IStringLocalizer<Lt2.Api.Contracts.ValidationMessages> L = new StringLocalizer<Lt2.Api.Contracts.ValidationMessages>(
        new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    [Fact]
    public void The_effective_classification_is_the_moderators_correction_when_there_is_one_and_the_rules_otherwise()
    {
        VoteClassification.Effective(Other, null).ShouldBe(Other);
        VoteClassification.Effective(Other, Adoption).ShouldBe(Adoption);
        VoteClassification.Effective(Adoption, Amendment).ShouldBe(Amendment);
        VoteClassification.Effective(Adoption, VoteClassification.CorrectionOf(null, BillStage.Presentation)).ShouldBe(Adoption, "no kind: no correction");
        VoteClassification.Effective(Adoption, VoteClassification.CorrectionOf(VoteKind.Procedural, null)).ShouldBe(new Classification(VoteKind.Procedural, null));
    }

    [Fact]
    public void A_stage_result_takes_the_chosen_stage_an_amendment_its_agenda_items_and_other_kinds_none()
    {
        VoteClassification.Corrected(VoteKind.StageResult, BillStage.Adoption, BillStage.Consideration).ShouldBe(Adoption);
        VoteClassification.Corrected(VoteKind.StageResult, null, BillStage.Consideration).ShouldBeNull("a stage result needs its stage");
        VoteClassification.Corrected(VoteKind.Amendment, BillStage.Adoption, BillStage.Consideration).ShouldBe(Amendment);
        VoteClassification.Corrected(VoteKind.Procedural, BillStage.Adoption, BillStage.Consideration).ShouldBe(new Classification(VoteKind.Procedural, null));
        VoteClassification.Corrected(VoteKind.Other, null, BillStage.Adoption).ShouldBe(Other);
    }

    [Fact]
    public void A_correction_to_another_classification_than_the_rules_and_the_current_one_is_allowed()
    {
        VoteClassification.CheckCorrection(Other, Other, Adoption, closesRecord: false).ShouldBeNull("into the adoption stage: the closing job closes the record");
        VoteClassification.CheckCorrection(Adoption, Adoption, Consideration, closesRecord: false).ShouldBeNull("out of it, while no record closed by it");
        VoteClassification.CheckCorrection(Amendment, Other, Consideration, closesRecord: false).ShouldBeNull("a corrected vote corrected again");
    }

    [Fact]
    public void A_correction_to_what_the_vote_has_already_or_to_the_rules_own_classification_is_refused()
    {
        VoteClassification.CheckCorrection(Adoption, Adoption, Adoption, closesRecord: false).ShouldBe(VoteClassification.Unchanged);
        VoteClassification.CheckCorrection(Amendment, Other, Amendment, closesRecord: false).ShouldBe(VoteClassification.Unchanged);
        VoteClassification.CheckCorrection(Amendment, Other, Other, closesRecord: false).ShouldBe(VoteClassification.SameAsRules);
    }

    [Fact]
    public void The_closing_vote_of_a_record_is_never_corrected_out_of_the_adoption_stage()
    {
        VoteClassification.CheckCorrection(Adoption, Adoption, Consideration, closesRecord: true).ShouldBe(VoteClassification.ClosingVote);
        VoteClassification.CheckCorrection(Adoption, Other, Other, closesRecord: true).ShouldBe(VoteClassification.ClosingVote);
        VoteClassification.CheckCorrection(Adoption, Adoption, Adoption, closesRecord: true).ShouldBe(VoteClassification.Unchanged);
    }

    [Fact]
    public void Returning_to_the_rules_needs_a_correction_and_keeps_a_closing_vote_the_adoption_stage_result()
    {
        VoteClassification.CheckReturn(corrected: true, Other, closesRecord: false).ShouldBeNull();
        VoteClassification.CheckReturn(corrected: false, Other, closesRecord: false).ShouldBe(VoteClassification.NotCorrected);
        VoteClassification.CheckReturn(corrected: true, Other, closesRecord: true).ShouldBe(VoteClassification.ClosingVote, "corrected into the adoption stage, closed a record, then back to the rules: refused");
        VoteClassification.CheckReturn(corrected: true, Adoption, closesRecord: true).ShouldBeNull("the rules make it the adoption-stage result too");
    }

    [Theory]
    [InlineData("stage-result", "adoption", "Klaidinga formuluotė.", null)]
    [InlineData("amendment", null, "Tai pasiūlymas.", null)]
    [InlineData("procedural", null, "Darbotvarkė.", null)]
    [InlineData("other", null, "Kita.", null)]
    [InlineData("stage-result", null, "Be stadijos.", "stage")]
    [InlineData("stage-result", "final", "Netinkama stadija.", "stage")]
    [InlineData("amendment", "adoption", "Stadija pasiūlymui.", "stage")]
    [InlineData("result", null, "Netinkama rūšis.", "kind")]
    [InlineData("", null, "Be rūšies.", "kind")]
    [InlineData("other", null, " ", "statement")]
    public void The_request_names_one_of_the_four_kinds_a_stage_only_for_a_stage_result_and_a_statement(string kind, string? stage, string statement, string? field)
    {
        var result = new VoteClassificationRequestValidator(L).Validate(new VoteClassificationRequest { Kind = kind, Stage = stage, Statement = statement });
        if (field is null)
        {
            result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.PropertyName + ": " + e.ErrorMessage)));
        }
        else
        {
            result.Errors.Select(e => e.PropertyName).ShouldBe([field]);
        }
    }

    [Fact]
    public void Returning_to_the_rules_needs_a_statement()
    {
        var validator = new VoteClassificationResetRequestValidator(L);
        validator.Validate(new VoteClassificationResetRequest { Statement = "" }).Errors.Select(e => e.PropertyName).ShouldBe(["statement"]);
        validator.Validate(new VoteClassificationResetRequest { Statement = new string('a', 1001) }).Errors.Select(e => e.PropertyName).ShouldBe(["statement"]);
        validator.Validate(new VoteClassificationResetRequest { Statement = "Taisyklės buvo teisios." }).IsValid.ShouldBeTrue();
    }
}
