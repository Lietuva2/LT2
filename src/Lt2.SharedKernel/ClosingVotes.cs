namespace Lt2.SharedKernel;

/// <summary>
/// Which Seimas votes closed a bill's record (design 8.1 step 6), for the Parliament module's correction of a vote's
/// classification (design 7.2): the closing vote of a record is never corrected out of being the adoption-stage result,
/// since a record closed by the vote is never reopened. Implemented by the Appeals module.
/// </summary>
public interface IClosingVotes
{
    /// <summary>True when a bill's record, in whatever state now (closed, or hidden after it closed), was closed by this vote.</summary>
    Task<bool> ClosesARecordAsync(long voteId, CancellationToken cancellationToken);
}
