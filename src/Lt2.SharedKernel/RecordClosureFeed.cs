namespace Lt2.SharedKernel;

/// <summary>
/// A bill's record a moderator closed because the Seimas rejected the bill or its initiators withdrew it (design 8.1 step 6), as
/// the follow emails tell it (design 8.6): the bill, <paramref name="Reason"/> (<c>rejected</c> or <c>withdrawn</c>), the day of
/// that decision, the source on lrs.lt and when the record closed. No person: who closed it is never told.
/// </summary>
public sealed record RecordClosureFacts(string BillNumber, string Reason, DateOnly DecidedOn, string SourceUrl, DateTimeOffset ClosedAt);

/// <summary>The moderators' closes of bills' records for the follow emails (design 8.1 step 6, 8.6). Implemented by the Appeals module.</summary>
public interface IRecordClosureFeed
{
    /// <summary>
    /// The records closed now with the reason <c>rejected</c> or <c>withdrawn</c> whose close happened in [<paramref name="from"/>,
    /// <paramref name="to"/>). A record reopened meanwhile, closed by a vote or hidden by the moderators is left out.
    /// </summary>
    Task<IReadOnlyList<RecordClosureFacts>> ClosedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
