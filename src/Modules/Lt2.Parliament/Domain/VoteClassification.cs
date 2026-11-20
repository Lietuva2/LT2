using Lt2.Parliament.Import.Parsing;

namespace Lt2.Parliament.Domain;

/// <summary>What a vote decided (design 7.2): its kind and, for a stage result or an amendment, the bill's stage.</summary>
internal readonly record struct Classification(VoteKind Kind, BillStage? Stage)
{
    /// <summary>The vote of a bill's adoption stage: the one that closes the bill's record (design 8.1 step 6).</summary>
    public bool IsAdoptionResult => Kind == VoteKind.StageResult && Stage == BillStage.Adoption;
}

/// <summary>
/// Design 7.2: a vote's classification is the rules' (<see cref="VoteRules.Classify"/>) unless a moderator corrected it, and
/// then the moderator's, on every read and on every re-parse. One pure function decides it (<see cref="Effective"/>); the
/// import and the moderator's actions write its result to the vote's <c>kind</c> and <c>stage</c>, which every reader reads.
/// The refusals of a correction are pure too.
/// </summary>
internal static class VoteClassification
{
    /// <summary>The vote is not the closing vote of a record any more (design 8.1 step 6: a record closed by the vote is never reopened).</summary>
    public const string ClosingVote = "vote-classification.closing-vote";

    /// <summary>The correction asks for the classification the vote has already.</summary>
    public const string Unchanged = "vote-classification.unchanged";

    /// <summary>The correction asks for the rules' own classification: that is "Grąžinti taisyklių klasifikaciją".</summary>
    public const string SameAsRules = "vote-classification.rules";

    /// <summary>Returning to the rules' classification when no moderator corrected it.</summary>
    public const string NotCorrected = "vote-classification.not-corrected";

    /// <summary>A stage result without a stage, or a stage that is none of the three.</summary>
    public const string StageRequired = "vote-classification.stage";

    /// <summary>A kind that is none of the four.</summary>
    public const string KindInvalid = "vote-classification.kind";

    /// <summary>The classification in force: the moderator's correction when there is one, otherwise the rules'.</summary>
    public static Classification Effective(Classification rules, Classification? correction) => correction ?? rules;

    /// <summary>The stored correction, or null when the columns hold none.</summary>
    public static Classification? CorrectionOf(VoteKind? kind, BillStage? stage) => kind is { } k ? new Classification(k, stage) : null;

    /// <summary>
    /// The correction a moderator's choice makes: a stage result at the chosen stage; an amendment at the agenda item's stage,
    /// as the rules place one; a procedural or other vote without a stage. Null when a stage result has no stage.
    /// </summary>
    public static Classification? Corrected(VoteKind kind, BillStage? chosenStage, BillStage? itemStage) => kind switch
    {
        VoteKind.StageResult => chosenStage is { } stage ? new Classification(kind, stage) : null,
        VoteKind.Amendment => new Classification(kind, itemStage),
        _ => new Classification(kind, null),
    };

    /// <summary>
    /// Why a correction to <paramref name="requested"/> is refused, or null: the vote already has it; it is the closing vote of
    /// a record and would stop being the adoption-stage result; it is the rules' own classification (returned to by its own
    /// action, so "corrected" always means "differs from the rules").
    /// </summary>
    public static string? CheckCorrection(Classification current, Classification rules, Classification requested, bool closesRecord) =>
        requested == current ? Unchanged
        : closesRecord && !requested.IsAdoptionResult ? ClosingVote
        : requested == rules ? SameAsRules
        : null;

    /// <summary>
    /// Why returning to the rules' classification is refused, or null: nobody corrected the vote; it is the closing vote of a
    /// record and the rules do not make it the adoption-stage result.
    /// </summary>
    public static string? CheckReturn(bool corrected, Classification rules, bool closesRecord) =>
        !corrected ? NotCorrected
        : closesRecord && !rules.IsAdoptionResult ? ClosingVote
        : null;
}
