using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Parliament;

/// <summary>
/// Design 7.2: a moderator corrects a vote's classification. <c>Kind</c> is one of <see cref="VoteKinds"/>; <c>Stage</c> (one
/// of <see cref="BillStages"/>) only for a stage result, where it is required; the statement is required. An amendment takes
/// its agenda item's stage, a procedural or other vote none.
/// </summary>
public sealed record VoteClassificationRequest
{
    public string Kind { get; init; } = "";

    public string? Stage { get; init; }

    public string Statement { get; init; } = "";
}

/// <summary>Design 7.2: "Grąžinti taisyklių klasifikaciją", with a required statement.</summary>
public sealed record VoteClassificationResetRequest
{
    public string Statement { get; init; } = "";
}

/// <summary>
/// A vote's classification for the moderators (design 7.2): the one in force, the rules' own, and, when a moderator corrected
/// it, when and the statement (never who). <c>ClosesRecord</c>: the vote closed a bill's record, so it stays the adoption-stage
/// result and is not offered for correction.
/// </summary>
public sealed record VoteClassificationResponse(
    long VoteId, string Kind, string? Stage, string RulesKind, string? RulesStage, bool Corrected, DateTimeOffset? CorrectedAt, string? Statement, bool ClosesRecord);

public sealed class VoteClassificationRequestValidator : AbstractValidator<VoteClassificationRequest>
{
    public VoteClassificationRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Kind).Must(k => VoteKinds.All.Contains(k)).WithMessage(l["VoteClassification.Kind"]).OverridePropertyName("kind");
        RuleFor(x => x.Stage)
            .Must(s => s is not null && BillStages.All.Contains(s)).When(x => x.Kind == VoteKinds.StageResult).WithMessage(l["VoteClassification.Stage"])
            .OverridePropertyName("stage");
        RuleFor(x => x.Stage)
            .Null().When(x => x.Kind != VoteKinds.StageResult).WithMessage(l["VoteClassification.NoStage"])
            .OverridePropertyName("stage");
        RuleFor(x => x.Statement).VoteStatement(l);
    }
}

public sealed class VoteClassificationResetRequestValidator : AbstractValidator<VoteClassificationResetRequest>
{
    public VoteClassificationResetRequestValidator(IStringLocalizer<ValidationMessages> l) => RuleFor(x => x.Statement).VoteStatement(l);
}

internal static class VoteModerationValidation
{
    /// <summary>The moderator's statement (design 8.3): required, at most <see cref="Appeals.AppealLimits.StatementMaxLength"/> characters.</summary>
    public static IRuleBuilderOptions<T, string> VoteStatement<T>(this IRuleBuilderInitial<T, string> rule, IStringLocalizer l) =>
        rule.Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["VoteClassification.Statement"])
            .Must(t => t.Trim().Length <= Appeals.AppealLimits.StatementMaxLength).WithMessage(l["Statement.TooLong"])
            .OverridePropertyName("statement");
}
