using Lt2.Appeals.Domain;
using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Appeals.Application;

/// <summary>
/// A bill's last activity (design 8.12.5, owner 2026-11-17), the order "Naujausia veikla" of <c>/projektai</c>: written in the
/// caller's transaction (the context's own, so it commits or rolls back with the action), on the bill's open record only. One
/// time per bill: who acted is never stored with it.
/// </summary>
internal static class RecordActivity
{
    /// <summary>On a record, by its id (a side, an argument).</summary>
    public static Task TouchAsync(AppealsDb db, Guid recordId, DateTimeOffset at, CancellationToken ct)
    {
        var when = at.ToUniversalTime();
        return db.BillRecords.Where(r => r.Id == recordId && r.Status == BillRecordStatuses.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastActivityAt, when), ct);
    }

    /// <summary>
    /// On a bill's open record, by the bill's number (a comment, an organisation's statement, an MP's response): a bill without
    /// a record has none to mark, and nothing is created for it.
    /// </summary>
    public static Task TouchBillAsync(AppealsDb db, string billNumber, DateTimeOffset at, CancellationToken ct)
    {
        var when = at.ToUniversalTime();
        return db.BillRecords.Where(r => r.BillNumber == billNumber && r.Status == BillRecordStatuses.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastActivityAt, when), ct);
    }
}
