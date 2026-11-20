using Lt2.SharedKernel;

namespace Lt2.Parliament;

/// <summary>
/// A vote's classification as the moderators see it (design 7.2): the one in force (<paramref name="Kind"/>,
/// <paramref name="Stage"/>), the rules' own, and the moderator's correction when there is one (when and why; never who).
/// Codes are <see cref="VoteKinds"/> and <see cref="BillStages"/>.
/// </summary>
/// <param name="ClosesRecord">The vote closed a bill's record (design 8.1 step 6): it stays the adoption-stage result.</param>
public sealed record VoteClassificationState(
    long VoteId, string Kind, string? Stage, string RulesKind, string? RulesStage, DateTimeOffset? CorrectedAt, string? Statement, bool ClosesRecord)
{
    public bool Corrected => CorrectedAt is not null;
}

/// <summary>
/// Design 7.2: a moderator corrects a vote's kind (and, for a stage result, its stage), or returns it to the rules'
/// classification. Only those: the totals, the ballots and the wording are Seimas data. Each needs a statement, is audited
/// (<c>vote.reclassified</c>, <c>vote.classification-reset</c>) and wins over the rules on every read and every re-parse.
/// The closing vote of a bill's record is never corrected out of being the adoption-stage result.
/// </summary>
public interface IVoteClassifications
{
    /// <summary>The vote's classification for the moderators; <c>vote.not-found</c>.</summary>
    Task<Result<VoteClassificationState>> StateAsync(long voteId, CancellationToken cancellationToken);

    /// <summary>
    /// Corrects the vote to <paramref name="kind"/> (<see cref="VoteKinds"/>), with <paramref name="stage"/>
    /// (<see cref="BillStages"/>) for a stage result; an amendment takes its agenda item's stage, other kinds none.
    /// </summary>
    Task<Result<Unit>> CorrectAsync(long voteId, string kind, string? stage, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>"Grąžinti taisyklių klasifikaciją": the correction is removed and the rules' classification is in force again.</summary>
    Task<Result<Unit>> ResetAsync(long voteId, string statement, StaffActor moderator, CancellationToken cancellationToken);
}
